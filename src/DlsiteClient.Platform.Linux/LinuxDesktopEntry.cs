using System.Runtime.Versioning;

namespace DlsiteClient.Platform.Linux;

[SupportedOSPlatform("linux")]
public static class LinuxDesktopEntry
{
    public static string DataHome => Xdg("XDG_DATA_HOME", ".local", "share");
    public static string ConfigHome => Xdg("XDG_CONFIG_HOME", ".config");

    private static string Xdg(string variable, params string[] fallback)
    {
        var value = Environment.GetEnvironmentVariable(variable);
        return !string.IsNullOrWhiteSpace(value) && Path.IsPathRooted(value)
            ? value : Path.Combine(new[] { Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) }.Concat(fallback).ToArray());
    }

    public static string ResolveExecutable(string? processPath)
    {
        var image = Environment.GetEnvironmentVariable("APPIMAGE");
        if (!string.IsNullOrWhiteSpace(image) && Path.IsPathRooted(image) && File.Exists(image))
            return image;
        return processPath ?? throw new InvalidOperationException("No application executable path");
    }

    public static string QuoteExecutable(string path)
    {
        // First quote Exec metacharacters, then escape backslashes for desktop-entry parsing.
        var quoted = path.Replace("\\", "\\\\").Replace("\"", "\\\"")
            .Replace("`", "\\`").Replace("$", "\\$").Replace("%", "%%");
        return "\"" + quoted.Replace("\\", "\\\\") + "\"";
    }
}
