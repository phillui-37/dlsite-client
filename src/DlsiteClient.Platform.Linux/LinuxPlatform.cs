using DlsiteClient.Platform;

namespace DlsiteClient.Platform.Linux;

/// <summary>M5+ stub — keyring / xdg / Proton not implemented yet.</summary>
public sealed class LinuxPathConfig : IPathConfig
{
    public string AppDataRoot { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "dlsite-client");

    public string DefaultLibraryDirectory => Path.Combine(AppDataRoot, "Library");
    public string DefaultCacheDirectory => Path.Combine(AppDataRoot, "Cache");
    public string DefaultLogDirectory => Path.Combine(AppDataRoot, "logs");
}

public sealed class LinuxCredentialStore : ICredentialStore
{
    private readonly string _file;

    public LinuxCredentialStore(IPathConfig paths)
    {
        Directory.CreateDirectory(paths.AppDataRoot);
        _file = Path.Combine(paths.AppDataRoot, ".secrets-stub"); // replaced by secret-service in M5
    }

    public Task SaveAsync(string key, string value, CancellationToken ct = default)
    {
        // Stub only — not for production secrets on Linux until M5.
        File.AppendAllText(_file, $"{key}=***\n");
        return Task.CompletedTask;
    }

    public Task<string?> GetAsync(string key, CancellationToken ct = default) =>
        Task.FromResult<string?>(null);

    public Task DeleteAsync(string key, CancellationToken ct = default) => Task.CompletedTask;
}

public sealed class LinuxNestSettingsImporter : INestSettingsImporter
{
    public bool TryReadPaths(out NestPathImport? paths)
    {
        paths = null;
        return false; // Nest is Windows-only
    }
}

public sealed class LinuxProtocolHandler : IProtocolHandler
{
    public void Register(string scheme, string exePath) { /* M5: xdg desktop file */ }

    public bool TryParsePlayWorkno(string[] args, out string? workno)
    {
        workno = null;
        return false;
    }
}

public sealed class LinuxProcessLocale : IProcessLocale
{
    public void StartWithLocale(string exePath, string workingDirectory, string? localeName, bool useJapanLocale)
    {
        throw new NotSupportedException("Linux/Proton launcher is M5+");
    }
}

public sealed class LinuxStartupRegistration : IStartupRegistration
{
    public bool IsEnabled => false;
    public void SetEnabled(bool enabled) { }
}

public sealed class LinuxShell : IShell
{
    public void OpenFolder(string path) => Directory.CreateDirectory(path);
    public void OpenUrl(string url) { }
}
