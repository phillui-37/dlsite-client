namespace DlsiteClient.Core.Models;

public enum LibraryViewMode
{
    List,
    Grid,
    Hybrid
}

public enum MediaFilter
{
    All,
    Games,
    Images,
    Videos,
    Audio,
    Other
}

public sealed class WorkItem
{
    public required string Workno { get; init; }
    public string WorkName { get; set; } = "";
    public string MakerName { get; set; } = "";
    public string WorkType { get; set; } = "";
    public string SiteId { get; set; } = "";
    public long ContentsFileSize { get; set; }
    public int Point { get; set; }
    public string? RegistDate { get; set; }
    public string? SalesDate { get; set; }
    public string? UpdateDate { get; set; }
    public string? ThumbUrl { get; set; }
    public string? MainImageUrl { get; set; }
    public string TagsJson { get; set; } = "[]";
    public bool IsFavorite { get; set; }
    public bool IsHidden { get; set; }
    public bool IsDownloaded { get; set; }
    public string? InstallPath { get; set; }
    public string? SelectedExe { get; set; }
    public bool UseJapanLocale { get; set; }
    public string? LocalUpdateDate { get; set; }
    public string? ParentWorkno { get; set; }
    public bool IsDlc { get; set; }

    /// <summary>Store rating as x/5 text; Nest `point` probed as 0–50 → /10, else clamp.</summary>
    public string RatingText
    {
        get
        {
            if (Point <= 0) return "0/5";
            var stars = Point > 5 ? Point / 10.0 : Point;
            if (stars > 5) stars = 5;
            return $"{stars:0.#}/5";
        }
    }

    public bool HasUpdate =>
        IsDownloaded
        && !string.IsNullOrEmpty(UpdateDate)
        && !string.Equals(UpdateDate, LocalUpdateDate, StringComparison.Ordinal);
}

public sealed class AppSettings
{
    public string LibraryDirectory { get; set; } = "";
    public string CacheDirectory { get; set; } = "";
    public string LogDirectory { get; set; } = "";
    public string UiLocale { get; set; } = "ja";
    public string ApiLocale { get; set; } = "ja_JP";
    public bool StartOnLogin { get; set; }
    public int MaxConcurrentDownloads { get; set; } = 2;
    public int MaxConcurrentExtracts { get; set; } = 1;
    public double RateLimitMBps { get; set; } // 0 = unlimited
    public string ProxyMode { get; set; } = "off"; // off | system | custom
    public string? ProxyUrl { get; set; }
    public string LogLevel { get; set; } = "Information";
    public LibraryViewMode ViewMode { get; set; } = LibraryViewMode.List;
    public string VisibleColumns { get; set; } =
        "purchaseDate,title,maker,workType,rating,salesDate,downloaded,extractSize,update";
    /// <summary>Library page size: 20 (default), 50, or 100.</summary>
    public int LibraryPageSize { get; set; } = 20;
    public bool NestImportAsked { get; set; }
}

public enum DownloadJobState
{
    Queued,
    Downloading,
    Paused,
    Extracting,
    Completed,
    Failed,
    CorruptArchive
}

public sealed class DownloadJob
{
    public required string Workno { get; init; }
    public string WorkName { get; set; } = "";
    public string? ThumbUrl { get; set; }
    public DownloadJobState State { get; set; } = DownloadJobState.Queued;
    public double ProgressPercent { get; set; }
    public long BytesDownloaded { get; set; }
    public long BytesTotal { get; set; }
    public string? CurrentFileName { get; set; }
    public string? ErrorMessage { get; set; }
    public int QueueOrder { get; set; }
    public bool IsUpdate { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
}
