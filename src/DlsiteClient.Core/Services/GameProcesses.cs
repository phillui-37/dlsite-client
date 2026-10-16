using System.Diagnostics;

namespace DlsiteClient.Core.Services;

public enum GameLaunchState { Idle, Preparing, Starting, Running, Failed }

public interface IGameProcesses
{
    bool IsRunning(string exePath);
}

public sealed class GameProcesses : IGameProcesses
{
    public bool IsRunning(string exePath)
    {
        var path = Path.GetFullPath(exePath);
        var file = Path.GetFileName(path);
        var stem = Path.GetFileNameWithoutExtension(path);
        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                var matchedName = false;
                try
                {
                    if (process.HasExited) continue;
                    var name = process.ProcessName;
                    if (!string.Equals(name, stem, StringComparison.OrdinalIgnoreCase) &&
                        !string.Equals(name, file, StringComparison.OrdinalIgnoreCase) &&
                        !(OperatingSystem.IsLinux() && file.Length > 15 && string.Equals(name, file[..15], StringComparison.OrdinalIgnoreCase))) continue;
                    matchedName = true;
                    if (OperatingSystem.IsLinux() && file.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                    {
                        // Wine's main module is Wine itself; the target is an argv path.
                        var args = File.ReadAllText($"/proc/{process.Id}/cmdline").Split('\0');
                        if (args.Any(arg => string.Equals(arg.Replace("Z:\\", "/", StringComparison.OrdinalIgnoreCase).Replace('\\', '/'), path, StringComparison.OrdinalIgnoreCase))) return true;
                        continue;
                    }
                    var actual = process.MainModule?.FileName;
                    if (actual is not null && string.Equals(Path.GetFullPath(actual), path,
                        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)) return true;
                }
                catch (System.ComponentModel.Win32Exception)
                {
                    if (matchedName && OperatingSystem.IsWindows()) return true;
                }
                catch (InvalidOperationException) { /* Process exited during inspection. */ }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
        return false;
    }
}
