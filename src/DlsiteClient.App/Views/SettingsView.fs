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
open DlsiteClient.App.NestImportPrompt
open DlsiteClient.App.OAuthLogin
open DlsiteClient.App.Services
open DlsiteClient.App.UiAsync
open DlsiteClient.Platform

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
        let busy = ctx.useState false
        let debounce = debounce 400
        let onLinux = RuntimeInformation.IsOSPlatform OSPlatform.Linux

        let persist (mutate: DlsiteClient.Core.Models.AppSettings -> unit) =
            svc.Settings.Update mutate

        let applyPaths () =
            persist (fun s ->
                s.LibraryDirectory <- lib.Current
                s.CacheDirectory <- cache.Current)

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

        ScrollViewer.create [
            ScrollViewer.content (
                StackPanel.create [
                    StackPanel.margin 16.0
                    StackPanel.spacing 10.0
                    StackPanel.children [
                        TextBlock.create [
                            TextBlock.text (t "settings.account")
                            TextBlock.fontSize 18.0
                            TextBlock.fontWeight FontWeight.Bold
                        ]
                        TextBlock.create [
                            TextBlock.text (
                                if svc.Auth.IsLoggedIn then
                                    let id = svc.Auth.LoginId
                                    if isNull id || id = "" then "SID OK" else id
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
                                                    busy.Set true
                                                    status.Set (t "auth.syncing")
                                                    runPoolAsyncUnit
                                                        (fun () -> svc.Library.SyncPurchasesAsync())
                                                        (fun () ->
                                                            busy.Set false
                                                            status.Set (t "auth.synced"))
                                                        (fun ex ->
                                                            busy.Set false
                                                            status.Set ex.Message)
                                                elif r = "cancelled" then
                                                    status.Set (t "auth.cancelled")
                                                else
                                                    status.Set r)
                                            (fun ex ->
                                                busy.Set false
                                                status.Set ex.Message))
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
                                                status.Set ex.Message))
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
                                                status.Set ex.Message))
                                ]
                            ]
                        ]
                        TextBlock.create [
                            TextBlock.text (t "auth.sidFallback")
                            TextBlock.opacity 0.7
                        ]
                        TextBox.create [
                            TextBox.watermark "__DLsite_SID"
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
                                            busy.Set false
                                            status.Set (t "auth.loginOk")
                                            busy.Set true
                                            status.Set (t "auth.syncing")
                                            runPoolAsyncUnit
                                                (fun () -> svc.Library.SyncPurchasesAsync())
                                                (fun () ->
                                                    busy.Set false
                                                    status.Set (t "auth.synced"))
                                                (fun ex ->
                                                    busy.Set false
                                                    status.Set ex.Message))
                                        (fun ex ->
                                            busy.Set false
                                            status.Set ex.Message))
                        ]

                        TextBlock.create [
                            TextBlock.text (t "settings.environment")
                            TextBlock.fontSize 18.0
                            TextBlock.fontWeight FontWeight.Bold
                        ]
                        TextBlock.create [ TextBlock.text (t "settings.libraryFolder") ]
                        TextBox.create [
                            TextBox.text lib.Current
                            TextBox.onTextChanged (fun s ->
                                lib.Set s
                                debounce applyPaths)
                        ]
                        TextBlock.create [ TextBlock.text (t "settings.cacheFolder") ]
                        TextBox.create [
                            TextBox.text cache.Current
                            TextBox.onTextChanged (fun s ->
                                cache.Set s
                                debounce applyPaths)
                        ]
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

                        TextBlock.create [ TextBlock.text (t "settings.language") ]
                        ComboBox.create [
                            ComboBox.dataItems [ "ja"; "zh-TW" ]
                            ComboBox.selectedItem locale.Current
                            ComboBox.onSelectedItemChanged (fun o ->
                                match o with
                                | :? string as s when s <> locale.Current -> applyLocale s
                                | _ -> ())
                        ]

                        TextBlock.create [
                            TextBlock.text (t "settings.download")
                            TextBlock.fontSize 18.0
                            TextBlock.fontWeight FontWeight.Bold
                        ]
                        TextBlock.create [ TextBlock.text (t "settings.maxDownloads") ]
                        TextBox.create [
                            TextBox.text maxDl.Current
                            TextBox.onTextChanged (fun s ->
                                maxDl.Set s
                                debounce applyDownload)
                        ]
                        TextBlock.create [ TextBlock.text (t "settings.maxExtracts") ]
                        TextBox.create [
                            TextBox.text maxEx.Current
                            TextBox.onTextChanged (fun s ->
                                maxEx.Set s
                                debounce applyDownload)
                        ]
                        TextBlock.create [ TextBlock.text (t "settings.rateLimit") ]
                        TextBox.create [
                            TextBox.text rate.Current
                            TextBox.onTextChanged (fun s ->
                                rate.Set s
                                debounce applyDownload)
                        ]
                        TextBlock.create [ TextBlock.text (t "settings.proxy") ]
                        ComboBox.create [
                            ComboBox.dataItems [ "off"; "system"; "custom" ]
                            ComboBox.selectedItem proxyMode.Current
                            ComboBox.onSelectedItemChanged (fun o ->
                                match o with
                                | :? string as s ->
                                    proxyMode.Set s
                                    applyDownload ()
                                | _ -> ())
                        ]
                        TextBox.create [
                            TextBox.watermark "http://host:port"
                            TextBox.text proxyUrl.Current
                            TextBox.onTextChanged (fun s ->
                                proxyUrl.Set s
                                debounce applyDownload)
                        ]

                        if onLinux then
                            TextBlock.create [
                                TextBlock.text (t "settings.linux")
                                TextBlock.fontSize 18.0
                                TextBlock.fontWeight FontWeight.Bold
                            ]
                            TextBlock.create [ TextBlock.text (t "settings.protonPath") ]
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
                            ]

                        TextBlock.create [
                            TextBlock.text (t "settings.logging")
                            TextBlock.fontSize 18.0
                            TextBlock.fontWeight FontWeight.Bold
                        ]
                        TextBlock.create [ TextBlock.text (t "settings.logLevel") ]
                        ComboBox.create [
                            ComboBox.dataItems
                                [ "Verbose"; "Debug"; "Information"; "Warning"; "Error"; "Fatal" ]
                            ComboBox.selectedItem logLevel.Current
                            ComboBox.onSelectedItemChanged (fun o ->
                                match o with
                                | :? string as s ->
                                    logLevel.Set s
                                    applyLog ()
                                | _ -> ())
                        ]
                        TextBlock.create [ TextBlock.text (t "settings.logPath") ]
                        TextBox.create [
                            TextBox.text logPath.Current
                            TextBox.onTextChanged (fun s ->
                                logPath.Set s
                                debounce applyLog)
                        ]
                        Button.create [
                            Button.content (t "settings.open")
                            Button.onClick (fun _ -> svc.Shell.OpenFolder logPath.Current)
                        ]

                        Button.create [
                            Button.content (t "settings.nestImportTitle")
                            Button.onClick (fun _ ->
                                svc.Settings.Update(fun s -> s.NestImportAsked <- false)
                                maybeAsk svc
                                let s = svc.Settings.Current
                                lib.Set s.LibraryDirectory
                                cache.Set s.CacheDirectory
                                status.Set (t "settings.nestImportTitle"))
                        ]

                        TextBlock.create [
                            TextBlock.text (
                                if busy.Current then t "settings.working" else status.Current)
                            TextBlock.opacity 0.8
                        ]
                    ]
                ])
        ])
