using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using System.Xml.Linq;
using DlsiteClient.Platform;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;

namespace DlsiteClient.Platform.Windows;

[SupportedOSPlatform("windows")]
public sealed class WindowsPathConfig : IPathConfig
{
    public string AppDataRoot { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DlsiteClient");

    public string DefaultLibraryDirectory => Path.Combine(AppDataRoot, "Library");
    public string DefaultCacheDirectory => Path.Combine(AppDataRoot, "Cache");
    public string DefaultLogDirectory => Path.Combine(AppDataRoot, "logs");
}

/// <summary>Stores secrets in Windows Credential Manager (Generic credentials).</summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsCredentialStore : ICredentialStore
{
    private const string TargetPrefix = "DlsiteClient/";

    public Task SaveAsync(string key, string value, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var target = TargetPrefix + key;
        var bytes = Encoding.UTF8.GetBytes(value);
        var cred = new Native.CREDENTIAL
        {
            Type = Native.CRED_TYPE_GENERIC,
            TargetName = target,
            CredentialBlobSize = bytes.Length,
            CredentialBlob = Marshal.AllocHGlobal(bytes.Length),
            Persist = Native.CRED_PERSIST_LOCAL_MACHINE,
            UserName = Environment.UserName,
            AttributeCount = 0,
            Attributes = IntPtr.Zero,
            Comment = null,
            TargetAlias = null
        };
        try
        {
            Marshal.Copy(bytes, 0, cred.CredentialBlob, bytes.Length);
            if (!Native.CredWrite(ref cred, 0))
                throw new InvalidOperationException($"CredWrite failed: {Marshal.GetLastWin32Error()}");
        }
        finally
        {
            Marshal.FreeHGlobal(cred.CredentialBlob);
        }
        return Task.CompletedTask;
    }

    public Task<string?> GetAsync(string key, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var target = TargetPrefix + key;
        if (!Native.CredRead(target, Native.CRED_TYPE_GENERIC, 0, out var ptr) || ptr == IntPtr.Zero)
            return Task.FromResult<string?>(null);
        try
        {
            var cred = Marshal.PtrToStructure<Native.CREDENTIAL>(ptr);
            if (cred.CredentialBlob == IntPtr.Zero || cred.CredentialBlobSize <= 0)
                return Task.FromResult<string?>(null);
            var bytes = new byte[cred.CredentialBlobSize];
            Marshal.Copy(cred.CredentialBlob, bytes, 0, bytes.Length);
            return Task.FromResult<string?>(Encoding.UTF8.GetString(bytes));
        }
        finally
        {
            Native.CredFree(ptr);
        }
    }

    public Task DeleteAsync(string key, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        Native.CredDelete(TargetPrefix + key, Native.CRED_TYPE_GENERIC, 0);
        return Task.CompletedTask;
    }

    private static class Native
    {
        public const int CRED_TYPE_GENERIC = 1;
        public const int CRED_PERSIST_LOCAL_MACHINE = 2;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct CREDENTIAL
        {
            public int Flags;
            public int Type;
            public string TargetName;
            public string? Comment;
            public long LastWritten;
            public int CredentialBlobSize;
            public IntPtr CredentialBlob;
            public int Persist;
            public int AttributeCount;
            public IntPtr Attributes;
            public string? TargetAlias;
            public string? UserName;
        }

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern bool CredWrite([In] ref CREDENTIAL userCredential, [In] uint flags);

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern bool CredRead(string target, int type, int reservedFlag, out IntPtr credentialPtr);

        [DllImport("advapi32.dll", SetLastError = true)]
        public static extern bool CredFree(IntPtr cred);

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern bool CredDelete(string target, int type, int flags);
    }
}

/// <summary>Reads Nest library/cache paths only — never credentials.</summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsNestSettingsImporter(ILogger<WindowsNestSettingsImporter>? logger = null) : INestSettingsImporter
{
    public bool TryReadPaths(out NestPathImport? paths)
    {
        paths = null;
        var settings = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "DLsiteNest", "settings.xml");
        if (!File.Exists(settings))
            return false;
        try
        {
            var doc = XDocument.Load(settings);
            string? lib = null, cache = null;
            foreach (var kv in doc.Descendants("KeyValueOfStringString"))
            {
                var key = kv.Element("Key")?.Value;
                var value = kv.Element("Value")?.Value;
                if (key == "libraryDirectory") lib = value;
                if (key == "cacheDirectory") cache = value;
            }
            if (string.IsNullOrWhiteSpace(lib) || string.IsNullOrWhiteSpace(cache))
                return false;
            paths = new NestPathImport(lib!, cache!);
            return true;
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "Failed to read Nest settings.xml paths");
            return false;
        }
    }
}

[SupportedOSPlatform("windows")]
public sealed class WindowsProtocolHandler : IProtocolHandler
{
    public const string Scheme = "dlsite-client";

    public void Register(string scheme, string exePath)
    {
        using var key = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{scheme}");
        key.SetValue("", $"URL:{scheme} Protocol");
        key.SetValue("URL Protocol", "");
        using var command = key.CreateSubKey(@"shell\open\command");
        command.SetValue("", $"\"{exePath}\" \"%1\"");
    }

    public bool TryParsePlayWorkno(string[] args, out string? workno)
    {
        workno = null;
        foreach (var a in args)
        {
            if (!a.StartsWith($"{Scheme}://", StringComparison.OrdinalIgnoreCase))
                continue;
            var uri = new Uri(a);
            if (!string.Equals(uri.Host, "play", StringComparison.OrdinalIgnoreCase))
                continue;
            workno = uri.AbsolutePath.Trim('/');
            return !string.IsNullOrWhiteSpace(workno);
        }
        return false;
    }
}

[SupportedOSPlatform("windows")]
public sealed class WindowsProcessLocale : IProcessLocale
{
    public void StartWithLocale(string exePath, string workingDirectory, string? localeName, bool useJapanLocale)
    {
        var psi = new ProcessStartInfo
        {
            FileName = exePath,
            WorkingDirectory = workingDirectory,
            UseShellExecute = false
        };
        if (useJapanLocale)
        {
            // Soft locale hint for child; full AppLocale emulation is OS-dependent.
            psi.Environment["LANG"] = "ja_JP.UTF-8";
            psi.Environment["LC_ALL"] = "ja_JP.UTF-8";
        }
        else if (!string.IsNullOrWhiteSpace(localeName))
        {
            psi.Environment["LANG"] = localeName;
        }
        Process.Start(psi);
    }
}

[SupportedOSPlatform("windows")]
public sealed class WindowsStartupRegistration : IStartupRegistration
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "DlsiteClient";

    public bool IsEnabled
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, false);
            return key?.GetValue(ValueName) is string;
        }
    }

    public void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, true)
            ?? Registry.CurrentUser.CreateSubKey(RunKey);
        if (enabled)
        {
            var exe = Environment.ProcessPath ?? throw new InvalidOperationException("ProcessPath null");
            key.SetValue(ValueName, $"\"{exe}\"");
        }
        else
        {
            key.DeleteValue(ValueName, false);
        }
    }
}

[SupportedOSPlatform("windows")]
public sealed class WindowsShell : IShell
{
    public void OpenFolder(string path)
    {
        Directory.CreateDirectory(path);
        Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });
    }

    public void OpenUrl(string url) =>
        Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
}
