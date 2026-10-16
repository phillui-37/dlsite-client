using System.Diagnostics;

namespace DlsiteClient.Platform.Linux;

/// <summary>Do not pass the AppImage's private GTK/WebKit libraries to host programs.</summary>
public static class LinuxHostEnvironment
{
    public static Process? Start(ProcessStartInfo process)
    {
        Restore(process);
        return Process.Start(process);
    }
    public static readonly string[] Variables =
    [
        "PATH", "LD_LIBRARY_PATH", "LD_PRELOAD", "XDG_DATA_DIRS", "GIO_MODULE_DIR",
        "GIO_EXTRA_MODULES", "GSETTINGS_SCHEMA_DIR", "GTK_PATH", "GTK_EXE_PREFIX",
        "GTK_DATA_PREFIX", "GTK_IM_MODULE_FILE", "GDK_PIXBUF_MODULE_FILE",
        "GDK_PIXBUF_MODULEDIR", "WEBKIT_EXEC_PATH", "WEBKIT_INJECTED_BUNDLE_PATH",
        "WEBKIT_DISABLE_COMPOSITING_MODE", "WEBKIT_DISABLE_DMABUF_RENDERER", "WEBKIT_SKIA_ENABLE_CPU_RENDERING",
        "LIBGL_ALWAYS_SOFTWARE", "EGL_PLATFORM", "LIBGL_DRIVERS_PATH", "__EGL_VENDOR_LIBRARY_FILENAMES",
        "MESA_LOADER_DRIVER_OVERRIDE", "GALLIUM_DRIVER"
    ];

    public static void Restore(ProcessStartInfo process)
    {
        if (Environment.GetEnvironmentVariable("DLSITE_BUNDLED_RUNTIME") != "1") return;
        foreach (var name in Variables)
        {
            var original = Environment.GetEnvironmentVariable("DLSITE_HOST_" + name);
            if (original is null || original == "__DLSITE_UNSET__") process.Environment.Remove(name);
            else process.Environment[name] = original;
            process.Environment.Remove("DLSITE_HOST_" + name);
        }
        process.Environment.Remove("DLSITE_BUNDLED_RUNTIME");
        process.Environment.Remove("DLSITE_SECRET_TOOL");
        process.Environment.Remove("APPDIR");
        process.Environment.Remove("APPIMAGE");
    }
}
