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

/// <summary>FreeDesktop Secret Service. Never stores session values in app files.</summary>
[SupportedOSPlatform("linux")]
public sealed class LinuxCredentialStore : ICredentialStore
{
    public const string UnavailableErrorCode = "secret-service-unavailable";
    private readonly string _tool;
    private readonly bool _bundled;

    public LinuxCredentialStore(IPathConfig paths)
    {
        var bundled = Environment.GetEnvironmentVariable("DLSITE_SECRET_TOOL");
        _bundled = !string.IsNullOrWhiteSpace(bundled) && File.Exists(bundled);
        _tool = _bundled ? bundled! : "secret-tool";
    }

    public async Task SaveAsync(string key, string value, CancellationToken ct = default)
    {
        var result = await RunAsync(["store", "--label", $"DlsiteClient/{key}", "app", "dlsite-client", "key", key], value, ct);
        if (!result.Ok) throw new InvalidOperationException(UnavailableErrorCode);
    }

    public async Task<string?> GetAsync(string key, CancellationToken ct = default)
    {
        var result = await RunAsync(["lookup", "app", "dlsite-client", "key", key], null, ct);
        return result.Ok && result.Output.Length > 0 ? result.Output.TrimEnd('\n', '\r') : null;
    }

    public async Task DeleteAsync(string key, CancellationToken ct = default)
    {
        var result = await RunAsync(["clear", "app", "dlsite-client", "key", key], null, ct);
        // Exit 1 without an error means that no matching item exists.
        if (!result.Ok && result.Error.Length > 0) throw new InvalidOperationException(UnavailableErrorCode);
    }

    private async Task<(bool Ok, string Output, string Error)> RunAsync(string[] args, string? value, CancellationToken ct)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromSeconds(60));
        var psi = new ProcessStartInfo(_tool)
        {
            UseShellExecute = false, RedirectStandardOutput = true,
            RedirectStandardError = true, RedirectStandardInput = value is not null,
            CreateNoWindow = true
        };
        foreach (var arg in args) psi.ArgumentList.Add(arg);
        if (!_bundled) LinuxHostEnvironment.Restore(psi);
        Process? process;
        try { process = Process.Start(psi); }
        catch (System.ComponentModel.Win32Exception) { return (false, "", UnavailableErrorCode); }
        if (process is null) return (false, "", UnavailableErrorCode);
        using (process)
        {
            var output = process.StandardOutput.ReadToEndAsync(deadline.Token);
            var error = process.StandardError.ReadToEndAsync(deadline.Token);
            try
            {
                if (value is not null)
                {
                    await process.StandardInput.WriteAsync(value.AsMemory(), deadline.Token);
                    process.StandardInput.Close();
                }
                await process.WaitForExitAsync(deadline.Token);
                return (process.ExitCode == 0, await output, await error);
            }
            catch (IOException)
            {
                // A failed helper can close stdin before the session value is written.
                try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
                return (false, "", UnavailableErrorCode);
            }
            catch (OperationCanceledException)
            {
                try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
                if (ct.IsCancellationRequested) throw;
                if (value is null) return (false, "", UnavailableErrorCode);
                throw new InvalidOperationException(UnavailableErrorCode);
            }
        }
    }
}

