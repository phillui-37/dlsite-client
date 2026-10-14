namespace DlsiteClient.Platform;

public interface ICredentialStore
{
    Task SaveAsync(string key, string value, CancellationToken ct = default);
    Task<string?> GetAsync(string key, CancellationToken ct = default);
    Task DeleteAsync(string key, CancellationToken ct = default);
}

public interface IPathConfig
{
    string AppDataRoot { get; }
    string DefaultLibraryDirectory { get; }
    string DefaultCacheDirectory { get; }
    string DefaultLogDirectory { get; }
}

public sealed record NestPathImport(string LibraryDirectory, string CacheDirectory);

public interface INestSettingsImporter
{
    bool TryReadPaths(out NestPathImport? paths);
}

public interface IProtocolHandler
{
    void Register(string scheme, string exePath);
    bool TryParsePlayWorkno(string[] args, out string? workno);
}

public interface IProcessLocale
{
    void StartWithLocale(string exePath, string workingDirectory, string? localeName, bool useJapanLocale);
}

public interface IStartupRegistration
{
    bool IsEnabled { get; }
    void SetEnabled(bool enabled);
}

public interface IShell
{
    void OpenFolder(string path);
    void OpenUrl(string url);
}
