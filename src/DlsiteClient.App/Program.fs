namespace DlsiteClient.App

open System
open System.Runtime.InteropServices
open Avalonia
open DlsiteClient.Core.Services
open DlsiteClient.App.Services

module Program =

    [<DllImport("user32.dll", CharSet = CharSet.Unicode)>]
    extern int MessageBoxW(nativeint hWnd, string text, string caption, uint32 typ)

    let private showFatal (title: string) (msg: string) =
        MessageBoxW(0n, msg, title, 0x10u) |> ignore

    [<EntryPoint>]
    let main argv =
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
