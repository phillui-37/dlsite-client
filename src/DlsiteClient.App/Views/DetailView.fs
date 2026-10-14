module DlsiteClient.App.Views.DetailView

open System
open Avalonia.Controls
open Avalonia.Layout
open Avalonia.Media
open Avalonia.FuncUI
open Avalonia.FuncUI.DSL
open Avalonia.FuncUI.Types
open DlsiteClient.Core.Services
open DlsiteClient.App.Localization
open DlsiteClient.App.Services
open DlsiteClient.App.Fmt
open DlsiteClient.App.ThumbCache

let private orDash (s: string) =
    if String.IsNullOrEmpty s then "-" else s

let view (workno: string) (onBack: unit -> unit) =
    Component(fun ctx ->
        let svc = get ()
        let work = svc.Library.Works |> Seq.tryFind (fun w -> w.Workno = workno)
        let exeChoice = ctx.useState (None: string list option)
        let selectedExe = ctx.useState ""
        let always = ctx.useState true
        let status = ctx.useState ""

        match work with
        | None ->
            StackPanel.create [
                StackPanel.children [
                    Button.create [ Button.content (t "common.back"); Button.onClick (fun _ -> onBack ()) ]
                    TextBlock.create [ TextBlock.text (t "detail.notFound") ]
                ]
            ]
        | Some w ->
            let tags = LibraryService.ParseTags(w.TagsJson) |> Seq.toList

            DockPanel.create [
                DockPanel.children [
                    Button.create [
                        DockPanel.dock Dock.Top
                        Button.content (t "common.back")
                        Button.margin 8.0
                        Button.onClick (fun _ -> onBack ())
                    ]
                    ScrollViewer.create [
                        ScrollViewer.content (
                            StackPanel.create [
                                StackPanel.margin 16.0
                                StackPanel.spacing 8.0
                                StackPanel.children [
                                    match tryGet (if isNull w.ThumbUrl then "" else w.ThumbUrl) with
                                    | Some bmp ->
                                        Image.create [
                                            Image.source bmp
                                            Image.width 240.0
                                            Image.height 240.0
                                            Image.stretch Stretch.Uniform
                                        ]
                                    | None ->
                                        Border.create [
                                            Border.width 240.0
                                            Border.height 240.0
                                            Border.background "#E5E7EB"
                                        ]
                                    TextBlock.create [
                                        TextBlock.text w.WorkName
                                        TextBlock.fontSize 24.0
                                        TextBlock.fontWeight FontWeight.Bold
                                    ]
                                    TextBlock.create [
                                        TextBlock.text (fmt2 "{0} : {1}" (t "library.col.maker") w.MakerName)
                                    ]
                                    TextBlock.create [
                                        TextBlock.text (fmt2 "{0} : {1}" (t "library.col.rating") w.RatingText)
                                    ]
                                    TextBlock.create [
                                        TextBlock.text (
                                            fmt2 "{0} : {1}" (t "library.col.purchaseDate") (orDash w.RegistDate))
                                    ]
                                    TextBlock.create [
                                        TextBlock.text (fmt2 "{0} : {1}" (t "library.col.workType") w.WorkType)
                                    ]
                                    if w.IsDlc && svc.Library.DlcFeatureEnabled then
                                        TextBlock.create [
                                            TextBlock.text (
                                                fmt2 "{0} {1}" (t "library.dlcBadge") (orDash w.ParentWorkno))
                                            TextBlock.foreground Brushes.DarkOrange
                                        ]
                                    TextBlock.create [
                                        TextBlock.text (t "detail.tags")
                                        TextBlock.fontWeight FontWeight.SemiBold
                                    ]
                                    WrapPanel.create [
                                        WrapPanel.children (
                                            tags
                                            |> List.map (fun tag ->
                                                Border.create [
                                                    Border.margin 2.0
                                                    Border.padding 4.0
                                                    Border.background "#E0F2FE"
                                                    Border.child (TextBlock.create [ TextBlock.text tag ])
                                                ]
                                                :> IView))
                                    ]
                                    CheckBox.create [
                                        CheckBox.content (t "detail.japanLocale")
                                        CheckBox.isChecked w.UseJapanLocale
                                        CheckBox.onIsCheckedChanged (fun e ->
                                            match e.Source with
                                            | :? CheckBox as cb ->
                                                svc.Library.SetJapanLocale(w.Workno, cb.IsChecked = Nullable true)
                                            | _ -> ())
                                    ]
                                    StackPanel.create [
                                        StackPanel.orientation Orientation.Horizontal
                                        StackPanel.spacing 8.0
                                        StackPanel.children [
                                            Button.create [
                                                Button.content (t "detail.download")
                                                Button.onClick (fun _ -> svc.Downloads.Enqueue([ w ], false))
                                            ]
                                            if w.HasUpdate then
                                                Button.create [
                                                    Button.content (t "library.action.update")
                                                    Button.onClick (fun _ ->
                                                        svc.Downloads.Enqueue([ w ], true))
                                                ]
                                            if w.IsDownloaded then
                                                Button.create [
                                                    Button.content (t "detail.play")
                                                    Button.onClick (fun _ ->
                                                        try
                                                            let d = svc.Launcher.PrepareLaunch w.Workno
                                                            if d.NeedsUserChoice then
                                                                let list = d.Candidates |> Seq.toList
                                                                exeChoice.Set(Some list)
                                                                selectedExe.Set(
                                                                    match list with
                                                                    | h :: _ -> h
                                                                    | [] -> "")
                                                            else
                                                                svc.Launcher.ConfirmAndLaunch(
                                                                    w.Workno, d.ExePath, false, d.UseJapanLocale)
                                                        with ex -> status.Set ex.Message)
                                                ]
                                            Button.create [
                                                Button.content (t "detail.openStore")
                                                Button.onClick (fun _ ->
                                                    let floor =
                                                        if String.IsNullOrEmpty w.SiteId then "maniax" else w.SiteId
                                                    let url =
                                                        fmt2
                                                            "https://www.dlsite.com/{0}/work/=/product_id/{1}.html"
                                                            floor
                                                            w.Workno
                                                    svc.Shell.OpenUrl url)
                                            ]
                                        ]
                                    ]
                                    match exeChoice.Current with
                                    | None -> TextBlock.create [ TextBlock.text "" ]
                                    | Some exes ->
                                        StackPanel.create [
                                            StackPanel.spacing 6.0
                                            StackPanel.children [
                                                TextBlock.create [ TextBlock.text (t "launcher.multiExe") ]
                                                ComboBox.create [
                                                    ComboBox.dataItems exes
                                                    ComboBox.selectedItem selectedExe.Current
                                                    ComboBox.onSelectedItemChanged (fun o ->
                                                        match o with
                                                        | :? string as s -> selectedExe.Set s
                                                        | _ -> ())
                                                ]
                                                CheckBox.create [
                                                    CheckBox.content (t "launcher.alwaysUse")
                                                    CheckBox.isChecked always.Current
                                                    CheckBox.onIsCheckedChanged (fun e ->
                                                        match e.Source with
                                                        | :? CheckBox as cb -> always.Set (cb.IsChecked = Nullable true)
                                                        | _ -> ())
                                                ]
                                                StackPanel.create [
                                                    StackPanel.orientation Orientation.Horizontal
                                                    StackPanel.spacing 8.0
                                                    StackPanel.children [
                                                        Button.create [
                                                            Button.content (t "launcher.launch")
                                                            Button.onClick (fun _ ->
                                                                svc.Launcher.ConfirmAndLaunch(
                                                                    w.Workno,
                                                                    selectedExe.Current,
                                                                    always.Current,
                                                                    w.UseJapanLocale)
                                                                exeChoice.Set None)
                                                        ]
                                                        Button.create [
                                                            Button.content (t "launcher.cancel")
                                                            Button.onClick (fun _ -> exeChoice.Set None)
                                                        ]
                                                    ]
                                                ]
                                            ]
                                        ]
                                    TextBlock.create [
                                        TextBlock.text status.Current
                                        TextBlock.foreground Brushes.Red
                                    ]
                                ]
                            ])
                    ]
                ]
            ])
