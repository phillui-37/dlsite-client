namespace DlsiteClient.App

open System
open System.Runtime.InteropServices
open System.Threading
open Avalonia
open DlsiteClient.Core.Services
open DlsiteClient.App.Services

module Program =

    [<DllImport("user32.dll", CharSet = CharSet.Unicode)>]
    extern int MessageBoxW(nativeint hWnd, string text, string caption, uint32 typ)

    let private showFatal (title: string) (msg: string) =
        MessageBoxW(0n, msg, title, 0x10u) |> ignore

    // WebView2 needs STA. Without STAThread, .NET leaves main as MTA and
    // NativeWebDialog CreateBuilder dies with COMException 0x80010106 (RPC_E_CHANGED_MODE).
    [<STAThread>]
    [<EntryPoint>]
    let main argv =
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

            AppBuilder
                .Configure<App>()
                .UsePlatformDetect()
                .WithInterFont()
                .LogToTrace()
                .StartWithClassicDesktopLifetime(argv)
        with ex ->
            showFatal "DLsite Client" ("Startup failed: " + ex.ToString())
            1
