using DlsiteClient.Core.Storage;
using DlsiteClient.Platform;
using Microsoft.Extensions.Logging;

namespace DlsiteClient.Core.Services;

public sealed class LauncherService
{
    private readonly LibraryDatabase _db;
    private readonly IProcessLocale _process;
    private readonly ILogger<LauncherService> _logger;

    private static readonly HashSet<string> IgnoredExeNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "vc_redist.x64.exe", "vc_redist.x86.exe", "vc_redist.arm64.exe",
        "vcredist_x64.exe", "vcredist_x86.exe",
        "UnityCrashHandler64.exe", "UnityCrashHandler32.exe",
        "crashpad_handler.exe", "uninstall.exe", "unins000.exe"
    };

    public LauncherService(LibraryDatabase db, IProcessLocale process, ILogger<LauncherService> logger)
    {
        _db = db;
        _process = process;
        _logger = logger;
    }

    public IReadOnlyList<string> FindExecutables(string installPath)
    {
        if (!Directory.Exists(installPath)) return [];
        return Directory.EnumerateFiles(installPath, "*.exe", SearchOption.AllDirectories)
            .Where(p => !IgnoredExeNames.Contains(Path.GetFileName(p)))
            .OrderBy(p => p.Length)
            .ToList();
    }

    public LaunchDecision PrepareLaunch(string workno)
    {
        var work = _db.GetWork(workno) ?? throw new InvalidOperationException("Work not found");
        if (string.IsNullOrEmpty(work.InstallPath) || !Directory.Exists(work.InstallPath))
            throw new InvalidOperationException("Not installed");

        var exes = FindExecutables(work.InstallPath);
        if (exes.Count == 0)
            throw new InvalidOperationException("No executable found");

        if (!string.IsNullOrEmpty(work.SelectedExe) && File.Exists(work.SelectedExe))
            return LaunchDecision.Ready(work.SelectedExe, work.InstallPath, work.UseJapanLocale);

        if (exes.Count == 1)
            return LaunchDecision.Ready(exes[0], work.InstallPath, work.UseJapanLocale);

        return LaunchDecision.NeedsChoice(exes, work.InstallPath, work.UseJapanLocale);
    }

    public void ConfirmAndLaunch(string workno, string exePath, bool always, bool useJapanLocale)
    {
        var work = _db.GetWork(workno) ?? throw new InvalidOperationException("Work not found");
        // Persist when asked, or remember first successful choice so protocol launches work next time.
        if (always || string.IsNullOrEmpty(work.SelectedExe))
            _db.SetSelectedExe(workno, exePath, useJapanLocale);
        else
            _db.SetSelectedExe(workno, work.SelectedExe, useJapanLocale);

        var wd = Path.GetDirectoryName(exePath) ?? work.InstallPath!;
        _logger.LogInformation("Launching {Exe} for {Workno}", exePath, workno);
        _process.StartWithLocale(exePath, wd, null, useJapanLocale);
    }

    /// <summary>
    /// Protocol / CLI entry: launch if a single exe (or saved choice) is ready;
    /// otherwise return NeedsChoice so the UI can open the picker.
    /// </summary>
    public ProtocolLaunchResult TryProtocolLaunch(string workno)
    {
        try
        {
            var d = PrepareLaunch(workno);
            if (d.NeedsUserChoice)
                return ProtocolLaunchResult.NeedsChoice;
            ConfirmAndLaunch(workno, d.ExePath!, always: false, d.UseJapanLocale);
            return ProtocolLaunchResult.Launched;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Protocol launch failed for {Workno}", workno);
            return ProtocolLaunchResult.Failed(ex.Message);
        }
    }
}

public enum ProtocolLaunchKind
{
    Launched,
    NeedsChoice,
    Failed
}

public readonly struct ProtocolLaunchResult
{
    public ProtocolLaunchKind Kind { get; init; }
    public string? Error { get; init; }

    public static ProtocolLaunchResult Launched => new() { Kind = ProtocolLaunchKind.Launched };
    public static ProtocolLaunchResult NeedsChoice => new() { Kind = ProtocolLaunchKind.NeedsChoice };
    public static ProtocolLaunchResult Failed(string error) =>
        new() { Kind = ProtocolLaunchKind.Failed, Error = error };
}

public sealed class LaunchDecision
{
    public bool NeedsUserChoice { get; init; }
    public string? ExePath { get; init; }
    public IReadOnlyList<string> Candidates { get; init; } = [];
    public string InstallPath { get; init; } = "";
    public bool UseJapanLocale { get; init; }

    public static LaunchDecision Ready(string exe, string install, bool jp) => new()
    {
        NeedsUserChoice = false,
        ExePath = exe,
        InstallPath = install,
        UseJapanLocale = jp
    };

    public static LaunchDecision NeedsChoice(IReadOnlyList<string> exes, string install, bool jp) => new()
    {
        NeedsUserChoice = true,
        Candidates = exes,
        InstallPath = install,
        UseJapanLocale = jp
    };
}
