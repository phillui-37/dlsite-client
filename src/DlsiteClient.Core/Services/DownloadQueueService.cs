using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Threading.Channels;
using DlsiteClient.Core.Api;
using DlsiteClient.Core.Models;
using DlsiteClient.Core.Storage;
using Microsoft.Extensions.Logging;

namespace DlsiteClient.Core.Services;

public sealed class DownloadQueueService : IAsyncDisposable
{
    private readonly DlsiteApiClient _api;
    private readonly SettingsService _settings;
    private readonly LibraryDatabase _db;
    private readonly LibraryService _library;
    private readonly ArchiveExtractor _extractor;
    private readonly ILogger<DownloadQueueService> _logger;
    private readonly List<DownloadJob> _jobs = [];
    private readonly object _gate = new();
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _cts = new();
    private readonly ConcurrentDictionary<string, byte> _extractOnly = new();
    private readonly ConcurrencyGate _downloadSlots;
    private readonly ConcurrencyGate _extractSlots;
    private readonly Channel<string> _wake = Channel.CreateUnbounded<string>();
    private readonly Task _loop;
    private readonly CancellationTokenSource _runCts = new();
    private long _lastProgressPersistTicks;

    public DownloadQueueService(
        DlsiteApiClient api,
        SettingsService settings,
        LibraryDatabase db,
        LibraryService library,
        ArchiveExtractor extractor,
        ILogger<DownloadQueueService> logger)
    {
        _api = api;
        _settings = settings;
        _db = db;
        _library = library;
        _extractor = extractor;
        _logger = logger;
        _downloadSlots = new ConcurrencyGate(settings.Current.MaxConcurrentDownloads);
        _extractSlots = new ConcurrencyGate(settings.Current.MaxConcurrentExtracts);
        ApplyProxyFromSettings();
        _settings.Changed += OnSettingsChanged;
        foreach (var j in db.LoadDownloadJobs())
        {
            if (j.State is DownloadJobState.Downloading or DownloadJobState.Extracting)
                j.State = DownloadJobState.Queued; // resume after restart
            _jobs.Add(j);
        }
        _loop = Task.Run(() => WorkerLoopAsync(_runCts.Token));
        _ = _wake.Writer.TryWrite("start");
    }

    public event Action? Changed;
    public double CurrentSpeedBps { get; private set; }
    public double MaxSpeedBps { get; private set; }
    public long SessionBytes { get; private set; }

    public IReadOnlyList<DownloadJob> Active =>
        Snapshot(j => j.State is DownloadJobState.Downloading or DownloadJobState.Extracting);

    public IReadOnlyList<DownloadJob> UpNext =>
        Snapshot(j => j.State is DownloadJobState.Queued or DownloadJobState.Paused)
            .OrderBy(j => j.QueueOrder).ToList();

    public IReadOnlyList<DownloadJob> Completed =>
        Snapshot(j => j.State is DownloadJobState.Completed or DownloadJobState.Failed
            or DownloadJobState.CorruptArchive);

    public bool HasCachedArchive(string workno)
    {
        var cacheDir = Path.Combine(_settings.Current.CacheDirectory, workno);
        return ArchiveExtractor.FindPrimaryArchive(cacheDir) is not null;
    }

    private List<DownloadJob> Snapshot(Func<DownloadJob, bool> pred)
    {
        lock (_gate) return _jobs.Where(pred).Select(Clone).ToList();
    }

    private static DownloadJob Clone(DownloadJob j) => new()
    {
        Workno = j.Workno,
        WorkName = j.WorkName,
        ThumbUrl = j.ThumbUrl,
        State = j.State,
        ProgressPercent = j.ProgressPercent,
        BytesDownloaded = j.BytesDownloaded,
        BytesTotal = j.BytesTotal,
        CurrentFileName = j.CurrentFileName,
        ErrorMessage = j.ErrorMessage,
        QueueOrder = j.QueueOrder,
        IsUpdate = j.IsUpdate,
        CompletedAt = j.CompletedAt
    };

    public void Enqueue(IEnumerable<WorkItem> works, bool isUpdate)
    {
        lock (_gate)
        {
            var order = _jobs.Count == 0 ? 0 : _jobs.Max(j => j.QueueOrder) + 1;
            foreach (var w in works)
            {
                var existing = _jobs.FirstOrDefault(j => j.Workno == w.Workno
                    && j.State is not DownloadJobState.Completed);
                if (existing is not null) continue;
                _jobs.Add(new DownloadJob
                {
                    Workno = w.Workno,
                    WorkName = w.WorkName,
                    ThumbUrl = w.ThumbUrl,
                    State = DownloadJobState.Queued,
                    QueueOrder = order++,
                    IsUpdate = isUpdate,
                    BytesTotal = w.ContentsFileSize
                });
            }
            Persist(force: true);
        }
        Notify();
        _wake.Writer.TryWrite("enqueue");
    }

