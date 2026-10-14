module DlsiteClient.App.Views.MainView

open Avalonia
open Avalonia.Controls
open Avalonia.Input
open Avalonia.Layout
open Avalonia.Media
open Avalonia.FuncUI
open Avalonia.FuncUI.DSL
open Avalonia.FuncUI.Types
open DlsiteClient.App.Localization
open DlsiteClient.App.Services
open DlsiteClient.App.Views

type Page =
    | Library
    | Downloads
    | Settings
    | Detail of string

let view () =
    Component(fun ctx ->
        let initial =
            match pendingPlayWorkno with
            | Some w -> Page.Detail w
            | None -> Page.Library
        let page = ctx.useState initial

        let tab (target: Page) (key: string) : IView =
            let active =
                match page.Current, target with
                | Library, Library -> true
                | Downloads, Downloads -> true
                | Settings, Settings -> true
                | Detail _, Library -> true
                | _ -> false
            Border.create [
                Border.minWidth 120.0
                Border.height 48.0
                Border.padding (16.0, 0.0)
                Border.cursor (Cursor(StandardCursorType.Hand))
                Border.background "Transparent"
                Border.borderBrush (if active then "#1976D2" else "Transparent")
                Border.borderThickness (0.0, 0.0, 0.0, 3.0)
                Border.onTapped (fun _ -> page.Set target)
                Border.child (
                    TextBlock.create [
                        TextBlock.text (t key)
                        TextBlock.verticalAlignment VerticalAlignment.Center
                        TextBlock.horizontalAlignment HorizontalAlignment.Center
                        TextBlock.fontWeight (if active then FontWeight.SemiBold else FontWeight.Normal)
                        TextBlock.foreground (if active then "#1976D2" else "#5F6368")
                        TextBlock.fontSize 14.0
                    ])
            ]
            :> IView

        DockPanel.create [
            DockPanel.background "#FAFAFA"
            DockPanel.children [
                Border.create [
                    DockPanel.dock Dock.Top
                    Border.background "#FFFFFF"
                    Border.borderBrush "#E0E0E0"
                    Border.borderThickness (0.0, 0.0, 0.0, 1.0)
                    Border.child (
                        StackPanel.create [
                            StackPanel.orientation Orientation.Horizontal
                            StackPanel.spacing 4.0
                            StackPanel.margin (8.0, 0.0)
                            StackPanel.children [
                                tab Page.Library "nav.library"
                                tab Page.Downloads "nav.downloads"
                                tab Page.Settings "nav.settings"
                            ]
                        ])
                ]
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
            ]
        ])
