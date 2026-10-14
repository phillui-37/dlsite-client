using System.Globalization;
using System.Text.Json;
using DlsiteClient.Core.Api;
using DlsiteClient.Core.Models;
using DlsiteClient.Core.Serialization;
using DlsiteClient.Core.Storage;
using Microsoft.Extensions.Logging;

namespace DlsiteClient.Core.Services;

public sealed class LibraryService
{
    private readonly LibraryDatabase _db;
    private readonly DlsiteApiClient _api;
    private readonly AuthSession _auth;
    private readonly SettingsService _settings;
    private readonly ILogger<LibraryService> _logger;
    private List<WorkItem> _cache = [];
    private readonly Dictionary<string, string> _workTypeNames = new(StringComparer.OrdinalIgnoreCase);
    private bool _dlcFeatureEnabled;
    private bool _tagsFromProductJson;

    public LibraryService(
        LibraryDatabase db,
        DlsiteApiClient api,
        AuthSession auth,
        SettingsService settings,
        ILogger<LibraryService> logger)
    {
        _db = db;
        _api = api;
        _auth = auth;
        _settings = settings;
        _logger = logger;
        Reload();
    }

    public IReadOnlyList<WorkItem> Works => _cache;
    public bool DlcFeatureEnabled => _dlcFeatureEnabled;
    public event Action? Changed;

    public void Reload()
    {
        _cache = _db.GetAllWorks().ToList();
        Changed?.Invoke();
    }

    public async Task SyncPurchasesAsync(CancellationToken ct = default)
    {
        try
        {
            var locale = _settings.Current.ApiLocale;
            var products = await _api.FetchAllPurchasesAsync(locale, ct).ConfigureAwait(false);
            _logger.LogInformation("Fetched {Count} purchases", products.Count);

            var existing = _db.GetAllWorks().ToDictionary(w => w.Workno);
            var mapped = new List<WorkItem>();
            var tagShapeLogged = false;

            foreach (var p in products)
            {
                if (string.IsNullOrWhiteSpace(p.Workno)) continue;
                existing.TryGetValue(p.Workno, out var prev);
                var tagsJson = NormalizeTags(p.Tags, ref tagShapeLogged);
                mapped.Add(new WorkItem
                {
                    Workno = p.Workno,
                    WorkName = p.WorkName ?? "",
                    MakerName = p.MakerName ?? "",
                    WorkType = p.WorkType ?? "",
                    SiteId = p.SiteId ?? "",
                    ContentsFileSize = p.ContentsFileSize,
                    Point = p.Point,
                    RegistDate = p.RegistDate,
                    SalesDate = p.SalesDate,
                    UpdateDate = p.UpdateDate,
                    ThumbUrl = p.ImageUrls?.Thum ?? p.ImageUrls?.Main,
                    MainImageUrl = p.ImageUrls?.Main,
                    TagsJson = tagsJson,
                    IsFavorite = prev?.IsFavorite ?? false,
                    IsHidden = prev?.IsHidden ?? false,
                    IsDownloaded = prev?.IsDownloaded ?? false,
                    InstallPath = prev?.InstallPath,
                    SelectedExe = prev?.SelectedExe,
                    UseJapanLocale = prev?.UseJapanLocale ?? false,
                    LocalUpdateDate = prev?.LocalUpdateDate,
                    ParentWorkno = prev?.ParentWorkno,
                    IsDlc = prev?.IsDlc ?? false
                });
            }

            _db.UpsertWorks(mapped);
            await EnsureWorkTypeNamesAsync(ct).ConfigureAwait(false);
            await EnrichSampleMetadataAsync(mapped.Take(5).ToList(), ct).ConfigureAwait(false);
            await ReconcileLocalInstallsAsync(ct).ConfigureAwait(false);
        }
        catch (NestSessionExpiredException)
        {
            await _auth.InvalidateLocalSessionAsync(ct).ConfigureAwait(false);
            throw;
        }
    }

    /// <summary>
    /// Match Nest layout Game/{maker}/{workName} (and root {maker}/{workName}, {workno}).
    /// </summary>
    public async Task ReconcileLocalInstallsAsync(CancellationToken ct = default)
    {
        var libRoot = _settings.Current.LibraryDirectory;
        if (string.IsNullOrWhiteSpace(libRoot) || !Directory.Exists(libRoot))
        {
            Reload();
            return;
        }

        var works = _db.GetAllWorks().ToList();
        var changed = 0;
        await Task.Run(() =>
        {
            foreach (var w in works)
            {
                ct.ThrowIfCancellationRequested();
                var found = ResolveInstallPath(libRoot, w);
                if (found is not null)
                {
                    var localDate = ResolveLocalUpdateDate(w, found);
                    if (!w.IsDownloaded
                        || !string.Equals(w.InstallPath, found, StringComparison.OrdinalIgnoreCase)
                        || !string.Equals(w.LocalUpdateDate, localDate, StringComparison.Ordinal))
                    {
                        _db.SetDownloaded(w.Workno, true, found, localDate);
                        changed++;
                    }
                }
                else if (w.IsDownloaded)
                {
                    _db.SetDownloaded(w.Workno, false, null, null);
                    changed++;
                }
            }
        }, ct).ConfigureAwait(false);

        if (changed > 0)
            _logger.LogInformation("Reconciled {Count} local install flags under {Root}", changed, libRoot);
        Reload();
    }

