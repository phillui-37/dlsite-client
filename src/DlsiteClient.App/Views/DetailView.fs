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

let private colorOutline = "#DFE6EF"
let private colorOnSurfaceVariant = "#607087"
let private colorPrimary = "#1976D2"

let private orDash (s: string) =
    if String.IsNullOrEmpty s then "-" else s

// Work-type codes must render via i18n / Nest names, never raw-only (REQUIREMENTS Library).
let private workTypeLabel (svc: AppServices) (code: string) =
    let api = svc.Library.WorkTypeDisplay code
    if not (String.Equals(api, code, StringComparison.Ordinal)) then api
    else
        let key = "workType." + code
        let loc = t key
        if loc = key then code else loc

let private fileNameOrPath (p: string) =
    if String.IsNullOrEmpty p then ""
    else
        try Path.GetFileName p
        with _ -> p

let private card (children: IView list) : IView =
    Border.create [
        Border.cornerRadius 8.0
        Border.background "#FFFFFF"
        Border.borderBrush colorOutline
        Border.borderThickness 1.0
        Border.padding 16.0
        Border.child (
            StackPanel.create [
                StackPanel.spacing 10.0
                StackPanel.children children
            ])
    ]
    :> IView

let private cardTitle (key: string) : IView =
    TextBlock.create [
        TextBlock.text (t key)
        TextBlock.fontSize 16.0
        TextBlock.fontWeight FontWeight.SemiBold
        TextBlock.foreground "#142337"
    ]
    :> IView

