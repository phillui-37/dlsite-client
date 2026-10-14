using System.Text.Json;
using DlsiteClient.Core.Models;
using DlsiteClient.Core.Storage;
using DlsiteClient.Platform;

namespace DlsiteClient.Core.Services;

public sealed class SettingsService
{
    private readonly LibraryDatabase _db;
    private readonly IPathConfig _paths;
    private AppSettings _current;

    public SettingsService(LibraryDatabase db, IPathConfig paths)
    {
        _db = db;
        _paths = paths;
        _current = Load();
    }

    public AppSettings Current => _current;
    public event Action? Changed;

    public void Update(Action<AppSettings> mutate)
    {
        mutate(_current);
        Save();
        Changed?.Invoke();
    }

    private AppSettings Load()
    {
        var json = _db.GetSetting("app");
        AppSettings s;
        if (string.IsNullOrEmpty(json))
        {
            s = new AppSettings();
        }
        else
        {
            s = JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
        }
        if (string.IsNullOrWhiteSpace(s.LibraryDirectory))
            s.LibraryDirectory = _paths.DefaultLibraryDirectory;
        if (string.IsNullOrWhiteSpace(s.CacheDirectory))
            s.CacheDirectory = _paths.DefaultCacheDirectory;
        if (string.IsNullOrWhiteSpace(s.LogDirectory))
            s.LogDirectory = _paths.DefaultLogDirectory;
        Directory.CreateDirectory(s.LibraryDirectory);
        Directory.CreateDirectory(s.CacheDirectory);
        Directory.CreateDirectory(s.LogDirectory);
        return s;
    }

    private void Save()
    {
        _db.SetSetting("app", JsonSerializer.Serialize(_current));
    }
}
