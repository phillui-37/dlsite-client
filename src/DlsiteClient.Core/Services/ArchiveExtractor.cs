using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace DlsiteClient.Core.Services;

public enum ArchiveTestResult
{
    Ok,
    Corrupt,
    IncompleteSplit,
    ToolMissing,
    UnknownError
}

public sealed class ArchiveExtractor
{
    private readonly ILogger<ArchiveExtractor> _logger;
    private string? _sevenZipPath;

    public ArchiveExtractor(ILogger<ArchiveExtractor> logger)
    {
        _logger = logger;
    }

    public string? ResolveSevenZip()
    {
        if (_sevenZipPath is not null) return _sevenZipPath;
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "7z.exe"),
            Path.Combine(AppContext.BaseDirectory, "tools", "7z.exe"),
            @"C:\Program Files\7-Zip\7z.exe",
            @"C:\Program Files (x86)\7-Zip\7z.exe",
            Environment.GetEnvironmentVariable("SCOOP") is { } scoop
                ? Path.Combine(scoop, "apps", "7zip", "current", "7z.exe")
                : null,
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "scoop", "apps", "7zip", "current", "7z.exe")
        };
        foreach (var c in candidates)
        {
            if (c is not null && File.Exists(c))
            {
                _sevenZipPath = c;
                return c;
            }
        }
        return null;
    }

    public async Task<(bool ok, string message)> ExtractAsync(
        string archivePath,
        string destDir,
        CancellationToken ct = default)
    {
        var seven = ResolveSevenZip();
        if (seven is null)
            return (false, "7z.exe not found. Install 7-Zip (scoop install 7zip) or place 7z.exe next to the app.");

        Directory.CreateDirectory(destDir);
        var psi = new ProcessStartInfo
        {
            FileName = seven,
            Arguments = $"x \"{archivePath}\" -o\"{destDir}\" -y -bsp1",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        using var proc = new Process { StartInfo = psi };
        proc.Start();
        var stdoutTask = proc.StandardOutput.ReadToEndAsync(ct);
        var stderrTask = proc.StandardError.ReadToEndAsync(ct);
        await proc.WaitForExitAsync(ct).ConfigureAwait(false);
        var stdout = await stdoutTask.ConfigureAwait(false);
        var stderr = await stderrTask.ConfigureAwait(false);
        _logger.LogDebug("7z extract exit={Code} stderr={Err}", proc.ExitCode, stderr);
        if (proc.ExitCode != 0)
            return (false, string.IsNullOrWhiteSpace(stderr) ? stdout : stderr);
        return (true, "ok");
    }

    public async Task<ArchiveTestResult> TestAsync(string archivePath, CancellationToken ct = default)
    {
        if (!File.Exists(archivePath))
            return ArchiveTestResult.IncompleteSplit;

        if (LooksLikeIncompleteSplit(archivePath))
            return ArchiveTestResult.IncompleteSplit;

        var seven = ResolveSevenZip();
        if (seven is null)
            return ArchiveTestResult.ToolMissing;

        var psi = new ProcessStartInfo
        {
            FileName = seven,
            Arguments = $"t \"{archivePath}\" -y",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        try
        {
            using var proc = new Process { StartInfo = psi };
            proc.Start();
            await proc.WaitForExitAsync(ct).ConfigureAwait(false);
            if (proc.ExitCode == 0) return ArchiveTestResult.Ok;
            _logger.LogWarning("7z t failed for {Path} code={Code}", archivePath, proc.ExitCode);
            return ArchiveTestResult.Corrupt;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "7z t error");
            return ArchiveTestResult.UnknownError;
        }
    }

    public static string? FindPrimaryArchive(string cacheWorkDir)
    {
        if (!Directory.Exists(cacheWorkDir)) return null;
        var files = Directory.GetFiles(cacheWorkDir);
        // Prefer first volume of split sets
        var part001 = files.FirstOrDefault(f =>
            f.EndsWith(".001", StringComparison.OrdinalIgnoreCase) ||
            f.EndsWith(".7z.001", StringComparison.OrdinalIgnoreCase));
        if (part001 is not null) return part001;
        var rar = files.FirstOrDefault(f => f.EndsWith(".rar", StringComparison.OrdinalIgnoreCase));
        if (rar is not null) return rar;
        var seven = files.FirstOrDefault(f => f.EndsWith(".7z", StringComparison.OrdinalIgnoreCase));
        if (seven is not null) return seven;
        return files.FirstOrDefault(f => f.EndsWith(".zip", StringComparison.OrdinalIgnoreCase));
    }

    private static bool LooksLikeIncompleteSplit(string archivePath)
    {
        var dir = Path.GetDirectoryName(archivePath)!;
        var name = Path.GetFileName(archivePath);
        if (name.EndsWith(".001", StringComparison.OrdinalIgnoreCase))
        {
            var prefix = name[..^4];
            var parts = Directory.GetFiles(dir, prefix + "*");
            // If only .001 exists and size suggests more, still let 7z decide; check .002 missing when .001 small? skip
            // Require at least that consecutive numbering doesn't jump from 001 to missing 002 when 003 exists
            var numbered = parts
                .Select(p => Path.GetExtension(p).TrimStart('.'))
                .Where(e => int.TryParse(e, out _))
                .Select(int.Parse)
                .OrderBy(x => x)
                .ToList();
            if (numbered.Count >= 1)
            {
                for (var i = 1; i <= numbered.Max(); i++)
                {
                    if (!numbered.Contains(i)) return true;
                }
            }
        }
        return false;
    }
}