let private metaRow (labelKey: string) (value: string) : IView =
    DockPanel.create [
        DockPanel.minHeight 28.0
        DockPanel.children [
            TextBlock.create [
                DockPanel.dock Dock.Left
                TextBlock.width 160.0
                TextBlock.text (t labelKey)
                TextBlock.foreground colorOnSurfaceVariant
                TextBlock.verticalAlignment VerticalAlignment.Center
            ]
            TextBlock.create [
                TextBlock.text value
                TextBlock.textWrapping TextWrapping.Wrap
                TextBlock.verticalAlignment VerticalAlignment.Center
            ]
        ]
    ]
    :> IView

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
                StackPanel.margin 16.0
                StackPanel.spacing 8.0
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

            let headerCard =
                card [
                    DockPanel.create [
                        DockPanel.children [
                            Border.create [
                                DockPanel.dock Dock.Left
                                Border.margin (0.0, 0.0, 16.0, 0.0)
                                Border.cornerRadius 8.0
                                Border.clipToBounds true
                                Border.child (thumbView (if isNull w.ThumbUrl then "" else w.ThumbUrl) 120.0 120.0)
                            ]
                            StackPanel.create [
                                StackPanel.spacing 6.0
                                StackPanel.verticalAlignment VerticalAlignment.Center
                                StackPanel.children [
                                    TextBlock.create [
                                        TextBlock.text w.WorkName
                                        TextBlock.fontSize 22.0
                                        TextBlock.fontWeight FontWeight.Bold
                                        TextBlock.textWrapping TextWrapping.Wrap
                                    ]
                                    TextBlock.create [
                                        TextBlock.text w.MakerName
                                        TextBlock.fontSize 14.0
                                        TextBlock.foreground colorOnSurfaceVariant
                                    ]
                                    StackPanel.create [
                                        StackPanel.orientation Orientation.Horizontal
                                        StackPanel.spacing 8.0
                                        StackPanel.children [
                                            Border.create [
                                                Border.cornerRadius 10.0
                                                Border.background "#E3F2FD"
                                                Border.padding (10.0, 3.0)
                                                Border.child (
                                                    TextBlock.create [
                                                        TextBlock.text (workTypeLabel svc w.WorkType)
                                                        TextBlock.fontSize 12.0
                                                        TextBlock.foreground colorPrimary
                                                    ])
                                            ]
                                            Border.create [
                                                Border.cornerRadius 10.0
                                                Border.background "#F1F4F9"
                                                Border.padding (10.0, 3.0)
                                                Border.child (
                                                    TextBlock.create [
                                                        TextBlock.text (w.RatingText)
                                                        TextBlock.fontSize 12.0
                                                        TextBlock.foreground colorOnSurfaceVariant
                                                    ])
                                            ]
                                        ]
                                    ]
                                ]
                            ]
                        ]
                    ]
                ]

            let actionButtons : IView list =
                [ if w.IsDownloaded then
                      Button.create [
                          Button.content (if launchState = GameLaunchState.Running then t "launcher.running" else t "detail.play")
                          Button.isEnabled (not starting && launchState <> GameLaunchState.Running)
                          Button.background colorPrimary
                          Button.foreground "#FFFFFF"
                          Button.fontWeight FontWeight.SemiBold
                          Button.minHeight 40.0
                          Button.cornerRadius 6.0
                          Button.onClick (fun _ ->
                              beginPlay w.UseJapanLocale
                              tick.Set(tick.Current + 1))
                      ]
                  Button.create [
                      Button.content (t "detail.download")
                      Button.minHeight 40.0
                      Button.onClick (fun _ -> svc.Downloads.Enqueue([ w ], false))
                  ]
                  if w.HasUpdate then
                      Button.create [
                          Button.content (t "library.action.update")
                          Button.minHeight 40.0
                          Button.onClick (fun _ ->
                              svc.Downloads.Enqueue([ w ], true))
                      ]
                  if w.IsDownloaded then
                      Button.create [
                          Button.content (t "launcher.changeExe")
                          Button.minHeight 40.0
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
                      Button.minHeight 40.0
                      Button.isEnabled (not (String.IsNullOrWhiteSpace w.InstallPath) && Directory.Exists w.InstallPath)
                      Button.onClick (fun _ ->
                          run
                              (fun () -> svc.Shell.OpenFolder w.InstallPath)
                              (fun () -> ())
                              (fun ex -> status.Set (errorText ex)))
                  ]
                  Button.create [
                      Button.content (t "detail.openStore")
                      Button.minHeight 40.0
                      Button.onClick (fun _ ->
                          let floor =
                              if String.IsNullOrEmpty w.SiteId then "maniax" else w.SiteId
                          let url =
                              fmt2
                                  "https://www.dlsite.com/{0}/work/=/product_id/{1}.html"
                                  floor
                                  w.Workno
                          svc.Shell.OpenUrl url)
                  ] ]

            let actionBar : IView =
                WrapPanel.create [
                    WrapPanel.orientation Orientation.Horizontal
                    WrapPanel.children [
                        for b in actionButtons -> b :> IView
                    ]
                ]
                :> IView

            let infoCard =
                card [
                    cardTitle "detail.info"
                    metaRow "library.col.maker" w.MakerName
                    metaRow "library.col.rating" w.RatingText
                    metaRow "library.col.purchaseDate" (orDash w.RegistDate)
                    metaRow "library.col.salesDate" (orDash w.SalesDate)
                    metaRow "library.col.workType" (workTypeLabel svc w.WorkType)
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
                    if tags.IsEmpty then
                        TextBlock.create [
                            TextBlock.text "-"
                            TextBlock.foreground colorOnSurfaceVariant
                        ]
                    else
                        WrapPanel.create [
                            WrapPanel.children (
                                tags
                                |> List.map (fun tag ->
                                    Border.create [
                                        Border.margin 2.0
                                        Border.padding (8.0, 3.0)
                                        Border.cornerRadius 10.0
                                        Border.background "#E0F2FE"
                                        Border.child (TextBlock.create [ TextBlock.text tag; TextBlock.fontSize 12.0 ])
                                    ]
                                    :> IView))
                        ]
                ]

            let launchCard =
                card [
                    cardTitle "detail.launchSettings"
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
                    if starting then
                        StackPanel.create [
                            StackPanel.orientation Orientation.Horizontal
                            StackPanel.spacing 8.0
                            StackPanel.children [
                                ProgressBar.create [ ProgressBar.classes [ "circular" ]; ProgressBar.width 24.0; ProgressBar.height 24.0; ProgressBar.isIndeterminate true ]
                                TextBlock.create [ TextBlock.text (t "launcher.starting"); TextBlock.verticalAlignment VerticalAlignment.Center ]
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
                                    ComboBox.width 360.0
                                    ComboBox.horizontalAlignment HorizontalAlignment.Left
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
                        TextBlock.textWrapping TextWrapping.Wrap
                    ]
                ]

            DockPanel.create [
                DockPanel.background "#F0F3F8"
                DockPanel.children [
                    Button.create [
                        DockPanel.dock Dock.Top
                        Button.classes [ "flat" ]
                        Button.horizontalAlignment HorizontalAlignment.Left
                        Button.margin (12.0, 8.0)
                        Button.content (t "common.back")
                        Button.onClick (fun _ -> onBack ())
                    ]
                    ScrollViewer.create [
                        ScrollViewer.content (
                            StackPanel.create [
                                StackPanel.margin (16.0, 8.0, 16.0, 16.0)
                                StackPanel.spacing 12.0
                                StackPanel.maxWidth 860.0
                                StackPanel.horizontalAlignment HorizontalAlignment.Left
                                StackPanel.children [
                                    headerCard
                                    actionBar
                                    infoCard
                                    launchCard
                                ]
                            ])
                    ]
                ]
            ])
