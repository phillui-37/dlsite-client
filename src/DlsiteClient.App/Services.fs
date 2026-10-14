module DlsiteClient.App.Services

open System
open System.IO
open System.Runtime.InteropServices
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Logging
open Serilog
open Serilog.Events
open DlsiteClient.Core
open DlsiteClient.Core.Services
open DlsiteClient.Platform
open DlsiteClient.Platform.Windows
open DlsiteClient.App.Localization

type AppServices =
    { Provider: ServiceProvider
      Auth: AuthSession
      Library: LibraryService
      Downloads: DownloadQueueService
      Settings: SettingsService
      Launcher: LauncherService
      Paths: IPathConfig
      NestImport: INestSettingsImporter
      Protocol: IProtocolHandler
      Startup: IStartupRegistration
      Shell: IShell
      LocalesDir: string }

let private parseLevel (s: string) =
    match s.ToLowerInvariant() with
    | "verbose" | "trace" -> LogEventLevel.Verbose
    | "debug" -> LogEventLevel.Debug
    | "warning" -> LogEventLevel.Warning
    | "error" -> LogEventLevel.Error
    | "fatal" | "critical" -> LogEventLevel.Fatal
    | _ -> LogEventLevel.Information

let private bootstrapLogger (logDir: string) (level: string) =
    Directory.CreateDirectory logDir |> ignore
    Log.Logger <-
        LoggerConfiguration()
            .MinimumLevel.Is(parseLevel level)
            .WriteTo.File(
                Path.Combine(logDir, "dlsite-client-.log"),
                rollingInterval = RollingInterval.Day,
                fileSizeLimitBytes = Nullable(20L * 1024L * 1024L),
                rollOnFileSizeLimit = true,
                retainedFileCountLimit = Nullable 14)
            .CreateLogger()

let build (_args: string[]) : AppServices =
    let paths: IPathConfig = WindowsPathConfig() :> IPathConfig
    Directory.CreateDirectory paths.AppDataRoot |> ignore
    bootstrapLogger paths.DefaultLogDirectory "Information"

    let localesDir =
        let candidates =
            [ Path.Combine(AppContext.BaseDirectory, "locales")
              Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "locales"))
              Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "locales")) ]
        candidates |> List.tryFind Directory.Exists |> Option.defaultValue (Path.Combine(AppContext.BaseDirectory, "locales"))

    let sc = ServiceCollection()
    sc.AddLogging(fun b ->
        b.ClearProviders() |> ignore
        b.AddSerilog(dispose = true) |> ignore) |> ignore
    sc.AddSingleton<ICredentialStore, WindowsCredentialStore>() |> ignore
    sc.AddSingleton<INestSettingsImporter, WindowsNestSettingsImporter>() |> ignore
    sc.AddSingleton<IProtocolHandler, WindowsProtocolHandler>() |> ignore
    sc.AddSingleton<IProcessLocale, WindowsProcessLocale>() |> ignore
    sc.AddSingleton<IStartupRegistration, WindowsStartupRegistration>() |> ignore
    sc.AddSingleton<IShell, WindowsShell>() |> ignore
    sc.AddDlsiteCore(paths) |> ignore

    let sp = sc.BuildServiceProvider()
    let settings = sp.GetRequiredService<SettingsService>()

    // Re-init logger with user path/level
    bootstrapLogger settings.Current.LogDirectory settings.Current.LogLevel
    globalI18n.Load(settings.Current.UiLocale, localesDir)

    let auth = sp.GetRequiredService<AuthSession>()
    auth.RestoreAsync().GetAwaiter().GetResult()

    let protocol = sp.GetRequiredService<IProtocolHandler>()
    if RuntimeInformation.IsOSPlatform OSPlatform.Windows then
        match Environment.ProcessPath with
        | null -> ()
        | exe -> protocol.Register(WindowsProtocolHandler.Scheme, exe)

    { Provider = sp
      Auth = auth
      Library = sp.GetRequiredService<LibraryService>()
      Downloads = sp.GetRequiredService<DownloadQueueService>()
      Settings = settings
      Launcher = sp.GetRequiredService<LauncherService>()
      Paths = paths
      NestImport = sp.GetRequiredService<INestSettingsImporter>()
      Protocol = protocol
      Startup = sp.GetRequiredService<IStartupRegistration>()
      Shell = sp.GetRequiredService<IShell>()
      LocalesDir = localesDir }

let mutable shared: AppServices option = None

/// When protocol play needs an exe picker, MainView opens this work's detail.
let mutable pendingPlayWorkno: string option = None

let applyLogSettings (logDir: string) (level: string) =
    bootstrapLogger logDir level

let get () =
    match shared with
    | Some s -> s
    | None -> failwith "Services not initialized"
