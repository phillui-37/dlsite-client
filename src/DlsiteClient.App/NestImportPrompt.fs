module DlsiteClient.App.NestImportPrompt

open DlsiteClient.App.Localization
open DlsiteClient.App.Services
open DlsiteClient.Platform

let private tryUpdate (svc: AppServices) (mutate: DlsiteClient.Core.Models.AppSettings -> unit) =
    try
        svc.Settings.Update(mutate)
    with ex ->
        // Never kill startup on settings I/O (AOT JSON bugs, disk, etc.)
        System.Diagnostics.Debug.WriteLine("NestImport settings update failed: " + ex.Message)

/// First-run: offer Nest library/cache paths only (never credentials).
let maybeAsk (svc: AppServices) =
    if svc.Settings.Current.NestImportAsked then ()
    else
        let mutable paths: NestPathImport = Unchecked.defaultof<_>
        if svc.NestImport.TryReadPaths(&paths) && not (isNull (box paths)) then
            if svc.Shell.Confirm(t "settings.nestImport", t "settings.nestImportTitle") then
                tryUpdate svc (fun s ->
                    s.LibraryDirectory <- paths.LibraryDirectory
                    s.CacheDirectory <- paths.CacheDirectory
                    s.NestImportAsked <- true)
            else
                tryUpdate svc (fun s -> s.NestImportAsked <- true)
        else
            tryUpdate svc (fun s -> s.NestImportAsked <- true)
