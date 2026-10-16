namespace DlsiteClient.App

open System
open System.Diagnostics
open System.IO
open System.Runtime.InteropServices
open System.Threading
open Avalonia
open DlsiteClient.Core.Services
open DlsiteClient.App.Services

module Program =

    [<DllImport("user32.dll", CharSet = CharSet.Unicode)>]
    extern int private MessageBoxW(nativeint hWnd, string text, string caption, uint32 typ)

    let private showFatal (title: string) (msg: string) =
        Console.Error.WriteLine(String.Concat(title, ": ", msg))
        if RuntimeInformation.IsOSPlatform OSPlatform.Windows then
            MessageBoxW(0n, msg, title, 0x10u) |> ignore
        else
            try
                let psi =
                    ProcessStartInfo(
                        FileName = "zenity",
                        UseShellExecute = false,
                        CreateNoWindow = true)
                psi.ArgumentList.Add("--error")
                psi.ArgumentList.Add("--title")
                psi.ArgumentList.Add(title)
                psi.ArgumentList.Add("--text")
                psi.ArgumentList.Add(msg)
                use p = Process.Start(psi)
                if not (isNull p) then p.WaitForExit(15_000) |> ignore
            with _ -> ()

    // WebView2 needs STA on Windows. Harmless on Linux.
    [<STAThread>]
    [<EntryPoint>]
    let main argv =
        if OperatingSystem.IsWindows() then
            ignore (Thread.CurrentThread.TrySetApartmentState ApartmentState.STA)
        try
            let svc = build argv
            Services.shared <- Some svc

            // Deep link: dlsite-client://play/{workno}
            let mutable workno = null
            if svc.Protocol.TryParsePlayWorkno(argv, &workno) && not (isNull workno) then
                let r = svc.Launcher.TryProtocolLaunch workno
                match r.Kind with
                | ProtocolLaunchKind.Launched -> ()
                | ProtocolLaunchKind.NeedsChoice ->
                    Services.pendingPlayWorkno <- Some workno
                | ProtocolLaunchKind.Failed
                | _ ->
                    let err =
                        if String.IsNullOrEmpty r.Error then "Launch failed" else r.Error
                    showFatal "DLsite Client" err
                    Services.pendingPlayWorkno <- Some workno

            AppDomain.CurrentDomain.UnhandledException.Add(fun args ->
                let ex = args.ExceptionObject :?> Exception
                showFatal "DLsite Client" ("Unhandled: " + ex.ToString()))

            let builder = AppBuilder.Configure<App>()
            if OperatingSystem.IsLinux() &&
               (not (Directory.Exists "/dev/dri") ||
                Directory.GetFiles("/dev/dri", "renderD*").Length = 0) then
                builder.With(X11PlatformOptions(RenderingMode = [| X11RenderingMode.Software |])) |> ignore

            builder
                .UsePlatformDetect()
                .WithInterFont()
                .LogToTrace()
                .StartWithClassicDesktopLifetime(argv)
        with ex ->
            showFatal "DLsite Client" ("Startup failed: " + ex.ToString())
            1