[SupportedOSPlatform("linux")]
public sealed class LinuxProtocolHandler : IProtocolHandler
{
    public void Register(string scheme, string exePath)
    {
        exePath = LinuxDesktopEntry.ResolveExecutable(exePath);
        if (string.IsNullOrWhiteSpace(exePath) || !File.Exists(exePath)) return;
        var apps = Path.Combine(LinuxDesktopEntry.DataHome, "applications");
        Directory.CreateDirectory(apps);
        var desktop = Path.Combine(apps, "dlsite-client.desktop");
        var content =
            $"""
            [Desktop Entry]
            Type=Application
            Name=DLsite Client
            Exec={LinuxDesktopEntry.QuoteExecutable(exePath)} %u
            StartupNotify=false
            MimeType=x-scheme-handler/{scheme};
            NoDisplay=true
            """;
        File.WriteAllText(desktop, content);
        Directory.CreateDirectory(LinuxDesktopEntry.ConfigHome);
        try
        {
            LinuxHostEnvironment.Start(new ProcessStartInfo
            {
                FileName = "xdg-mime",
                Arguments = $"default dlsite-client.desktop x-scheme-handler/{scheme}",
                UseShellExecute = false,
                CreateNoWindow = true
            })?.WaitForExit(3000);
            LinuxHostEnvironment.Start(new ProcessStartInfo
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
        LinuxHostEnvironment.Restore(psi);
        ApplyLocale(psi, localeName, useJapanLocale);
        LinuxHostEnvironment.Start(psi);
    }

    private void StartWindowsExe(string exePath, string workingDirectory, bool useJapanLocale)
    {
        var proton = ResolveProton();
        var installKey = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            Encoding.UTF8.GetBytes(Path.GetFullPath(workingDirectory))));
        var prefix = Path.Combine(LinuxDesktopEntry.DataHome, "dlsite-client", "compatdata", installKey);
        Directory.CreateDirectory(prefix);
        var umu = FindOnPath("umu-run");
        if (umu is not null)
        {
            Directory.CreateDirectory(Path.Combine(prefix, "pfx"));
            var psi = new ProcessStartInfo(umu) { WorkingDirectory = workingDirectory, UseShellExecute = false };
            LinuxHostEnvironment.Restore(psi);
            // umu maps WINEPREFIX to STEAM_COMPAT_DATA_PATH; Proton adds /pfx.
            psi.Environment["WINEPREFIX"] = prefix;
            psi.Environment["GAMEID"] = "umu-default";
            if (proton is not null) psi.Environment["PROTONPATH"] = Path.GetDirectoryName(proton)!;
            psi.ArgumentList.Add(exePath);
            ApplyLocale(psi, null, useJapanLocale);
            LinuxHostEnvironment.Start(psi);
            return;
        }
        if (proton is not null)
        {
            var runtime = ResolveSteamRuntime(proton);
            var psi = new ProcessStartInfo
            {
                FileName = runtime ?? proton,
                WorkingDirectory = workingDirectory,
                UseShellExecute = false
            };
            // Keep game prefixes outside the install tree so updates do not erase saves.
            LinuxHostEnvironment.Restore(psi);
            psi.Environment["STEAM_COMPAT_DATA_PATH"] = prefix;
            psi.Environment["STEAM_COMPAT_INSTALL_PATH"] = workingDirectory;
            if (runtime is not null)
                psi.Environment["STEAM_COMPAT_MOUNTS"] = string.Join(':', new[]
                {
                    psi.Environment.TryGetValue("STEAM_COMPAT_MOUNTS", out var mounts) ? mounts ?? "" : "",
                    workingDirectory, prefix, Path.GetDirectoryName(proton)!, Path.GetDirectoryName(runtime)!
                }.Where(p => p.Length > 0));
            var steam = SteamRoots().FirstOrDefault(Directory.Exists);
            if (steam is not null)
                psi.Environment["STEAM_COMPAT_CLIENT_INSTALL_PATH"] = steam;
            if (runtime is not null)
            {
                psi.ArgumentList.Add("--verb=waitforexitandrun");
                psi.ArgumentList.Add("--");
                psi.ArgumentList.Add(proton);
            }
            psi.ArgumentList.Add(runtime is null ? "run" : "waitforexitandrun");
            psi.ArgumentList.Add(exePath);
            ApplyLocale(psi, null, useJapanLocale);
            LinuxHostEnvironment.Start(psi);
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
            LinuxHostEnvironment.Restore(psi);
            psi.Environment["WINEPREFIX"] = Path.Combine(prefix, "wine");
            psi.ArgumentList.Add(exePath);
            ApplyLocale(psi, null, useJapanLocale);
            if (useJapanLocale)
                psi.Environment["LANG"] = "ja_JP.UTF-8";
            LinuxHostEnvironment.Start(psi);
            return;
        }

        throw new InvalidOperationException(
            "No Proton or Wine found. Install Steam Proton or Wine, or set Proton path in Settings.");
    }

    private string? ResolveProton()
    {
        var overridePath = _protonPath();
        if (!string.IsNullOrWhiteSpace(overridePath))
        {
            if (Directory.Exists(overridePath)) overridePath = Path.Combine(overridePath, "proton");
            if (File.Exists(overridePath)) return overridePath;
            throw new FileNotFoundException("Configured Proton launcher was not found.", overridePath);
        }

        foreach (var c in ProtonCandidates())
            if (File.Exists(c)) return c;
        return null;
    }