    public void ReorderUpNext(IReadOnlyList<string> worknosInOrder)
    {
        lock (_gate)
        {
            var i = 0;
            foreach (var w in worknosInOrder)
            {
                var job = _jobs.FirstOrDefault(j => j.Workno == w);
                if (job is not null) job.QueueOrder = i++;
            }
            Persist(force: true);
        }
        Notify();
    }

    public void MoveUpNext(string workno, int delta)
    {
        if (delta == 0) return;
        lock (_gate)
        {
            var up = _jobs
                .Where(j => j.State is DownloadJobState.Queued or DownloadJobState.Paused)
                .OrderBy(j => j.QueueOrder)
                .ToList();
            var idx = up.FindIndex(j => j.Workno == workno);
            if (idx < 0) return;
            var nidx = Math.Clamp(idx + delta, 0, up.Count - 1);
            if (nidx == idx) return;
            (up[idx].QueueOrder, up[nidx].QueueOrder) = (up[nidx].QueueOrder, up[idx].QueueOrder);
            Persist(force: true);
        }
        Notify();
    }

    public void Pause(string workno)
    {
        if (_cts.TryGetValue(workno, out var cts)) cts.Cancel();
        lock (_gate)
        {
            var j = _jobs.FirstOrDefault(x => x.Workno == workno);
            if (j is not null && j.State == DownloadJobState.Downloading)
                j.State = DownloadJobState.Paused;
            Persist(force: true);
        }
        Notify();
    }

    public void ResumeJob(string workno)
    {
        _extractOnly.TryRemove(workno, out _);
        lock (_gate)
        {
            var j = _jobs.FirstOrDefault(x => x.Workno == workno);
            if (j is null) return;
            if (j.State is DownloadJobState.Paused or DownloadJobState.Failed)
            {
                j.State = DownloadJobState.Queued;
                j.ErrorMessage = null;
            }
            Persist(force: true);
        }
        Notify();
        _wake.Writer.TryWrite("resume");
    }

    public void ClearCompleted()
    {
        lock (_gate)
        {
            _jobs.RemoveAll(j => j.State is DownloadJobState.Completed);
            Persist(force: true);
        }
        Notify();
    }

    public void RequestRedownload(string workno)
    {
        _extractOnly.TryRemove(workno, out _);
        var cacheDir = Path.Combine(_settings.Current.CacheDirectory, workno);
        try
        {
            if (Directory.Exists(cacheDir)) Directory.Delete(cacheDir, true);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to wipe cache for {Workno}", workno);
        }
        lock (_gate)
        {
            var j = _jobs.FirstOrDefault(x => x.Workno == workno);
            if (j is not null)
            {
                j.State = DownloadJobState.Queued;
                j.BytesDownloaded = 0;
                j.ProgressPercent = 0;
                j.ErrorMessage = null;
            }
            Persist(force: true);
        }
        Notify();
        _wake.Writer.TryWrite("redownload");
    }

    public void RequestRetryExtract(string workno)
    {
        _extractOnly[workno] = 0;
        lock (_gate)
        {
            var j = _jobs.FirstOrDefault(x => x.Workno == workno);
            if (j is null) return;
            j.State = DownloadJobState.Queued;
            j.ErrorMessage = null;
            if (j.ProgressPercent < 99) j.ProgressPercent = 99;
            Persist(force: true);
        }
        Notify();
        _wake.Writer.TryWrite("retry-extract");
    }

    private void OnSettingsChanged()
    {
        _downloadSlots.SetMax(_settings.Current.MaxConcurrentDownloads);
        _extractSlots.SetMax(_settings.Current.MaxConcurrentExtracts);
        ApplyProxyFromSettings();
        _wake.Writer.TryWrite("settings");
    }

    private void ApplyProxyFromSettings() =>
        _api.ConfigureProxy(_settings.Current.ProxyMode, _settings.Current.ProxyUrl);

