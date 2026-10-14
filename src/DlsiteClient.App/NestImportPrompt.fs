module DlsiteClient.App.NestImportPrompt

open System.Runtime.InteropServices
open DlsiteClient.App.Localization
open DlsiteClient.App.Services
open DlsiteClient.Platform

[<DllImport("user32.dll", CharSet = CharSet.Unicode)>]
extern int private MessageBoxW(nativeint hWnd, string text, string caption, uint32 typ)

let private MB_YESNO = 0x4u
let private MB_ICONQUESTION = 0x20u
let private IDYES = 6

/// First-run: offer Nest library/cache paths only (never credentials).
let maybeAsk (svc: AppServices) =
    if svc.Settings.Current.NestImportAsked then ()
    else
        let mutable paths: NestPathImport = Unchecked.defaultof<_>
        if svc.NestImport.TryReadPaths(&paths) && not (isNull (box paths)) then
            let r =
                MessageBoxW(
                    0n,
                    t "settings.nestImport",
                    t "settings.nestImportTitle",
                    MB_YESNO ||| MB_ICONQUESTION)
            if r = IDYES then
                svc.Settings.Update(fun s ->
                    s.LibraryDirectory <- paths.LibraryDirectory
                    s.CacheDirectory <- paths.CacheDirectory
                    s.NestImportAsked <- true)
            else
                svc.Settings.Update(fun s -> s.NestImportAsked <- true)
        else
            svc.Settings.Update(fun s -> s.NestImportAsked <- true)
