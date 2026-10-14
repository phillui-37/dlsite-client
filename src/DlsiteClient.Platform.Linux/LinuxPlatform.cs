using System.Diagnostics;
using System.Runtime.Versioning;
using System.Text;
using DlsiteClient.Platform;

namespace DlsiteClient.Platform.Linux;

[SupportedOSPlatform("linux")]
public sealed class LinuxPathConfig : IPathConfig
{
    public string AppDataRoot { get; } =
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "dlsite-client");

    public string DefaultLibraryDirectory => Path.Combine(AppDataRoot, "Library");
    public string DefaultCacheDirectory => Path.Combine(AppDataRoot, "Cache");
    public string DefaultLogDirectory => Path.Combine(AppDataRoot, "logs");
}

/// <summary>
/// FreeDesktop Secret Service via <c>secret-tool</c> when present; otherwise a 0600 file under XDG data.
/// </summary>
[SupportedOSPlatform("linux")]
public sealed class LinuxCredentialStore : ICredentialStore
{
    private const string AttrApp = "dlsite-client";
    private readonly string _fallbackDir;
    private readonly bool _useSecretTool;

    public LinuxCredentialStore(IPathConfig paths)
    {
        _fallbackDir = Path.Combine(paths.AppDataRoot, "secrets");
        Directory.CreateDirectory(_fallbackDir);
        _useSecretTool = CanRun("secret-tool", "--version");
    }

    public Task SaveAsync(string key, string value, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (_useSecretTool)
        {
            RunSecretTool(
                ["store", "--label", $"DlsiteClient/{key}", "app", AttrApp, "key", key],
                stdin: value);
            return Task.CompletedTask;
        }

        var path = FallbackPath(key);
        File.WriteAllText(path, value, Encoding.UTF8);
        TryChmod600(path);
        return Task.CompletedTask;
    }

    public Task<string?> GetAsync(string key, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (_useSecretTool)
        {
            var (ok, stdout) = RunSecretTool(["lookup", "app", AttrApp, "key", key], stdin: null);
            return Task.FromResult(ok && !string.IsNullOrWhiteSpace(stdout) ? stdout.TrimEnd('\n', '\r') : null);
        }

        var path = FallbackPath(key);
        if (!File.Exists(path)) return Task.FromResult<string?>(null);
        return Task.FromResult<string?>(File.ReadAllText(path, Encoding.UTF8));
    }

    public Task DeleteAsync(string key, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (_useSecretTool)
        {
            RunSecretTool(["clear", "app", AttrApp, "key", key], stdin: null);
            return Task.CompletedTask;
        }

        var path = FallbackPath(key);
        if (File.Exists(path)) File.Delete(path);
        return Task.CompletedTask;
    }

    private string FallbackPath(string key)
    {
        var safe = string.Concat(key.Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' ? c : '_'));
        return Path.Combine(_fallbackDir, safe + ".secret");
    }

    private static void TryChmod600(string path)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "chmod",
                Arguments = $"600 \"{path}\"",
                UseShellExecute = false,
                CreateNoWindow = true
            })?.WaitForExit(2000);
        }
        catch
        {
            /* best effort */
        }
    }

    private static bool CanRun(string file, string args)
    {
        try
        {
            using var p = Process.Start(new ProcessStartInfo
            {
                FileName = file,
                Arguments = args,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            });
            if (p is null) return false;
            p.WaitForExit(2000);
            return p.ExitCode == 0 || p.ExitCode == 1; // secret-tool --version may vary
        }
        catch
        {
            return false;
        }
    }

    private static (bool ok, string stdout) RunSecretTool(IReadOnlyList<string> args, string? stdin)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "secret-tool",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = stdin is not null,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            foreach (var a in args) psi.ArgumentList.Add(a);
            using var p = Process.Start(psi);
            if (p is null) return (false, "");
            if (stdin is not null)
            {
                p.StandardInput.Write(stdin);
                p.StandardInput.Close();
            }
            var stdout = p.StandardOutput.ReadToEnd();
            p.WaitForExit(5000);
            return (p.ExitCode == 0, stdout);
        }
        catch
        {
            return (false, "");
        }
    }
}

