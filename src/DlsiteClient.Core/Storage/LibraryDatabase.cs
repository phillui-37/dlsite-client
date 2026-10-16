using DlsiteClient.Core.Models;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace DlsiteClient.Core.Storage;

public sealed class LibraryDatabase : IDisposable
{
    private readonly string _dbPath;
    private readonly ILogger<LibraryDatabase> _logger;
    private SqliteConnection? _conn;

    public LibraryDatabase(string dbPath, ILogger<LibraryDatabase> logger)
    {
        _dbPath = dbPath;
        _logger = logger;
    }

    public void Initialize()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_dbPath)!);
        _conn = new SqliteConnection($"Data Source={_dbPath}");
        _conn.Open();
        using var cmd = _conn.CreateCommand();
        cmd.CommandText =
            """
            CREATE TABLE IF NOT EXISTS works (
              workno TEXT PRIMARY KEY,
              work_name TEXT NOT NULL,
              maker_name TEXT NOT NULL,
              work_type TEXT NOT NULL,
              site_id TEXT NOT NULL,
              contents_file_size INTEGER NOT NULL,
              point INTEGER NOT NULL,
              regist_date TEXT,
              sales_date TEXT,
              update_date TEXT,
              thumb_url TEXT,
              main_image_url TEXT,
              tags_json TEXT NOT NULL DEFAULT '[]',
              is_favorite INTEGER NOT NULL DEFAULT 0,
              is_hidden INTEGER NOT NULL DEFAULT 0,
              is_downloaded INTEGER NOT NULL DEFAULT 0,
              install_path TEXT,
              selected_exe TEXT,
              use_japan_locale INTEGER NOT NULL DEFAULT 0,
              local_update_date TEXT,
              parent_workno TEXT,
              is_dlc INTEGER NOT NULL DEFAULT 0
            );
            CREATE TABLE IF NOT EXISTS settings (
              key TEXT PRIMARY KEY,
              value TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS download_jobs (
              workno TEXT PRIMARY KEY,
              work_name TEXT NOT NULL,
              thumb_url TEXT,
              state TEXT NOT NULL,
              progress REAL NOT NULL,
              bytes_downloaded INTEGER NOT NULL,
              bytes_total INTEGER NOT NULL,
              current_file TEXT,
              error TEXT,
              queue_order INTEGER NOT NULL,
              is_update INTEGER NOT NULL,
              completed_at TEXT
            );
            """;
        cmd.ExecuteNonQuery();
    }

    private SqliteConnection Conn => _conn ?? throw new InvalidOperationException("DB not initialized");

    public void UpsertWorks(IEnumerable<WorkItem> works)
    {
        using var tx = Conn.BeginTransaction();
        foreach (var w in works)
        {
            using var cmd = Conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText =
                """
                INSERT INTO works (
                  workno, work_name, maker_name, work_type, site_id, contents_file_size, point,
                  regist_date, sales_date, update_date, thumb_url, main_image_url, tags_json,
                  is_favorite, is_hidden, is_downloaded, install_path, selected_exe, use_japan_locale,
                  local_update_date, parent_workno, is_dlc)
                VALUES (
                  $workno, $work_name, $maker_name, $work_type, $site_id, $size, $point,
                  $regist, $sales, $update, $thumb, $main, $tags,
                  $fav, $hidden, $dl, $install, $exe, $jp,
                  $local_update, $parent, $dlc)
                ON CONFLICT(workno) DO UPDATE SET
                  work_name=excluded.work_name,
                  maker_name=excluded.maker_name,
                  work_type=excluded.work_type,
                  site_id=excluded.site_id,
                  contents_file_size=excluded.contents_file_size,
                  point=excluded.point,
                  regist_date=excluded.regist_date,
                  sales_date=excluded.sales_date,
                  update_date=excluded.update_date,
                  thumb_url=excluded.thumb_url,
                  main_image_url=excluded.main_image_url,
                  tags_json=CASE WHEN excluded.tags_json='[]' THEN works.tags_json ELSE excluded.tags_json END,
                  parent_workno=COALESCE(excluded.parent_workno, works.parent_workno),
                  is_dlc=CASE WHEN excluded.is_dlc=1 THEN 1 ELSE works.is_dlc END;
                """;
            BindWork(cmd, w);
            cmd.ExecuteNonQuery();
        }
        tx.Commit();
    }

    public IReadOnlyList<WorkItem> GetAllWorks()
    {
        using var cmd = Conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM works ORDER BY regist_date DESC";
        using var r = cmd.ExecuteReader();
        var list = new List<WorkItem>();
        while (r.Read())
            list.Add(ReadWork(r));
        return list;
    }

    public WorkItem? GetWork(string workno)
    {
        using var cmd = Conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM works WHERE workno=$w";
        cmd.Parameters.AddWithValue("$w", workno);
        using var r = cmd.ExecuteReader();
        return r.Read() ? ReadWork(r) : null;
    }

    public void SetFavorite(string workno, bool value) =>
        ExecFlag(workno, "is_favorite", value);

    public void SetHidden(string workno, bool value) =>
        ExecFlag(workno, "is_hidden", value);

    public void SetTags(string workno, string tagsJson)
    {
        // Metadata fetches run in the background. Do not share the UI connection
        // or upsert a stale WorkItem over newer install/download state.
        using var connection = new SqliteConnection($"Data Source={_dbPath}");
        connection.Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "UPDATE works SET tags_json=$tags WHERE workno=$w";
        cmd.Parameters.AddWithValue("$tags", tagsJson);
        cmd.Parameters.AddWithValue("$w", workno);
        cmd.ExecuteNonQuery();
    }

    public void SetDownloaded(string workno, bool downloaded, string? installPath, string? localUpdateDate)
    {
        using var cmd = Conn.CreateCommand();
        cmd.CommandText =
            """
            UPDATE works SET is_downloaded=$d, install_path=$p, local_update_date=$u WHERE workno=$w
            """;
        cmd.Parameters.AddWithValue("$d", downloaded ? 1 : 0);
        cmd.Parameters.AddWithValue("$p", (object?)installPath ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$u", (object?)localUpdateDate ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$w", workno);
        cmd.ExecuteNonQuery();
    }

    public void SetSelectedExe(string workno, string? exe, bool useJapanLocale)
    {
        using var cmd = Conn.CreateCommand();
        cmd.CommandText = "UPDATE works SET selected_exe=$e, use_japan_locale=$j WHERE workno=$w";
        cmd.Parameters.AddWithValue("$e", (object?)exe ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$j", useJapanLocale ? 1 : 0);
        cmd.Parameters.AddWithValue("$w", workno);
        cmd.ExecuteNonQuery();
    }

    public void SetDlc(string workno, bool isDlc, string? parent)
    {
        using var cmd = Conn.CreateCommand();
        cmd.CommandText = "UPDATE works SET is_dlc=$d, parent_workno=$p WHERE workno=$w";
        cmd.Parameters.AddWithValue("$d", isDlc ? 1 : 0);
        cmd.Parameters.AddWithValue("$p", (object?)parent ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$w", workno);
        cmd.ExecuteNonQuery();
    }

    public void SetSetting(string key, string value)
    {
        using var cmd = Conn.CreateCommand();
        cmd.CommandText =
            "INSERT INTO settings(key,value) VALUES($k,$v) ON CONFLICT(key) DO UPDATE SET value=excluded.value";
        cmd.Parameters.AddWithValue("$k", key);
        cmd.Parameters.AddWithValue("$v", value);
        cmd.ExecuteNonQuery();
    }

    public string? GetSetting(string key)
    {
        using var cmd = Conn.CreateCommand();
        cmd.CommandText = "SELECT value FROM settings WHERE key=$k";
        cmd.Parameters.AddWithValue("$k", key);
        return cmd.ExecuteScalar() as string;
    }

    public void SaveDownloadJobs(IEnumerable<DownloadJob> jobs)
    {
        using var tx = Conn.BeginTransaction();
        using (var clear = Conn.CreateCommand())
        {
            clear.Transaction = tx;
            clear.CommandText = "DELETE FROM download_jobs";
            clear.ExecuteNonQuery();
        }
        foreach (var j in jobs)
        {
            using var cmd = Conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText =
                """
                INSERT INTO download_jobs VALUES (
                  $w,$n,$t,$s,$p,$bd,$bt,$cf,$e,$o,$u,$c)
                """;
            cmd.Parameters.AddWithValue("$w", j.Workno);
            cmd.Parameters.AddWithValue("$n", j.WorkName);
            cmd.Parameters.AddWithValue("$t", (object?)j.ThumbUrl ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$s", j.State.ToString());
            cmd.Parameters.AddWithValue("$p", j.ProgressPercent);
            cmd.Parameters.AddWithValue("$bd", j.BytesDownloaded);
            cmd.Parameters.AddWithValue("$bt", j.BytesTotal);
            cmd.Parameters.AddWithValue("$cf", (object?)j.CurrentFileName ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$e", (object?)j.ErrorMessage ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$o", j.QueueOrder);
            cmd.Parameters.AddWithValue("$u", j.IsUpdate ? 1 : 0);
            cmd.Parameters.AddWithValue("$c", (object?)j.CompletedAt?.ToString("O") ?? DBNull.Value);
            cmd.ExecuteNonQuery();
        }
        tx.Commit();
    }

    public IReadOnlyList<DownloadJob> LoadDownloadJobs()
    {
        using var cmd = Conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM download_jobs ORDER BY queue_order";
        using var r = cmd.ExecuteReader();
        var list = new List<DownloadJob>();
        while (r.Read())
        {
            list.Add(new DownloadJob
            {
                Workno = r.GetString(0),
                WorkName = r.GetString(1),
                ThumbUrl = r.IsDBNull(2) ? null : r.GetString(2),
                State = Enum.Parse<DownloadJobState>(r.GetString(3)),
                ProgressPercent = r.GetDouble(4),
                BytesDownloaded = r.GetInt64(5),
                BytesTotal = r.GetInt64(6),
                CurrentFileName = r.IsDBNull(7) ? null : r.GetString(7),
                ErrorMessage = r.IsDBNull(8) ? null : r.GetString(8),
                QueueOrder = r.GetInt32(9),
                IsUpdate = r.GetInt32(10) == 1,
                CompletedAt = r.IsDBNull(11) ? null : DateTimeOffset.Parse(r.GetString(11))
            });
        }
        return list;
    }

    private void ExecFlag(string workno, string col, bool value)
    {
        using var cmd = Conn.CreateCommand();
        cmd.CommandText = $"UPDATE works SET {col}=$v WHERE workno=$w";
        cmd.Parameters.AddWithValue("$v", value ? 1 : 0);
        cmd.Parameters.AddWithValue("$w", workno);
        cmd.ExecuteNonQuery();
    }

    private static void BindWork(SqliteCommand cmd, WorkItem w)
    {
        cmd.Parameters.AddWithValue("$workno", w.Workno);
        cmd.Parameters.AddWithValue("$work_name", w.WorkName);
        cmd.Parameters.AddWithValue("$maker_name", w.MakerName);
        cmd.Parameters.AddWithValue("$work_type", w.WorkType);
        cmd.Parameters.AddWithValue("$site_id", w.SiteId);
        cmd.Parameters.AddWithValue("$size", w.ContentsFileSize);
        cmd.Parameters.AddWithValue("$point", w.Point);
        cmd.Parameters.AddWithValue("$regist", (object?)w.RegistDate ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$sales", (object?)w.SalesDate ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$update", (object?)w.UpdateDate ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$thumb", (object?)w.ThumbUrl ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$main", (object?)w.MainImageUrl ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$tags", w.TagsJson);
        cmd.Parameters.AddWithValue("$fav", w.IsFavorite ? 1 : 0);
        cmd.Parameters.AddWithValue("$hidden", w.IsHidden ? 1 : 0);
        cmd.Parameters.AddWithValue("$dl", w.IsDownloaded ? 1 : 0);
        cmd.Parameters.AddWithValue("$install", (object?)w.InstallPath ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$exe", (object?)w.SelectedExe ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$jp", w.UseJapanLocale ? 1 : 0);
        cmd.Parameters.AddWithValue("$local_update", (object?)w.LocalUpdateDate ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$parent", (object?)w.ParentWorkno ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$dlc", w.IsDlc ? 1 : 0);
    }

    private static WorkItem ReadWork(SqliteDataReader r) => new()
    {
        Workno = r.GetString(0),
        WorkName = r.GetString(1),
        MakerName = r.GetString(2),
        WorkType = r.GetString(3),
        SiteId = r.GetString(4),
        ContentsFileSize = r.GetInt64(5),
        Point = r.GetInt32(6),
        RegistDate = r.IsDBNull(7) ? null : r.GetString(7),
        SalesDate = r.IsDBNull(8) ? null : r.GetString(8),
        UpdateDate = r.IsDBNull(9) ? null : r.GetString(9),
        ThumbUrl = r.IsDBNull(10) ? null : r.GetString(10),
        MainImageUrl = r.IsDBNull(11) ? null : r.GetString(11),
        TagsJson = r.GetString(12),
        IsFavorite = r.GetInt32(13) == 1,
        IsHidden = r.GetInt32(14) == 1,
        IsDownloaded = r.GetInt32(15) == 1,
        InstallPath = r.IsDBNull(16) ? null : r.GetString(16),
        SelectedExe = r.IsDBNull(17) ? null : r.GetString(17),
        UseJapanLocale = r.GetInt32(18) == 1,
        LocalUpdateDate = r.IsDBNull(19) ? null : r.GetString(19),
        ParentWorkno = r.IsDBNull(20) ? null : r.GetString(20),
        IsDlc = r.GetInt32(21) == 1
    };

    public void Dispose() => _conn?.Dispose();
}
