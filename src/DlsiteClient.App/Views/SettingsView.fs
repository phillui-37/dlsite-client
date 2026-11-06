module DlsiteClient.App.Views.SettingsView

open System
open System.Runtime.InteropServices
open Avalonia.Controls
open Avalonia.Layout
open Avalonia.Media
open Avalonia.FuncUI
open Avalonia.FuncUI.DSL
open Avalonia.FuncUI.Types
open DlsiteClient.App.Localization
#if WINDOWS
open DlsiteClient.App.NestImportPrompt
#endif
open DlsiteClient.App.OAuthLogin
open DlsiteClient.App.Services
open DlsiteClient.App.Palette
open DlsiteClient.App.UiAsync
open DlsiteClient.Platform

let private colorOutline = Outline
let private colorOnSurfaceVariant = InkMuted

let private sectionCard (titleKey: string) (children: IView list) : IView =
    Border.create [
        Border.cornerRadius 8.0
        Border.background Surface
        Border.borderBrush colorOutline
        Border.borderThickness 1.0
        Border.padding 16.0
        Border.child (
            StackPanel.create [
                StackPanel.spacing 10.0
                StackPanel.children [
                    TextBlock.create [
                        TextBlock.text (t titleKey)
                        TextBlock.fontSize 16.0
                        TextBlock.fontWeight FontWeight.SemiBold
                        TextBlock.foreground Ink
                    ]
                    yield! children
                ]
            ])
    ]
    :> IView

/// Two-column settings row: wrapping label on the left, control on the right.
let private settingRow (labelKey: string) (control: IView) : IView =
    DockPanel.create [
        DockPanel.minHeight 36.0
        DockPanel.children [
            TextBlock.create [
                DockPanel.dock Dock.Left
                TextBlock.width 240.0
                TextBlock.text (t labelKey)
                TextBlock.textWrapping TextWrapping.Wrap
                TextBlock.verticalAlignment VerticalAlignment.Center
                TextBlock.foreground colorOnSurfaceVariant
            ]
            control
        ]
    ]
    :> IView