[SupportedOSPlatform("linux")]
public sealed class LinuxNestSettingsImporter : INestSettingsImporter
{
    public bool TryReadPaths(out NestPathImport? paths)
    {
        paths = null;
        return false; // Nest settings.xml is Windows-only
    }
}

[SupportedOSPlatform("linux")]
public sealed class LinuxProtocolHandler : IProtocolHandler
{
    public void Register(string scheme, string exePath)
    {
        if (string.IsNullOrWhiteSpace(exePath) || !File.Exists(exePath)) return;
        var apps = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "applications");
        Directory.CreateDirectory(apps);
        var desktop = Path.Combine(apps, "dlsite-client.desktop");
        var content =
            $"""
            [Desktop Entry]
            Type=Application
            Name=DLsite Client
            Exec="{exePath}" %u
            StartupNotify=false
            MimeType=x-scheme-handler/{scheme};
            NoDisplay=true
            """;
        File.WriteAllText(desktop, content);
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "xdg-mime",
                Arguments = $"default dlsite-client.desktop x-scheme-handler/{scheme}",
                UseShellExecute = false,
                CreateNoWindow = true
            })?.WaitForExit(3000);
            Process.Start(new ProcessStartInfo
            {
                FileName = "update-desktop-database",
                Arguments = $"\"{apps}\"",
                UseShellExecute = false,
                CreateNoWindow = true
            })?.WaitForExit(3000);
        }
        catch
        {
            /* optional helpers */
        }
    }

    public bool TryParsePlayWorkno(string[] args, out string? workno)
    {
        workno = null;
        foreach (var a in args)
        {
            if (!a.StartsWith($"{AppProtocol.Scheme}://", StringComparison.OrdinalIgnoreCase))
                continue;
            if (!Uri.TryCreate(a, UriKind.Absolute, out var uri)) continue;
            if (!string.Equals(uri.Host, "play", StringComparison.OrdinalIgnoreCase))
                continue;
            workno = uri.AbsolutePath.Trim('/');
            return !string.IsNullOrWhiteSpace(workno);
        }
        return false;
    }
}

/// <summary>Native process, or Proton/Wine for Windows EXEs.</summary>
[SupportedOSPlatform("linux")]
public sealed class LinuxProcessLocale : IProcessLocale
{
    private readonly Func<string?> _protonPath;

    public LinuxProcessLocale() : this(() => null) { }

    public LinuxProcessLocale(Func<string?> protonPathOverride) => _protonPath = protonPathOverride;

    public void StartWithLocale(string exePath, string workingDirectory, string? localeName, bool useJapanLocale)
    {
        var isWinExe = exePath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase);
        if (isWinExe)
        {
            StartWindowsExe(exePath, workingDirectory, useJapanLocale);
            return;
        }

