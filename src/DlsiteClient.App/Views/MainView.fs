module DlsiteClient.App.Views.MainView

open Avalonia.Controls
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

        DockPanel.create [
            DockPanel.children [
                StackPanel.create [
                    DockPanel.dock Dock.Top
                    StackPanel.orientation Orientation.Horizontal
                    StackPanel.children (
                        [ Page.Library, "nav.library"
                          Page.Downloads, "nav.downloads"
                          Page.Settings, "nav.settings" ]
                        |> List.map (fun (p, key) ->
                            let active =
                                match page.Current, p with
                                | Library, Library -> true
                                | Downloads, Downloads -> true
                                | Settings, Settings -> true
                                | Detail _, Library -> true
                                | _ -> false
                            Button.create [
                                Button.content (t key)
                                Button.width 140.0
                                Button.height 36.0
                                Button.background (
                                    if active then "#2563EB" else "#F3F4F6")
                                Button.foreground (if active then "White" else "Black")
                                Button.onClick (fun _ -> page.Set p)
                            ]
                            :> IView))
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