    /// <summary>
    /// Nest stores works under {libraryDirectory}/Game. Fall back to library root when Game is absent.
    /// </summary>
    public static string ResolveContentRoot(string libRoot)
    {
        var game = Path.Combine(libRoot, "Game");
        return Directory.Exists(game) ? game : libRoot;
    }

    public static IEnumerable<string> EnumerateContentRoots(string libRoot)
    {
        yield return libRoot;
        var game = Path.Combine(libRoot, "Game");
        if (Directory.Exists(game)
            && !string.Equals(
                Path.GetFullPath(game).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                Path.GetFullPath(libRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                StringComparison.OrdinalIgnoreCase))
        {
            yield return game;
        }
    }

    public static string? ResolveInstallPath(string libRoot, WorkItem w)
    {
        if (!string.IsNullOrWhiteSpace(w.InstallPath) && Directory.Exists(w.InstallPath))
            return w.InstallPath;

        var maker = SanitizePathSegment(w.MakerName);
        var title = SanitizePathSegment(w.WorkName);

        foreach (var root in EnumerateContentRoots(libRoot))
        {
            var found = ResolveInstallPathUnderRoot(root, w.Workno, maker, title);
            if (found is not null)
                return found;
        }

        return null;
    }

    private static string? ResolveInstallPathUnderRoot(string root, string workno, string maker, string title)
    {
        if (!string.IsNullOrWhiteSpace(workno))
        {
            var byWorkno = Path.Combine(root, workno);
            if (Directory.Exists(byWorkno))
                return byWorkno;
        }

        if (maker.Length == 0 || title.Length == 0)
            return null;

        var nestPath = Path.Combine(root, maker, title);
        if (Directory.Exists(nestPath))
            return nestPath;

        // Case-insensitive walk when exact combine misses (rare path normalization).
        try
        {
            foreach (var makerDir in Directory.EnumerateDirectories(root))
            {
                if (!string.Equals(Path.GetFileName(makerDir), maker, StringComparison.OrdinalIgnoreCase))
                    continue;
                foreach (var workDir in Directory.EnumerateDirectories(makerDir))
                {
                    if (string.Equals(Path.GetFileName(workDir), title, StringComparison.OrdinalIgnoreCase))
                        return workDir;
                }
            }
        }
        catch (Exception)
        {
            // IO errors → treat as not found
        }

        return null;
    }

    public static string SanitizePathSegment(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "";
        var invalid = Path.GetInvalidFileNameChars();
        var chars = name.Trim().ToCharArray();
        for (var i = 0; i < chars.Length; i++)
        {
            if (Array.IndexOf(invalid, chars[i]) >= 0)
                chars[i] = '_';
        }
        return new string(chars).TrimEnd('.', ' ');
    }

    /// <summary>
    /// Local revision for HasUpdate. Keep a real stamp; never assume Nest imports equal remote update_date.
    /// </summary>
    public static string? ResolveLocalUpdateDate(WorkItem w, string installPath)
    {
        var inferred = InferDirectoryStamp(installPath);
        if (string.IsNullOrWhiteSpace(w.LocalUpdateDate))
            return inferred;

        // Prior detect bug stamped LocalUpdateDate = UpdateDate. If folder is older, trust folder.
        if (string.Equals(w.LocalUpdateDate, w.UpdateDate, StringComparison.Ordinal)
            && !string.IsNullOrWhiteSpace(w.UpdateDate)
            && !string.IsNullOrWhiteSpace(inferred)
            && WorkItem.TryParseNestDate(w.UpdateDate, out var remote)
            && WorkItem.TryParseNestDate(inferred, out var folder)
            && folder < remote)
        {
            return inferred;
        }

        return w.LocalUpdateDate;
    }

    /// <summary>Max LastWriteTime among install root entries, Nest-shaped local timestamp.</summary>
    public static string? InferDirectoryStamp(string path)
    {
        try
        {
            var dir = new DirectoryInfo(path);
            if (!dir.Exists) return null;
            var max = dir.LastWriteTime;
            foreach (var entry in dir.EnumerateFileSystemInfos())
            {
                if (entry.LastWriteTime > max)
                    max = entry.LastWriteTime;
            }
            return max.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
        }
        catch (Exception)
        {
            return null;
        }
    }

    public async Task EnsureWorkTypeNamesAsync(CancellationToken ct = default)
    {
        if (_workTypeNames.Count > 0) return;
        try
        {
            var types = await _api.GetWorkTypesAsync(_settings.Current.ApiLocale, ct).ConfigureAwait(false);
            foreach (var wt in types)
            {
                if (string.IsNullOrWhiteSpace(wt.Value)) continue;
                var name = !string.IsNullOrWhiteSpace(wt.Name) ? wt.Name! : wt.Value!;
                _workTypeNames[wt.Value!] = name;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load Nest work-type names");
        }
    }

    /// <summary>Localized Nest name when available; otherwise the raw code.</summary>
    public string WorkTypeDisplay(string? code)
    {
        if (string.IsNullOrWhiteSpace(code)) return "-";
        return _workTypeNames.TryGetValue(code, out var name) && !string.IsNullOrWhiteSpace(name)
            ? name
            : code;
    }

    private async Task EnrichSampleMetadataAsync(List<WorkItem> sample, CancellationToken ct)
    {
        foreach (var w in sample)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                using var doc = await _api.GetProductJsonAsync(w.Workno, ct).ConfigureAwait(false);
                if (doc is null) continue;
                var root = doc.RootElement;
                // product.json is often an array
                var item = root.ValueKind == JsonValueKind.Array && root.GetArrayLength() > 0
                    ? root[0]
                    : root;

                if (w.TagsJson is "[]" or null)
                {
                    if (TryExtractGenres(item, out var genres))
                    {
                        w.TagsJson = JsonSerializer.Serialize(genres, AppJsonContext.Default.ListString);
                        _tagsFromProductJson = true;
                        _db.UpsertWorks([w]);
                    }
                }

                if (TryExtractDlc(item, out var isDlc, out var parent))
                {
                    _dlcFeatureEnabled = true;
                    _db.SetDlc(w.Workno, isDlc, parent);
                    _logger.LogInformation(
                        "DLC probe OK for {Workno}: isDlc={IsDlc} parent={Parent}",
                        w.Workno, isDlc, parent);
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Enrich failed for {Workno}", w.Workno);
            }
        }
    }

    private static bool TryExtractGenres(JsonElement item, out List<string> genres)
    {
        genres = [];
        if (item.TryGetProperty("genres", out var g) && g.ValueKind == JsonValueKind.Array)
        {
            foreach (var el in g.EnumerateArray())
            {
                if (el.ValueKind == JsonValueKind.Object && el.TryGetProperty("name", out var n))
                    genres.Add(n.GetString() ?? "");
                else if (el.ValueKind == JsonValueKind.String)
                    genres.Add(el.GetString() ?? "");
            }
            return genres.Count > 0;
        }
        if (item.TryGetProperty("genre", out var g2) && g2.ValueKind == JsonValueKind.Array)
        {
            foreach (var el in g2.EnumerateArray())
                genres.Add(el.GetString() ?? el.ToString());
            return genres.Count > 0;
        }
        return false;
    }

    private static bool TryExtractDlc(JsonElement item, out bool isDlc, out string? parent)
    {
        isDlc = false;
        parent = null;
        var packChild = item.TryGetProperty("is_pack_child", out var pc) && pc.ValueKind == JsonValueKind.True;
        var bonus = item.TryGetProperty("is_bonus_work", out var bw) && bw.ValueKind == JsonValueKind.True;
        if (!packChild && !bonus) return false;
        isDlc = true;
        if (item.TryGetProperty("work_pack_parent", out var pp))
        {
            if (pp.ValueKind == JsonValueKind.String)
                parent = pp.GetString();
            else if (pp.ValueKind == JsonValueKind.Array && pp.GetArrayLength() > 0)
                parent = pp[0].GetString();
        }
        return true;
    }

    private string NormalizeTags(JsonElement tags, ref bool logged)
    {
        try
        {
            if (tags.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
                return "[]";
            if (!logged)
            {
                _logger.LogInformation("Nest Product.tags shape: {Kind} sample={Sample}",
                    tags.ValueKind, tags.ToString()[..Math.Min(200, tags.ToString().Length)]);
                logged = true;
            }
            if (tags.ValueKind == JsonValueKind.Array)
            {
                var names = new List<string>();
                foreach (var el in tags.EnumerateArray())
                {
                    if (el.ValueKind == JsonValueKind.String) names.Add(el.GetString()!);
                    else if (el.ValueKind == JsonValueKind.Object && el.TryGetProperty("name", out var n))
                        names.Add(n.GetString() ?? "");
                }
                return JsonSerializer.Serialize(names, AppJsonContext.Default.ListString);
            }
            return tags.GetRawText();
        }
        catch
        {
            return "[]";
        }
    }

    public IReadOnlyList<string> DistinctWorkTypes() =>
        _cache.Select(w => w.WorkType)
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(t => t, StringComparer.OrdinalIgnoreCase)
            .ToList();

    public IReadOnlyList<string> DistinctTags()
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var w in _cache)
        {
            foreach (var tag in ParseTags(w.TagsJson))
                if (!string.IsNullOrWhiteSpace(tag))
                    set.Add(tag);
        }
        return set.OrderBy(t => t, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public static IReadOnlyList<string> ParseTags(string? tagsJson)
    {
        if (string.IsNullOrWhiteSpace(tagsJson) || tagsJson == "[]")
            return [];
        try
        {
            var arr = JsonSerializer.Deserialize(tagsJson, AppJsonContext.Default.StringArray);
            return arr ?? [];
        }
        catch
        {
            return [];
        }
    }

    public IReadOnlyList<WorkItem> Query(
        MediaFilter media,
        string? search,
        bool favoritesOnly,
        bool showHidden,
        bool downloadedOnly,
        string? tagFilter,
        IReadOnlyCollection<string>? workTypeFilter = null)
    {
        IEnumerable<WorkItem> q = _cache;
        if (!showHidden) q = q.Where(w => !w.IsHidden);
        if (favoritesOnly) q = q.Where(w => w.IsFavorite);
        if (downloadedOnly) q = q.Where(w => w.IsDownloaded);
        q = q.Where(w => MatchMedia(w.WorkType, media));
        if (workTypeFilter is { Count: > 0 })
        {
            var allow = new HashSet<string>(workTypeFilter, StringComparer.OrdinalIgnoreCase);
            q = q.Where(w => allow.Contains(w.WorkType));
        }
        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim();
            q = q.Where(w => Fuzzy(w.WorkName, s) || Fuzzy(w.MakerName, s));
        }
        if (!string.IsNullOrWhiteSpace(tagFilter))
        {
            q = q.Where(w => ParseTags(w.TagsJson)
                .Any(t => t.Contains(tagFilter, StringComparison.OrdinalIgnoreCase)));
        }
        return q.ToList();
    }

    public static bool Fuzzy(string haystack, string needle)
    {
        if (string.IsNullOrEmpty(needle)) return true;
        if (haystack.Contains(needle, StringComparison.OrdinalIgnoreCase)) return true;
        // simple subsequence fuzzy
        var hi = 0;
        foreach (var c in needle)
        {
            hi = haystack.IndexOf(c.ToString(), hi, StringComparison.OrdinalIgnoreCase);
            if (hi < 0) return false;
            hi++;
        }
        return true;
    }

    public static bool MatchMedia(string workType, MediaFilter filter)
    {
        if (filter == MediaFilter.All) return true;
        var t = workType.ToUpperInvariant();
        return filter switch
        {
            MediaFilter.Games => t is "GAME" or "ACN" or "ADV" or "RPG" or "SLN" or "STG" or "TBL" or "TYP" or "PZL" or "ETC",
            MediaFilter.Images => t is "CG" or "ILL" or "MNG" or "IME" or "NR2",
            MediaFilter.Videos => t is "MOV" or "VDO",
            MediaFilter.Audio => t is "SOU" or "MUS" or "VOICE",
            MediaFilter.Other => !MatchMedia(workType, MediaFilter.Games)
                                 && !MatchMedia(workType, MediaFilter.Images)
                                 && !MatchMedia(workType, MediaFilter.Videos)
                                 && !MatchMedia(workType, MediaFilter.Audio),
            _ => true
        };
    }

    public void SetFavorite(string workno, bool v) { _db.SetFavorite(workno, v); Reload(); }
    public void SetHidden(string workno, bool v) { _db.SetHidden(workno, v); Reload(); }

    public void SetJapanLocale(string workno, bool useJapanLocale)
    {
        var w = _db.GetWork(workno);
        _db.SetSelectedExe(workno, w?.SelectedExe, useJapanLocale);
        Reload();
    }

    public async Task MarkUninstalledAsync(string workno, CancellationToken ct = default)
    {
        var w = _db.GetWork(workno);
        if (w?.InstallPath is { } path && Directory.Exists(path))
        {
            try
            {
                await Task.Run(() => Directory.Delete(path, true), ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Uninstall delete failed for {Path}", path);
            }
        }
        _db.SetDownloaded(workno, false, null, null);
        _db.SetSelectedExe(workno, null, w?.UseJapanLocale ?? false);
        Reload();
    }

    public void ClearSelectedExe(string workno)
    {
        var w = _db.GetWork(workno);
        if (w is null) return;
        _db.SetSelectedExe(workno, null, w.UseJapanLocale);
        Reload();
    }
}