        var psi = new ProcessStartInfo
        {
            FileName = exePath,
            WorkingDirectory = workingDirectory,
            UseShellExecute = false
        };
        ApplyLocale(psi, localeName, useJapanLocale);
        Process.Start(psi);
    }

    private void StartWindowsExe(string exePath, string workingDirectory, bool useJapanLocale)
    {
        var proton = ResolveProton();
        if (proton is not null)
        {
            var psi = new ProcessStartInfo
            {
                FileName = proton,
                WorkingDirectory = workingDirectory,
                UseShellExecute = false
            };
            psi.ArgumentList.Add("run");
            psi.ArgumentList.Add(exePath);
            ApplyLocale(psi, null, useJapanLocale);
            Process.Start(psi);
            return;
        }

        var wine = FindOnPath("wine64") ?? FindOnPath("wine");
        if (wine is not null)
        {
            var psi = new ProcessStartInfo
            {
                FileName = wine,
                WorkingDirectory = workingDirectory,
                UseShellExecute = false
            };
            psi.ArgumentList.Add(exePath);
            ApplyLocale(psi, null, useJapanLocale);
            if (useJapanLocale)
                psi.Environment["LANG"] = "ja_JP.UTF-8";
            Process.Start(psi);
            return;
        }

        throw new InvalidOperationException(
            "No Proton or Wine found. Install Steam Proton or Wine, or set Proton path in Settings.");
    }

    private string? ResolveProton()
    {
        var overridePath = _protonPath();
        if (!string.IsNullOrWhiteSpace(overridePath) && File.Exists(overridePath))
            return overridePath;

        foreach (var c in ProtonCandidates())
            if (File.Exists(c)) return c;
        return null;
    }

    private static IEnumerable<string> ProtonCandidates()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var steamRoots = new[]
        {
            Path.Combine(home, ".steam", "steam"),
            Path.Combine(home, ".local", "share", "Steam"),
            Path.Combine(home, ".var", "app", "com.valvesoftware.Steam", "data", "Steam"),
            Environment.GetEnvironmentVariable("STEAM_COMPAT_CLIENT_INSTALL_PATH") ?? ""
        };
        foreach (var root in steamRoots.Where(r => !string.IsNullOrWhiteSpace(r) && Directory.Exists(r)))
        {
            var common = Path.Combine(root, "steamapps", "common");
            if (!Directory.Exists(common)) continue;
            foreach (var dir in Directory.EnumerateDirectories(common, "Proton*"))
            {
                var proton = Path.Combine(dir, "proton");
                if (File.Exists(proton)) yield return proton;
            }
        }
    }

    private static string? FindOnPath(string name)
    {
        var path = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var dir in path.Split(':', StringSplitOptions.RemoveEmptyEntries))
        {
            var full = Path.Combine(dir, name);
            if (File.Exists(full)) return full;
        }
        return null;
    }

    private static void ApplyLocale(ProcessStartInfo psi, string? localeName, bool useJapanLocale)
    {
        if (useJapanLocale)
        {
            psi.Environment["LANG"] = "ja_JP.UTF-8";
            psi.Environment["LC_ALL"] = "ja_JP.UTF-8";
            psi.Environment["LANGUAGE"] = "ja_JP:ja";
        }
        else if (!string.IsNullOrWhiteSpace(localeName))
        {
            psi.Environment["LANG"] = localeName;
        }
    }
}

[SupportedOSPlatform("linux")]
public sealed class LinuxStartupRegistration : IStartupRegistration
{
    private static string DesktopPath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "autostart",
            "dlsite-client.desktop");

    public bool IsEnabled => File.Exists(DesktopPath);

    public void SetEnabled(bool enabled)
    {
        var path = DesktopPath;
        if (!enabled)
        {
            if (File.Exists(path)) File.Delete(path);
            return;
        }

        var exe = Environment.ProcessPath ?? throw new InvalidOperationException("ProcessPath null");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path,
            $"""
            [Desktop Entry]
            Type=Application
            Name=DLsite Client
            Exec="{exe}"
            X-GNOME-Autostart-enabled=true
            """);
    }
}

[SupportedOSPlatform("linux")]
public sealed class LinuxShell : IShell
{
    public void OpenFolder(string path)
    {
        Directory.CreateDirectory(path);
        XdgOpen(path);
    }

    public void OpenUrl(string url) => XdgOpen(url);

    public bool Confirm(string message, string title)
    {
        if (TryZenity(message, title, out var z)) return z;
        if (TryKDialog(message, title, out var k)) return k;
        // Headless / no dialog helper: refuse destructive confirm by default.
        Console.Error.WriteLine($"[{title}] {message} (assumed No — install zenity/kdialog for prompts)");
        return false;
    }

    private static void XdgOpen(string target)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "xdg-open",
                Arguments = $"\"{target}\"",
                UseShellExecute = false,
                CreateNoWindow = true
            });
        }
        catch
        {
            /* ignore */
        }
    }

    private static bool TryZenity(string message, string title, out bool yes)
    {
        yes = false;
        try
        {
            using var p = Process.Start(new ProcessStartInfo
            {
                FileName = "zenity",
                ArgumentList = { "--question", "--title", title, "--text", message },
                UseShellExecute = false,
                CreateNoWindow = true
            });
            if (p is null) return false;
            p.WaitForExit(120_000);
            yes = p.ExitCode == 0;
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryKDialog(string message, string title, out bool yes)
    {
        yes = false;
        try
        {
            using var p = Process.Start(new ProcessStartInfo
            {
                FileName = "kdialog",
                ArgumentList = { "--title", title, "--yesno", message },
                UseShellExecute = false,
                CreateNoWindow = true
            });
            if (p is null) return false;
            p.WaitForExit(120_000);
            yes = p.ExitCode == 0;
            return true;
        }
        catch
        {
            return false;
        }
    }
}