    private async Task WorkerLoopAsync(CancellationToken ct)
    {
        try
        {
            await foreach (var _ in _wake.Reader.ReadAllAsync(ct).ConfigureAwait(false))
            {
                await PumpAsync(ct).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) { }
    }

    private async Task PumpAsync(CancellationToken ct)
    {
        List<DownloadJob> queued;
        lock (_gate)
            queued = _jobs.Where(j => j.State == DownloadJobState.Queued).OrderBy(j => j.QueueOrder).ToList();

        foreach (var job in queued)
        {
            if (ct.IsCancellationRequested) break;
            _ = Task.Run(() => RunJobAsync(job.Workno, ct), ct);
        }
    }

    private async Task RunJobAsync(string workno, CancellationToken ct)
    {
        if (!_cts.TryAdd(workno, CancellationTokenSource.CreateLinkedTokenSource(ct)))
            return;
        var linked = _cts[workno];
        var extractOnly = _extractOnly.TryRemove(workno, out _);
        try
        {
            var skipDownload = extractOnly && HasCachedArchive(workno);
            if (!skipDownload)
            {
                await _downloadSlots.WaitAsync(linked.Token).ConfigureAwait(false);
                try
                {
                    SetState(workno, DownloadJobState.Downloading);
                    var ok = await DownloadWorkAsync(workno, linked.Token).ConfigureAwait(false);
                    if (!ok) return;
                }
                finally
                {
                    _downloadSlots.Release();
                }
            }

            await _extractSlots.WaitAsync(linked.Token).ConfigureAwait(false);
            try
            {
                SetState(workno, DownloadJobState.Extracting);
                await ExtractWorkAsync(workno, linked.Token).ConfigureAwait(false);
            }
            finally
            {
                _extractSlots.Release();
            }
        }
        catch (OperationCanceledException)
        {
            lock (_gate)
            {
                var j = _jobs.FirstOrDefault(x => x.Workno == workno);
                if (j is not null && j.State != DownloadJobState.Paused)
                    j.State = DownloadJobState.Queued;
                Persist(force: true);
            }
            Notify();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Job failed {Workno}", workno);
            Fail(workno, ex.Message, DownloadJobState.Failed);
        }
        finally
        {
            if (_cts.TryRemove(workno, out var cts)) cts.Dispose();
            _wake.Writer.TryWrite("next");
        }
    }

    private async Task<bool> DownloadWorkAsync(string workno, CancellationToken ct)
    {
        var detail = await _api.GetProductDetailAsync(workno, _settings.Current.ApiLocale, ct)
            .ConfigureAwait(false);
        var files = detail?.ContentsWithUrl?.Where(f => !string.IsNullOrEmpty(f.Url)).ToList() ?? [];
        if (files.Count == 0)
        {
            Fail(workno, "No download URLs in product detail", DownloadJobState.Failed);
            return false;
        }

        var cacheDir = Path.Combine(_settings.Current.CacheDirectory, workno);
        Directory.CreateDirectory(cacheDir);
        long total = 0, done = 0;
        foreach (var f in files)
        {
            if (long.TryParse(f.FileSize, out var sz)) total += sz;
        }
        UpdateJob(workno, j => j.BytesTotal = total > 0 ? total : j.BytesTotal, persist: true);

        ApplyProxyFromSettings();
        using var client = _api.CreateDownloadClient();
        var limit = _settings.Current.RateLimitMBps;

        foreach (var f in files)
        {
            ct.ThrowIfCancellationRequested();
            var name = f.FileName ?? Path.GetFileName(new Uri(f.Url!).AbsolutePath);
            var dest = Path.Combine(cacheDir, name);
            UpdateJob(workno, j => j.CurrentFileName = name, persist: false);
            var existing = File.Exists(dest) ? new FileInfo(dest).Length : 0L;
            using var req = new HttpRequestMessage(HttpMethod.Get, f.Url);
            if (existing > 0)
                req.Headers.Range = new RangeHeaderValue(existing, null);

            using var res = await client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct)
                .ConfigureAwait(false);
            if (res.StatusCode == System.Net.HttpStatusCode.RequestedRangeNotSatisfiable)
            {
                done += existing;
                continue;
            }
            res.EnsureSuccessStatusCode();
            var append = res.StatusCode == System.Net.HttpStatusCode.PartialContent;
            await using var input = await res.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            await using var output = new FileStream(
                dest, append ? FileMode.Append : FileMode.Create, FileAccess.Write, FileShare.None, 81920, true);

            var buffer = new byte[81920];
            var sw = System.Diagnostics.Stopwatch.StartNew();
            long window = 0;
            while (true)
            {
                var read = await input.ReadAsync(buffer, ct).ConfigureAwait(false);
                if (read <= 0) break;
                await output.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
                done += read;
                window += read;
                SessionBytes += read;
                if (sw.ElapsedMilliseconds >= 200)
                {
                    CurrentSpeedBps = window / (sw.Elapsed.TotalSeconds + 0.001);
                    if (CurrentSpeedBps > MaxSpeedBps) MaxSpeedBps = CurrentSpeedBps;
                    window = 0;
                    sw.Restart();
                    var pct = total > 0 ? done * 100.0 / total : 0;
                    UpdateJob(workno, j =>
                    {
                        j.BytesDownloaded = done;
                        j.ProgressPercent = Math.Min(99, pct);
                    }, persist: false);
                }
                if (limit > 0)
                {
                    var maxBps = limit * 1024 * 1024;
                    var elapsed = sw.Elapsed.TotalSeconds + 0.001;
                    var allowed = maxBps * elapsed;
                    if (window > allowed)
                    {
                        var sleep = TimeSpan.FromSeconds((window - allowed) / maxBps);
                        if (sleep > TimeSpan.Zero)
                            await Task.Delay(sleep, ct).ConfigureAwait(false);
                    }
                }
            }
        }
        UpdateJob(workno, j => { j.BytesDownloaded = done; j.ProgressPercent = 99; }, persist: true);
        return true;
    }

