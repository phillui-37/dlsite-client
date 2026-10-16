using DlsiteClient.Core.Storage;
using DlsiteClient.Platform;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;

namespace DlsiteClient.Core.Services;

public sealed class LauncherService
{
    private readonly LibraryDatabase _db;
    private readonly IProcessLocale _process;
    private readonly ILogger<LauncherService> _logger;
    private readonly IGameProcesses _games;
    private readonly object _launchGate = new();
    private readonly ConcurrentDictionary<string, GameLaunchState> _states = new();
    private readonly ConcurrentDictionary<string, string> _errors = new();
    public event Action? Changed;
    public TimeSpan StartTimeout { get; init; } = TimeSpan.FromMinutes(3);
    public TimeSpan PollInterval { get; init; } = TimeSpan.FromMilliseconds(500);
    public string LaunchLockDirectory { get; init; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolderOption.DoNotVerify), "dlsite-client", "launch-locks");
    public GameLaunchState GetLaunchState(string workno) => _states.GetValueOrDefault(workno);
    public string GetLaunchError(string workno) => _errors.GetValueOrDefault(workno) ?? "launcher.startFailed";
    public bool TryBeginPreparation(string workno)
    {
        lock (_launchGate)
        {
            if (GetLaunchState(workno) is GameLaunchState.Preparing or GameLaunchState.Starting or GameLaunchState.Running) return false;
            _errors.TryRemove(workno, out _);
            _states[workno] = GameLaunchState.Preparing;
            Changed?.Invoke();
            return true;
        }
    }

    public void CancelPreparation(string workno)
    {
        if (_states.TryUpdate(workno, GameLaunchState.Idle, GameLaunchState.Preparing)) Changed?.Invoke();
    }

    private static readonly HashSet<string> IgnoredExeNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "vc_redist.x64.exe", "vc_redist.x86.exe", "vc_redist.arm64.exe",
        "vcredist_x64.exe", "vcredist_x86.exe",
        "UnityCrashHandler64.exe", "UnityCrashHandler32.exe",
        "crashpad_handler.exe", "uninstall.exe", "unins000.exe"
    };

    public LauncherService(LibraryDatabase db, IProcessLocale process, ILogger<LauncherService> logger, IGameProcesses? games = null)
    {
        _db = db;
        _process = process;
        _logger = logger;
        _games = games ?? new GameProcesses();
    }

    public IReadOnlyList<string> FindExecutables(string installPath)
    {
        if (!Directory.Exists(installPath)) return [];
        return Directory.EnumerateFiles(installPath, "*", new EnumerationOptions
            {
                RecurseSubdirectories = true,
                IgnoreInaccessible = true,
                AttributesToSkip = FileAttributes.ReparsePoint
            })
            .Where(IsLaunchable)
            .Where(p => !IgnoredExeNames.Contains(Path.GetFileName(p)))
            .OrderBy(p => p.Length)
            .ToList();
    }

    private static bool IsLaunchable(string path)
    {
        if (path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) return true;
        if (!OperatingSystem.IsLinux()) return false;
        try
        {
            var mode = File.GetUnixFileMode(path);
            if ((mode & (UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute)) == 0)
                return false;
            using var stream = File.OpenRead(path);
            Span<byte> header = stackalloc byte[4];
            var count = stream.Read(header);
            return (count >= 2 && header[0] == '#' && header[1] == '!') ||
                (count == 4 && header[0] == 0x7f && header[1] == 'E' && header[2] == 'L' && header[3] == 'F');
        }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }

    public LaunchDecision PrepareLaunch(string workno)
    {
        var work = _db.GetWork(workno) ?? throw new InvalidOperationException("Work not found");
        if (string.IsNullOrEmpty(work.InstallPath) || !Directory.Exists(work.InstallPath))
            throw new InvalidOperationException("Not installed");

        if (!string.IsNullOrEmpty(work.SelectedExe) && File.Exists(work.SelectedExe))
            return LaunchDecision.Ready(work.SelectedExe, work.InstallPath, work.UseJapanLocale);

        var exes = FindExecutables(work.InstallPath);
        if (exes.Count == 0)
            throw new InvalidOperationException("No executable found");

        if (exes.Count == 1)
            return LaunchDecision.Ready(exes[0], work.InstallPath, work.UseJapanLocale);

        return LaunchDecision.NeedsChoice(exes, work.InstallPath, work.UseJapanLocale);
    }

    public void ConfirmAndLaunch(string workno, string exePath, bool always, bool useJapanLocale)
    {
        try { ConfirmAndLaunchCore(workno, exePath, always, useJapanLocale); }
        catch
        {
            _states[workno] = GameLaunchState.Failed;
            _errors[workno] = "launcher.startFailed";
            Changed?.Invoke();
            throw;
        }
    }

    private void ConfirmAndLaunchCore(string workno, string exePath, bool always, bool useJapanLocale)
    {
        lock (_launchGate)
        {
            if (GetLaunchState(workno) is GameLaunchState.Starting or GameLaunchState.Running) return;
            exePath = Path.GetFullPath(exePath);
            FileStream? lease = null;
            var alreadyRunning = _games.IsRunning(exePath);
            var anotherLaunch = false;
            if (!alreadyRunning)
            {
                Directory.CreateDirectory(LaunchLockDirectory);
                var key = OperatingSystem.IsWindows() ? exePath.ToUpperInvariant() : exePath;
                var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)));
                try { lease = new FileStream(Path.Combine(LaunchLockDirectory, hash + ".lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
                catch (IOException) { anotherLaunch = true; }
            }
            _errors.TryRemove(workno, out _);
            _states[workno] = alreadyRunning ? GameLaunchState.Running : GameLaunchState.Starting;
            Changed?.Invoke();
            try
            {
                if (!alreadyRunning && !anotherLaunch)
                    StartGame(workno, exePath, always, useJapanLocale);
                _ = Task.Run(() => ObserveLaunchAsync(workno, exePath, lease));
            }
            catch
            {
                lease?.Dispose();
                _states[workno] = GameLaunchState.Failed;
                _errors[workno] = "launcher.startFailed";
                Changed?.Invoke();
                throw;
            }
        }
    }

    private void StartGame(string workno, string exePath, bool always, bool useJapanLocale)
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

    private async Task ObserveLaunchAsync(string workno, string exePath, FileStream? lease)
    {
        try
        {
            var timer = Stopwatch.StartNew();
            while (!_games.IsRunning(exePath))
            {
                if (timer.Elapsed >= StartTimeout)
                {
                    _states[workno] = GameLaunchState.Failed;
                    _errors[workno] = "launcher.startTimeout";
                    Changed?.Invoke();
                    return;
                }
                await Task.Delay(PollInterval).ConfigureAwait(false);
            }
            _states[workno] = GameLaunchState.Running;
            Changed?.Invoke();
            while (_games.IsRunning(exePath)) await Task.Delay(PollInterval).ConfigureAwait(false);
            lease?.Dispose();
            _states[workno] = GameLaunchState.Idle;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Launch monitor failed for {Workno}", workno);
            _states[workno] = GameLaunchState.Failed;
            _errors[workno] = "launcher.startFailed";
        }
        finally
        {
            lease?.Dispose();
            Changed?.Invoke();
        }
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
