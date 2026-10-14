module DlsiteClient.App.Views.LibraryView

open System
open Avalonia.Controls
open Avalonia.Layout
open Avalonia.FuncUI
open Avalonia.FuncUI.DSL
open Avalonia.FuncUI.Types
open DlsiteClient.Core.Models
open DlsiteClient.App.Localization
open DlsiteClient.App.Services
open DlsiteClient.App.Fmt

let private orDash (s: string) =
    if String.IsNullOrEmpty s then "-" else s

let view (onOpenDetail: string -> unit) (onGoDownloads: unit -> unit) =
    Component(fun ctx ->
        let svc = get ()
        let search = ctx.useState ""
        let media = ctx.useState MediaFilter.All
        let favOnly = ctx.useState false
        let showHidden = ctx.useState false
        let dlOnly = ctx.useState false
        let selected = ctx.useState (Set.empty<string>)
        let tick = ctx.useState 0
        let mode = ctx.useState svc.Settings.Current.ViewMode

        let _ = tick.Current
        let works =
            svc.Library.Query(media.Current, search.Current, favOnly.Current, showHidden.Current, dlOnly.Current, null)
            |> Seq.toList

        let bump () = tick.Set(tick.Current + 1)

        let enqueue isUpdate =
            let set = selected.Current
            let items = works |> List.filter (fun w -> set.Contains w.Workno)
            if items.Length > 0 then
                svc.Downloads.Enqueue(items, isUpdate)
                onGoDownloads ()

        DockPanel.create [
            DockPanel.children [
                StackPanel.create [
                    DockPanel.dock Dock.Top
                    StackPanel.orientation Orientation.Vertical
                    StackPanel.spacing 8.0
                    StackPanel.margin 8.0
                    StackPanel.children [
                        TextBox.create [
                            TextBox.watermark (t "library.search")
                            TextBox.text search.Current
                            TextBox.onTextChanged (fun s -> search.Set s)
                        ]
                        StackPanel.create [
                            StackPanel.orientation Orientation.Horizontal
                            StackPanel.spacing 4.0
                            StackPanel.children (
                                [ MediaFilter.All, "library.filter.all"
                                  MediaFilter.Games, "library.filter.games"
                                  MediaFilter.Images, "library.filter.images"
                                  MediaFilter.Videos, "library.filter.videos"
                                  MediaFilter.Audio, "library.filter.audio"
                                  MediaFilter.Other, "library.filter.other" ]
                                |> List.map (fun (f0, key) ->
                                    Button.create [
                                        Button.content (t key)
                                        Button.onClick (fun _ -> media.Set f0)
                                    ] :> IView))
                        ]
                        StackPanel.create [
                            StackPanel.orientation Orientation.Horizontal
                            StackPanel.spacing 8.0
                            StackPanel.children [
                                CheckBox.create [
                                    CheckBox.content (t "library.filter.favorites")
                                    CheckBox.isChecked favOnly.Current
                                    CheckBox.onChecked (fun _ -> favOnly.Set true)
                                    CheckBox.onUnchecked (fun _ -> favOnly.Set false)
                                ]
                                CheckBox.create [
                                    CheckBox.content (t "library.filter.hidden")
                                    CheckBox.isChecked showHidden.Current
                                    CheckBox.onChecked (fun _ -> showHidden.Set true)
                                    CheckBox.onUnchecked (fun _ -> showHidden.Set false)
                                ]
                                CheckBox.create [
                                    CheckBox.content (t "library.filter.downloaded")
                                    CheckBox.isChecked dlOnly.Current
                                    CheckBox.onChecked (fun _ -> dlOnly.Set true)
                                    CheckBox.onUnchecked (fun _ -> dlOnly.Set false)
                                ]
                                Button.create [
                                    Button.content (t "library.mode.list")
                                    Button.onClick (fun _ ->
                                        mode.Set LibraryViewMode.List
                                        svc.Settings.Update(fun s -> s.ViewMode <- LibraryViewMode.List))
                                ]
                                Button.create [
                                    Button.content (t "library.mode.grid")
                                    Button.onClick (fun _ ->
                                        mode.Set LibraryViewMode.Grid
                                        svc.Settings.Update(fun s -> s.ViewMode <- LibraryViewMode.Grid))
                                ]
                                Button.create [
                                    Button.content (t "library.mode.hybrid")
                                    Button.onClick (fun _ ->
                                        mode.Set LibraryViewMode.Hybrid
                                        svc.Settings.Update(fun s -> s.ViewMode <- LibraryViewMode.Hybrid))
                                ]
                                Button.create [
                                    Button.content (t "library.action.download")
                                    Button.onClick (fun _ -> enqueue false)
                                ]
                                Button.create [
                                    Button.content (t "library.action.update")
                                    Button.onClick (fun _ -> enqueue true)
                                ]
                                Button.create [
                                    Button.content (t "library.action.uninstall")
                                    Button.onClick (fun _ ->
                                        for w in selected.Current do
                                            svc.Library.MarkUninstalled w
                                        selected.Set Set.empty
                                        bump ())
                                ]
                            ]
                        ]
                        TextBlock.create [
                            TextBlock.text (fmt2 "{0}: {1}" (t "library.count") works.Length)
                        ]
                    ]
                ]

                ScrollViewer.create [
                    ScrollViewer.content (
                        StackPanel.create [
                            StackPanel.children (
                                works
                                |> List.map (fun w ->
                                    let dlc =
                                        if w.IsDlc && svc.Library.DlcFeatureEnabled then
                                            " [" + t "library.dlcBadge" + "]"
                                        else ""
                                    let line =
                                        String.Join(
                                            " | ",
                                            [|
                                                orDash w.RegistDate
                                                w.WorkName + dlc
                                                w.MakerName
                                                w.WorkType
                                                w.RatingText
                                                sizeBytes w.ContentsFileSize
                                                (if w.IsDownloaded then "OK" else "-")
                                            |])
                                    Border.create [
                                        Border.padding 4.0
                                        Border.child (
                                            DockPanel.create [
                                                DockPanel.children [
                                                    CheckBox.create [
                                                        DockPanel.dock Dock.Left
                                                        CheckBox.isChecked (selected.Current.Contains w.Workno)
                                                        CheckBox.onChecked (fun _ ->
                                                            selected.Set(selected.Current.Add w.Workno))
                                                        CheckBox.onUnchecked (fun _ ->
                                                            selected.Set(selected.Current.Remove w.Workno))
                                                    ]
                                                    if w.HasUpdate then
                                                        Button.create [
                                                            DockPanel.dock Dock.Right
                                                            Button.content (t "library.action.update")
                                                            Button.onClick (fun _ ->
                                                                svc.Downloads.Enqueue([ w ], true)
                                                                onGoDownloads ())
                                                        ]
                                                    Button.create [
                                                        DockPanel.dock Dock.Right
                                                        Button.content "->"
                                                        Button.onClick (fun _ -> onOpenDetail w.Workno)
                                                    ]
                                                    TextBlock.create [
                                                        TextBlock.margin 4.0
                                                        TextBlock.text line
                                                    ]
                                                ]
                                            ])
                                        Border.contextMenu (
                                            ContextMenu.create [
                                                ContextMenu.viewItems [
                                                    MenuItem.create [
                                                        MenuItem.header (t "library.action.download")
                                                        MenuItem.onClick (fun _ ->
                                                            svc.Downloads.Enqueue([ w ], false)
                                                            onGoDownloads ())
                                                    ]
                                                    MenuItem.create [
                                                        MenuItem.header (t "library.action.favorite")
                                                        MenuItem.onClick (fun _ ->
                                                            svc.Library.SetFavorite(w.Workno, not w.IsFavorite)
                                                            bump ())
                                                    ]
                                                    MenuItem.create [
                                                        MenuItem.header (t "library.action.hide")
                                                        MenuItem.onClick (fun _ ->
                                                            svc.Library.SetHidden(w.Workno, not w.IsHidden)
                                                            bump ())
                                                    ]
                                                    MenuItem.create [
                                                        MenuItem.header (t "library.action.uninstall")
                                                        MenuItem.onClick (fun _ ->
                                                            svc.Library.MarkUninstalled w.Workno
                                                            bump ())
                                                    ]
                                                ]
                                            ])
                                    ]
                                    :> IView))
                        ])
                ]
            ]
        ])
