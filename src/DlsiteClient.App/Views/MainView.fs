module DlsiteClient.App.Views.MainView

open System
open Avalonia.Controls
open Avalonia.Input
open Avalonia.Layout
open Avalonia.Media
open Avalonia.FuncUI
open Avalonia.FuncUI.DSL
open Avalonia.FuncUI.Types
open DlsiteClient.App.Localization
open DlsiteClient.App.Palette
open DlsiteClient.App.OAuthLogin
open DlsiteClient.App.Services
open DlsiteClient.App.UiAsync
open DlsiteClient.App.Views

type Page =
    | Library
    | Downloads
    | Settings
    | Detail of string

let view () =
    Component(fun ctx ->
        let svc = get ()
        let initial =
            match pendingPlayWorkno with
            | Some w -> Page.Detail w
            | None -> Page.Library
        let page = ctx.useState initial
        let needsRelogin = ctx.useState svc.Auth.NeedsRelogin
        let reloginBusy = ctx.useState false

        ctx.useEffect (
            (fun () ->
                let onAuth () =
                    Avalonia.Threading.Dispatcher.UIThread.Post(fun () ->
                        needsRelogin.Set svc.Auth.NeedsRelogin)
                svc.Auth.add_Changed onAuth
                svc.Auth.add_SessionExpired onAuth
                { new IDisposable with
                    member _.Dispose() =
                        svc.Auth.remove_Changed onAuth
                        svc.Auth.remove_SessionExpired onAuth }),
            [ EffectTrigger.AfterInit ])

        let tab (target: Page) (key: string) : IView =
            let active =
                match page.Current, target with
                | Library, Library -> true
                | Downloads, Downloads -> true
                | Settings, Settings -> true
                | Detail _, Library -> true
                | _ -> false
            Button.create [
                Button.classes [ "nav" ]
                Button.background (if active then CharcoalSelected else Charcoal)
                Button.horizontalAlignment HorizontalAlignment.Stretch
                Button.horizontalContentAlignment HorizontalAlignment.Left
                Button.minHeight 44.0
                Button.padding (16.0, 10.0)
                Button.cornerRadius 3.0
                Button.foreground RailText
                Button.borderBrush (if active then RailText else Charcoal)
                Button.borderThickness (3.0, 0.0, 0.0, 0.0)
                Button.content (t key)
                Button.fontWeight (if active then FontWeight.SemiBold else FontWeight.Normal)
                Button.onClick (fun _ -> page.Set target)
            ]
            :> IView

        let nav : IView =
            Border.create [
                DockPanel.dock Dock.Left
                Border.width 176.0
                Border.background Charcoal
                Border.borderBrush CharcoalSelected
                Border.borderThickness (0.0, 0.0, 0.0, 1.0)
                Border.child (
                    StackPanel.create [
                        StackPanel.spacing 8.0
                        StackPanel.margin (12.0, 24.0)
                        StackPanel.children [
                            TextBlock.create [
                                TextBlock.text (t "app.title")
                                TextBlock.foreground Surface
                                TextBlock.fontSize 18.0
                                TextBlock.fontWeight FontWeight.Bold
                                TextBlock.margin (12.0, 0.0, 0.0, 24.0)
                            ]
                            tab Page.Library "nav.library"
                            tab Page.Downloads "nav.downloads"
                            tab Page.Settings "nav.settings"
                        ]
                    ])
            ]
            :> IView

        let sessionBanner : IView =
            Border.create [
                DockPanel.dock Dock.Top
                Border.background ClayContainer
                Border.borderBrush Clay
                Border.borderThickness (0.0, 0.0, 0.0, 1.0)
                Border.padding (16.0, 10.0)
                Border.child (
                    DockPanel.create [
                        DockPanel.children [
                            Button.create [
                                DockPanel.dock Dock.Right
                                Button.content (t "auth.relogin")
                                Button.isEnabled (not reloginBusy.Current)
                                Button.onClick (fun _ ->
                                    reloginBusy.Set true
                                    runAsync
                                        (fun () -> loginAsync svc)
                                        (fun r ->
                                            if r = "ok" then
                                                needsRelogin.Set false
                                                runPoolAsyncUnit
                                                    (fun () -> svc.Library.SyncPurchasesAsync())
                                                    (fun () -> reloginBusy.Set false)
                                                    (fun _ ->
                                                        reloginBusy.Set false
                                                        needsRelogin.Set svc.Auth.NeedsRelogin)
                                            else
                                                reloginBusy.Set false
                                                needsRelogin.Set svc.Auth.NeedsRelogin)
                                        (fun _ ->
                                            reloginBusy.Set false
                                            needsRelogin.Set svc.Auth.NeedsRelogin))
                            ]
                            StackPanel.create [
                                StackPanel.orientation Orientation.Vertical
                                StackPanel.spacing 2.0
                                StackPanel.verticalAlignment VerticalAlignment.Center
                                StackPanel.children [
                                    TextBlock.create [
                                        TextBlock.text (t "auth.sessionExpired")
                                        TextBlock.foreground Error
                                        TextBlock.fontWeight FontWeight.SemiBold
                                        TextBlock.textWrapping TextWrapping.Wrap
                                    ]
                                    TextBlock.create [
                                        TextBlock.text (t "auth.queuePaused")
                                        TextBlock.foreground ClayHover
                                        TextBlock.fontSize 12.0
                                        TextBlock.opacity 0.85
                                    ]
                                ]
                            ]
                        ]
                    ])
            ]
            :> IView

        let body : IView =
            ContentControl.create [
                ContentControl.content (
                    match page.Current with
                    | Library ->
                        LibraryView.view
                            (fun workno -> page.Set(Detail workno))
                            (fun () -> page.Set Downloads)
                        :> obj
                    | Downloads -> DownloadsView.view () :> obj
                    | Settings -> SettingsView.view () :> obj
                    | Detail workno -> DetailView.view workno (fun () -> page.Set Library) :> obj)
            ]
            :> IView

        let children : IView list =
            nav
            :: (if needsRelogin.Current then [ sessionBanner ] else [])
            @ [ body ]

        DockPanel.create [
            DockPanel.background Paper
            DockPanel.children children
        ])