    private static string? ReadVdfField(string file, string key)
    {
        if (!File.Exists(file)) return null;
        foreach (var line in File.ReadLines(file))
        {
            var fields = line.Split('"');
            if (fields.Length >= 4 && fields[1] == key) return fields[3].Replace("\\\\", "\\");
        }
        return null;
    }

    private static string? ResolveSteamRuntime(string proton)
    {
        var required = ReadVdfField(Path.Combine(Path.GetDirectoryName(proton)!, "toolmanifest.vdf"), "require_tool_appid");
        if (string.IsNullOrEmpty(required)) return null; // Older stand-alone Proton builds.
        if (required.All(char.IsAsciiDigit))
            foreach (var library in SteamLibraries())
            {
                var directory = ReadVdfField(Path.Combine(library, "steamapps", "appmanifest_" + required + ".acf"), "installdir");
                if (string.IsNullOrWhiteSpace(directory)) continue;
                var entry = Path.Combine(library, "steamapps", "common", directory, "_v2-entry-point");
                if (File.Exists(entry)) return entry;
            }
        throw new InvalidOperationException("steam-runtime-missing");
    }

    private static IEnumerable<string> ProtonCandidates()
    {
        foreach (var root in SteamLibraries())
        {
            var custom = Path.Combine(root, "compatibilitytools.d");
            if (Directory.Exists(custom))
                foreach (var dir in Directory.EnumerateDirectories(custom).OrderDescending())
                    if (File.Exists(Path.Combine(dir, "proton"))) yield return Path.Combine(dir, "proton");
            var common = Path.Combine(root, "steamapps", "common");
            if (!Directory.Exists(common)) continue;
            foreach (var dir in Directory.EnumerateDirectories(common, "Proton*").OrderDescending())
            {
                var proton = Path.Combine(dir, "proton");
                if (File.Exists(proton)) yield return proton;
            }
        }
    }

    private static IEnumerable<string> SteamLibraries()
    {
        var paths = new HashSet<string>(StringComparer.Ordinal);
        foreach (var root in SteamRoots().Where(Directory.Exists))
        {
            paths.Add(root);
            var vdf = Path.Combine(root, "steamapps", "libraryfolders.vdf");
            if (!File.Exists(vdf)) continue;
            // Only read quoted path fields; no general VDF or command interpretation.
            foreach (var line in File.ReadLines(vdf))
            {
                var parts = line.Split('"');
                if (parts.Length >= 4 && parts[1] == "path")
                {
                    var path = parts[3].Replace("\\\\", "\\");
                    if (Path.IsPathRooted(path) && Directory.Exists(path)) paths.Add(path);
                }
            }
        }
        return paths;
    }

    private static IEnumerable<string> SteamRoots()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return new[]
        {
            Environment.GetEnvironmentVariable("STEAM_COMPAT_CLIENT_INSTALL_PATH") ?? "",
            Path.Combine(home, ".steam", "steam"),
            Path.Combine(home, ".local", "share", "Steam"),
            Path.Combine(home, ".var", "app", "com.valvesoftware.Steam", "data", "Steam")
        }.Where(r => !string.IsNullOrWhiteSpace(r));
    }

    private static string? FindOnPath(string name)
    {
        var path = Environment.GetEnvironmentVariable(
            Environment.GetEnvironmentVariable("DLSITE_BUNDLED_RUNTIME") == "1" ? "DLSITE_HOST_PATH" : "PATH") ?? "";
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
            LinuxDesktopEntry.ConfigHome,
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

        var exe = LinuxDesktopEntry.ResolveExecutable(Environment.ProcessPath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path,
            $"""
            [Desktop Entry]
            Type=Application
            Name=DLsite Client
            Exec={LinuxDesktopEntry.QuoteExecutable(exe)}
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
            LinuxHostEnvironment.Start(new ProcessStartInfo
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
            using var p = LinuxHostEnvironment.Start(new ProcessStartInfo
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
            using var p = LinuxHostEnvironment.Start(new ProcessStartInfo
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