    private async Task ExtractWorkAsync(string workno, CancellationToken ct)
    {
        var cacheDir = Path.Combine(_settings.Current.CacheDirectory, workno);
        var archive = ArchiveExtractor.FindPrimaryArchive(cacheDir);
        if (archive is null)
        {
            Fail(workno, "No archive in cache", DownloadJobState.Failed);
            return;
        }

        var libRoot = _settings.Current.LibraryDirectory;
        var work = _db.GetWork(workno);
        var maker = LibraryService.SanitizePathSegment(work?.MakerName);
        var title = LibraryService.SanitizePathSegment(work?.WorkName);
        // Nest layout {maker}/{workName}; fall back to workno when metadata missing.
        var dest = maker.Length > 0 && title.Length > 0
            ? Path.Combine(libRoot, maker, title)
            : Path.Combine(libRoot, workno);
        if (Directory.Exists(dest))
        {
            try { Directory.Delete(dest, true); }
            catch { /* best effort */ }
        }
        Directory.CreateDirectory(dest);

        var (ok, message) = await _extractor.ExtractAsync(archive, dest, ct).ConfigureAwait(false);
        if (!ok)
        {
            try { if (Directory.Exists(dest)) Directory.Delete(dest, true); } catch { /* ignore */ }
            var test = await _extractor.TestAsync(archive, ct).ConfigureAwait(false);
            if (test is ArchiveTestResult.Corrupt or ArchiveTestResult.IncompleteSplit)
            {
                Fail(workno, message, DownloadJobState.CorruptArchive);
                return;
            }
            // test OK / tool missing / unknown → keep cache; UI offers retry extract
            Fail(workno, message, DownloadJobState.Failed);
            return;
        }

        _db.SetDownloaded(workno, true, dest, work?.UpdateDate);
        try
        {
            if (Directory.Exists(cacheDir)) Directory.Delete(cacheDir, true);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Cache cleanup failed for {Workno}", workno);
        }

        lock (_gate)
        {
            var j = _jobs.FirstOrDefault(x => x.Workno == workno);
            if (j is not null)
            {
                j.State = DownloadJobState.Completed;
                j.ProgressPercent = 100;
                j.CompletedAt = DateTimeOffset.Now;
                j.ErrorMessage = null;
            }
            Persist(force: true);
        }
        _library.Reload();
        Notify();
    }

    private void Fail(string workno, string message, DownloadJobState state)
    {
        lock (_gate)
        {
            var j = _jobs.FirstOrDefault(x => x.Workno == workno);
            if (j is not null)
            {
                j.State = state;
                j.ErrorMessage = message;
            }
            Persist(force: true);
        }
        Notify();
    }

    private void SetState(string workno, DownloadJobState state)
    {
        UpdateJob(workno, j => j.State = state, persist: true);
    }

    private void UpdateJob(string workno, Action<DownloadJob> mutate, bool persist)
    {
        lock (_gate)
        {
            var j = _jobs.FirstOrDefault(x => x.Workno == workno);
            if (j is null) return;
            mutate(j);
            if (persist)
                Persist(force: true);
            else
                Persist(force: false);
        }
        Notify();
    }

    private void Persist(bool force)
    {
        if (!force)
        {
            var now = Environment.TickCount64;
            if (now - _lastProgressPersistTicks < 2000)
                return;
            _lastProgressPersistTicks = now;
        }
        else
        {
            _lastProgressPersistTicks = Environment.TickCount64;
        }

        _db.SaveDownloadJobs(_jobs);
    }

    private void Notify() => Changed?.Invoke();

    public async ValueTask DisposeAsync()
    {
        _settings.Changed -= OnSettingsChanged;
        _runCts.Cancel();
        _wake.Writer.TryComplete();
        try { await _loop.ConfigureAwait(false); } catch { /* ignore */ }
        _runCts.Dispose();
    }
}
