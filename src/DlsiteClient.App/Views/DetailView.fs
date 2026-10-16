module DlsiteClient.App.Views.DetailView

open System
open System.IO
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
open DlsiteClient.App.UiAsync

let private orDash (s: string) =
    if String.IsNullOrEmpty s then "-" else s

let private fileNameOrPath (p: string) =
    if String.IsNullOrEmpty p then ""
    else
        try Path.GetFileName p
        with _ -> p

let view (workno: string) (onBack: unit -> unit) =
    Component(fun ctx ->
        let svc = get ()
        let work = svc.Library.Works |> Seq.tryFind (fun w -> w.Workno = workno)
        let exeChoice = ctx.useState (None: string list option)
        let selectedExe = ctx.useState ""
        let always = ctx.useState true
        let status = ctx.useState ""
        let tick = ctx.useState 0

        let launch exePath always useJapan =
            run
                (fun () -> svc.Launcher.ConfirmAndLaunch(workno, exePath, always, useJapan))
                (fun () ->
                    exeChoice.Set None
                    tick.Set(tick.Current + 1))
                (fun ex -> status.Set (errorText ex))

        let beginPlay (useJapan: bool) =
            if svc.Launcher.TryBeginPreparation workno then
                run
                    (fun () -> svc.Launcher.PrepareLaunch workno)
                    (fun d ->
                        if d.NeedsUserChoice then
                            svc.Launcher.CancelPreparation workno
                            let list = d.Candidates |> Seq.toList
                            exeChoice.Set(Some list)
                            selectedExe.Set(match list with | h :: _ -> h | [] -> "")
                            tick.Set(tick.Current + 1)
                        else
                            launch d.ExePath false useJapan)
                    (fun ex -> svc.Launcher.CancelPreparation workno; status.Set (errorText ex))

        ctx.useEffect (
            (fun () ->
                let thumbSub =
                    subscribe (Action(fun () -> tick.Set(tick.Current + 1)))
                let changed = Action(fun () -> postUi (fun () -> tick.Set(tick.Current + 1)))
                svc.Library.add_Changed changed
                svc.Launcher.add_Changed changed
                runPoolAsyncUnit
                    (fun () -> svc.Library.EnsureTagsAsync workno)
                    (fun () -> tick.Set(tick.Current + 1))
                    (fun _ -> ())
                match pendingPlayWorkno with
                | Some w when w = workno ->
                    pendingPlayWorkno <- None
                    match svc.Library.Works |> Seq.tryFind (fun x -> x.Workno = workno) with
                    | Some ww when ww.IsDownloaded ->
                        beginPlay ww.UseJapanLocale
                        tick.Set(tick.Current + 1)
                    | _ -> status.Set (t "detail.notFound")
                | _ -> ()
                { new IDisposable with
                    member _.Dispose() =
                        thumbSub.Dispose()
                        svc.Library.remove_Changed changed
                        svc.Launcher.remove_Changed changed }),
            [ EffectTrigger.AfterInit ])

        let _ = tick.Current

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
            let launchState = svc.Launcher.GetLaunchState workno
            let starting = launchState = GameLaunchState.Preparing || launchState = GameLaunchState.Starting
            let hasSavedExe = not (String.IsNullOrEmpty w.SelectedExe)

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
                                    thumbView (if isNull w.ThumbUrl then "" else w.ThumbUrl) 240.0 240.0
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
                                                tick.Set(tick.Current + 1)
                                            | _ -> ())
                                    ]
                                    if hasSavedExe then
                                        TextBlock.create [
                                            TextBlock.text (
                                                fmt2 "{0} : {1}" (t "launcher.savedExe") (fileNameOrPath w.SelectedExe))
                                            TextBlock.opacity 0.8
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
                                                    Button.content (if launchState = GameLaunchState.Running then t "launcher.running" else t "detail.play")
                                                    Button.isEnabled (not starting && launchState <> GameLaunchState.Running)
                                                    Button.onClick (fun _ ->
                                                        beginPlay w.UseJapanLocale
                                                        tick.Set(tick.Current + 1))
                                                ]
                                                Button.create [
                                                    Button.content (t "launcher.changeExe")
                                                    Button.onClick (fun _ ->
                                                        svc.Library.ClearSelectedExe w.Workno
                                                        let install =
                                                            if isNull w.InstallPath then "" else w.InstallPath
                                                        run
                                                            (fun () ->
                                                                svc.Launcher.FindExecutables install |> Seq.toList)
                                                            (fun exes ->
                                                                if exes.IsEmpty then
                                                                    status.Set (t "launcher.noExe")
                                                                else
                                                                    exeChoice.Set(Some exes)
                                                                    selectedExe.Set(List.head exes)
                                                                    status.Set ""
                                                                tick.Set(tick.Current + 1))
                                                            (fun ex ->
                                                                status.Set (errorText ex)
                                                                tick.Set(tick.Current + 1)))
                                                ]
                                            Button.create [
                                                Button.content (t "detail.openFolder")
                                                Button.isEnabled (not (String.IsNullOrWhiteSpace w.InstallPath) && Directory.Exists w.InstallPath)
                                                Button.onClick (fun _ ->
                                                    run
                                                        (fun () -> svc.Shell.OpenFolder w.InstallPath)
                                                        (fun () -> ())
                                                        (fun ex -> status.Set (errorText ex)))
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
                                    if starting then
                                        StackPanel.create [
                                            StackPanel.orientation Orientation.Horizontal
                                            StackPanel.spacing 8.0
                                            StackPanel.children [
                                                ProgressBar.create [ ProgressBar.classes [ "circular" ]; ProgressBar.width 24.0; ProgressBar.height 24.0; ProgressBar.isIndeterminate true ]
                                                TextBlock.create [ TextBlock.text (t "launcher.starting") ]
                                            ]
                                        ]
                                    if launchState = GameLaunchState.Failed then
                                        TextBlock.create [ TextBlock.text (t (svc.Launcher.GetLaunchError workno)); TextBlock.foreground Brushes.Red ]
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
                                                                launch selectedExe.Current always.Current w.UseJapanLocale)
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
