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
    private readonly SettingsService _settings;
    private readonly ILogger<LibraryService> _logger;
    private List<WorkItem> _cache = [];
    private bool _dlcFeatureEnabled;
    private bool _tagsFromProductJson;

    public LibraryService(
        LibraryDatabase db,
        DlsiteApiClient api,
        SettingsService settings,
        ILogger<LibraryService> logger)
    {
        _db = db;
        _api = api;
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
        await EnrichSampleMetadataAsync(mapped.Take(5).ToList(), ct).ConfigureAwait(false);
        Reload();
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
        string? tagFilter)
    {
        IEnumerable<WorkItem> q = _cache;
        if (!showHidden) q = q.Where(w => !w.IsHidden);
        if (favoritesOnly) q = q.Where(w => w.IsFavorite);
        if (downloadedOnly) q = q.Where(w => w.IsDownloaded);
        q = q.Where(w => MatchMedia(w.WorkType, media));
        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim();
            q = q.Where(w => Fuzzy(w.WorkName, s) || Fuzzy(w.MakerName, s));
        }
        if (!string.IsNullOrWhiteSpace(tagFilter))
        {
            q = q.Where(w => w.TagsJson.Contains(tagFilter, StringComparison.OrdinalIgnoreCase));
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

    public void MarkUninstalled(string workno)
    {
        var w = _db.GetWork(workno);
        if (w?.InstallPath is { } path && Directory.Exists(path))
        {
            try { Directory.Delete(path, true); }
            catch (Exception ex) { _logger.LogWarning(ex, "Uninstall delete failed for {Path}", path); }
        }
        _db.SetDownloaded(workno, false, null, null);
        Reload();
    }
}