let view () =
    Component(fun ctx ->
        let svc = get ()
        let s0 = svc.Settings.Current
        let lib = ctx.useState s0.LibraryDirectory
        let cache = ctx.useState s0.CacheDirectory
        let logPath = ctx.useState s0.LogDirectory
        let logLevel = ctx.useState s0.LogLevel
        let locale = ctx.useState s0.UiLocale
        let maxDl = ctx.useState (string s0.MaxConcurrentDownloads)
        let maxEx = ctx.useState (string s0.MaxConcurrentExtracts)
        let rate = ctx.useState (string s0.RateLimitMBps)
        let proxyMode = ctx.useState s0.ProxyMode
        let proxyUrl = ctx.useState (if isNull s0.ProxyUrl then "" else s0.ProxyUrl)
        let protonPath = ctx.useState (if isNull s0.ProtonPath then "" else s0.ProtonPath)
        let startLogin = ctx.useState s0.StartOnLogin
        let status = ctx.useState ""
        let sidInput = ctx.useState ""
        let sidOpen = ctx.useState false
        let busy = ctx.useState false
        let debounce = debounce 400
        let onLinux = RuntimeInformation.IsOSPlatform OSPlatform.Linux

        let persist (mutate: DlsiteClient.Core.Models.AppSettings -> unit) =
            svc.Settings.Update mutate

        let errMsg (ex: exn) =
            if svc.Auth.NeedsRelogin then t "auth.sessionExpired" else errorText ex

        let applyPaths () =
            persist (fun s ->
                s.LibraryDirectory <- lib.Current
                s.CacheDirectory <- cache.Current)
            runPoolAsyncUnit
                (fun () -> svc.Library.ReconcileLocalInstallsAsync())
                (fun () -> ())
                (fun _ -> ())

        let applyLog () =
            persist (fun s ->
                s.LogDirectory <- logPath.Current
                s.LogLevel <- logLevel.Current)
            applyLogSettings logPath.Current logLevel.Current

        let applyLocale (code: string) =
            locale.Set code
            persist (fun s ->
                s.UiLocale <- code
                s.ApiLocale <- if code = "zh-TW" then "zh_TW" else "ja_JP")
            globalI18n.Load(code, svc.LocalesDir)
            status.Set (t "settings.applied")

        let applyDownload () =
            persist (fun s ->
                s.MaxConcurrentDownloads <-
                    match Int32.TryParse maxDl.Current with
                    | true, v -> max 1 v
                    | _ -> 2
                s.MaxConcurrentExtracts <-
                    match Int32.TryParse maxEx.Current with
                    | true, v -> max 1 v
                    | _ -> 1
                s.RateLimitMBps <-
                    match Double.TryParse rate.Current with
                    | true, v -> max 0.0 v
                    | _ -> 0.0
                s.ProxyMode <- proxyMode.Current
                s.ProxyUrl <- if String.IsNullOrWhiteSpace proxyUrl.Current then null else proxyUrl.Current)

        let syncAfterLogin () =
            busy.Set true
            status.Set (t "auth.syncing")
            runPoolAsyncUnit
                (fun () -> svc.Library.SyncPurchasesAsync())
                (fun () ->
                    busy.Set false
                    status.Set (t "auth.synced"))
                (fun ex ->
                    busy.Set false
                    status.Set (errMsg ex))

        let langName code = if code = "zh-TW" then t "settings.language.zh-TW" else t "settings.language.ja"
        let proxyLabel mode =
            match mode with
            | "system" -> t "settings.proxy.system"
            | "custom" -> t "settings.proxy.custom"
            | _ -> t "settings.proxy.off"

        let accountCard =
            sectionCard "settings.account" [
                TextBlock.create [
                    TextBlock.text (
                        if svc.Auth.IsLoggedIn then
                            let id = svc.Auth.LoginId
                            if String.IsNullOrEmpty id then t "auth.loginOk" else id
                        else
                            t "auth.login")
                ]
                StackPanel.create [
                    StackPanel.orientation Orientation.Horizontal
                    StackPanel.spacing 8.0
                    StackPanel.children [
                        Button.create [
                            Button.content (t "auth.login")
                            Button.isEnabled (not busy.Current)
                            Button.onClick (fun _ ->
                                status.Set (t "auth.loggingIn")
                                busy.Set true
                                runAsync
                                    (fun () -> loginAsync svc)
                                    (fun r ->
                                        busy.Set false
                                        if r = "ok" then
                                            status.Set (t "auth.loginOk")
                                            syncAfterLogin ()
                                        elif r = "cancelled" then
                                            status.Set (t "auth.cancelled")
                                        else
                                            status.Set r)
                                    (fun ex ->
                                        busy.Set false
                                        status.Set (errMsg ex)))
                        ]
                        Button.create [
                            Button.content (t "auth.logout")
                            Button.isEnabled (not busy.Current)
                            Button.onClick (fun _ ->
                                busy.Set true
                                runAsyncUnit
                                    (fun () -> svc.Auth.LogoutAsync())
                                    (fun () ->
                                        busy.Set false
                                        status.Set (t "auth.logout"))
                                    (fun ex ->
                                        busy.Set false
                                        status.Set (errMsg ex)))
                        ]
                        Button.create [
                            Button.content (t "auth.sync")
                            Button.isEnabled (not busy.Current)
                            Button.onClick (fun _ ->
                                busy.Set true
                                status.Set (t "auth.syncing")
                                runPoolAsyncUnit
                                    (fun () -> svc.Library.SyncPurchasesAsync())
                                    (fun () ->
                                        busy.Set false
                                        status.Set (t "auth.synced"))
                                    (fun ex ->
                                        busy.Set false
                                        status.Set (errMsg ex)))
                        ]
                    ]
                ]
                // Manual SID fallback is a debug path — collapsed out of the primary flow.
                Button.create [
                    Button.horizontalAlignment HorizontalAlignment.Left
                    Button.padding (0.0, 4.0)
                    Button.background "Transparent"
                    Button.content ((if sidOpen.Current then "▾  " else "▸  ") + t "auth.sidFallback")
                    Button.onClick (fun _ -> sidOpen.Set(not sidOpen.Current))
                ]
                if sidOpen.Current then
                    StackPanel.create [
                        StackPanel.orientation Orientation.Horizontal
                        StackPanel.spacing 8.0
                        StackPanel.children [
                            TextBox.create [
                                TextBox.width 260.0
                                TextBox.watermark (t "auth.sidPlaceholder")
                                TextBox.passwordChar '*'
                                TextBox.text sidInput.Current
                                TextBox.onTextChanged sidInput.Set
                            ]
                            Button.create [
                                Button.content (t "auth.sidLogin")
                                Button.isEnabled (not busy.Current)
                                Button.onClick (fun _ ->
                                    if not (String.IsNullOrWhiteSpace sidInput.Current) then
                                        let sid = sidInput.Current.Trim()
                                        busy.Set true
                                        runAsyncUnit
                                            (fun () -> svc.Auth.ApplyLoginAsync(sid, null))
                                            (fun () ->
                                                sidInput.Set ""
                                                busy.Set false
                                                status.Set (t "auth.loginOk")
                                                syncAfterLogin ())
                                            (fun ex ->
                                                busy.Set false
                                                status.Set (errMsg ex)))
                            ]
                        ]
                    ]
            ]

        let environmentCard =
            sectionCard "settings.environment" [
                settingRow "settings.libraryFolder" (
                    TextBox.create [
                        TextBox.text lib.Current
                        TextBox.onTextChanged (fun s ->
                            lib.Set s
                            debounce applyPaths)
                    ] :> IView)
                settingRow "settings.cacheFolder" (
                    TextBox.create [
                        TextBox.text cache.Current
                        TextBox.onTextChanged (fun s ->
                            cache.Set s
                            debounce applyPaths)
                    ] :> IView)
                CheckBox.create [
                    CheckBox.content (t "settings.startOnLogin")
                    CheckBox.isChecked startLogin.Current
                    CheckBox.onIsCheckedChanged (fun e ->
                        match e.Source with
                        | :? CheckBox as cb ->
                            let v = cb.IsChecked = Nullable true
                            startLogin.Set v
                            persist (fun s -> s.StartOnLogin <- v)
                            svc.Startup.SetEnabled v
                        | _ -> ())
                ]
                settingRow "settings.language" (
                    ComboBox.create [
                        ComboBox.width 220.0
                        ComboBox.horizontalAlignment HorizontalAlignment.Left
                        ComboBox.dataItems [ langName "ja"; langName "zh-TW" ]
                        ComboBox.selectedItem (langName locale.Current)
                        ComboBox.onSelectedItemChanged (fun o ->
                            match o with
                            | :? string as s when s <> langName locale.Current ->
                                applyLocale (if s = t "settings.language.zh-TW" then "zh-TW" else "ja")
                            | _ -> ())
                    ] :> IView)
#if WINDOWS
                Button.create [
                    Button.horizontalAlignment HorizontalAlignment.Left
                    Button.content (t "settings.nestImportTitle")
                    Button.onClick (fun _ ->
                        svc.Settings.Update(fun s -> s.NestImportAsked <- false)
                        maybeAsk svc
                        let s = svc.Settings.Current
                        lib.Set s.LibraryDirectory
                        cache.Set s.CacheDirectory
                        runPoolAsyncUnit
                            (fun () -> svc.Library.ReconcileLocalInstallsAsync())
                            (fun () -> ())
                            (fun _ -> ())
                        status.Set (t "settings.nestImportTitle"))
                ]
#endif
            ]

        let downloadCard =
            sectionCard "settings.download" [
                settingRow "settings.maxDownloads" (
                    TextBox.create [
                        TextBox.width 120.0
                        TextBox.horizontalAlignment HorizontalAlignment.Left
                        TextBox.text maxDl.Current
                        TextBox.onTextChanged (fun s ->
                            maxDl.Set s
                            debounce applyDownload)
                    ] :> IView)
                settingRow "settings.maxExtracts" (
                    TextBox.create [
                        TextBox.width 120.0
                        TextBox.horizontalAlignment HorizontalAlignment.Left
                        TextBox.text maxEx.Current
                        TextBox.onTextChanged (fun s ->
                            maxEx.Set s
                            debounce applyDownload)
                    ] :> IView)
                settingRow "settings.rateLimit" (
                    TextBox.create [
                        TextBox.width 120.0
                        TextBox.horizontalAlignment HorizontalAlignment.Left
                        TextBox.text rate.Current
                        TextBox.onTextChanged (fun s ->
                            rate.Set s
                            debounce applyDownload)
                    ] :> IView)
                settingRow "settings.proxy" (
                    StackPanel.create [
                        StackPanel.orientation Orientation.Horizontal
                        StackPanel.spacing 8.0
                        StackPanel.children [
                            ComboBox.create [
                                ComboBox.width 140.0
                                ComboBox.dataItems [ proxyLabel "off"; proxyLabel "system"; proxyLabel "custom" ]
                                ComboBox.selectedItem (proxyLabel proxyMode.Current)
                                ComboBox.onSelectedItemChanged (fun o ->
                                    match o with
                                    | :? string as s ->
                                        let mode =
                                            if s = t "settings.proxy.system" then "system"
                                            elif s = t "settings.proxy.custom" then "custom"
                                            else "off"
                                        if mode <> proxyMode.Current then
                                            proxyMode.Set mode
                                            applyDownload ()
                                    | _ -> ())
                            ]
                            TextBox.create [
                                TextBox.width 260.0
                                TextBox.watermark "http://host:port"
                                TextBox.text proxyUrl.Current
                                TextBox.isEnabled (proxyMode.Current = "custom")
                                TextBox.onTextChanged (fun s ->
                                    proxyUrl.Set s
                                    debounce applyDownload)
                            ]
                        ]
                    ] :> IView)
            ]

        let logLevels =
            [ "Verbose", "settings.logLevel.verbose"
              "Debug", "settings.logLevel.debug"
              "Information", "settings.logLevel.info"
              "Warning", "settings.logLevel.warning"
              "Error", "settings.logLevel.error"
              "Fatal", "settings.logLevel.fatal" ]

        let loggingCard =
            sectionCard "settings.logging" [
                settingRow "settings.logLevel" (
                    ComboBox.create [
                        ComboBox.width 160.0
                        ComboBox.horizontalAlignment HorizontalAlignment.Left
                        ComboBox.dataItems (logLevels |> List.map (fun (_, key) -> t key))
                        ComboBox.selectedItem (
                            logLevels
                            |> List.tryFind (fun (value, _) -> value = logLevel.Current)
                            |> Option.map (fun (_, key) -> t key)
                            |> Option.defaultValue (t "settings.logLevel.info"))
                        ComboBox.onSelectedItemChanged (fun o ->
                            match o with
                            | :? string as label ->
                                match logLevels |> List.tryFind (fun (_, key) -> t key = label) with
                                | Some (value, _) when value <> logLevel.Current ->
                                    logLevel.Set value
                                    applyLog ()
                                | _ -> ()
                            | _ -> ())
                    ] :> IView)
                settingRow "settings.logPath" (
                    StackPanel.create [
                        StackPanel.orientation Orientation.Horizontal
                        StackPanel.spacing 8.0
                        StackPanel.children [
                            TextBox.create [
                                TextBox.minWidth 240.0
                                TextBox.text logPath.Current
                                TextBox.onTextChanged (fun s ->
                                    logPath.Set s
                                    debounce applyLog)
                            ]
                            Button.create [
                                Button.content (t "settings.open")
                                Button.onClick (fun _ -> svc.Shell.OpenFolder logPath.Current)
                            ]
                        ]
                    ] :> IView)
            ]

        let linuxCard =
            if onLinux then
                [ sectionCard "settings.linux" [
                      settingRow "settings.protonPath" (
                          TextBox.create [
                              TextBox.watermark "/path/to/Proton/proton"
                              TextBox.text protonPath.Current
                              TextBox.onTextChanged (fun s ->
                                  protonPath.Set s
                                  debounce (fun () ->
                                      persist (fun st ->
                                          st.ProtonPath <-
                                              if String.IsNullOrWhiteSpace protonPath.Current then null
                                              else protonPath.Current)))
                          ] :> IView)
                  ] ]
            else []

        let pageCards : IView list =
            [ accountCard; environmentCard; downloadCard; loggingCard ] @ linuxCard

        ScrollViewer.create [
            ScrollViewer.background Paper
            ScrollViewer.content (
                StackPanel.create [
                    StackPanel.margin 20.0
                    StackPanel.spacing 12.0
                    StackPanel.maxWidth 780.0
                    StackPanel.horizontalAlignment HorizontalAlignment.Left
                    StackPanel.children (
                        pageCards
                        @ [ TextBlock.create [
                              TextBlock.text (
                                if busy.Current then t "settings.working" else status.Current)
                              TextBlock.opacity 0.8
                              TextBlock.fontSize 13.0
                              TextBlock.textWrapping TextWrapping.Wrap
                           ] :> IView ])
                ])
        ])
