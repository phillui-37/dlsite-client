module DlsiteClient.App.Views.DownloadsView

open System
open Avalonia
open Avalonia.Controls
open Avalonia.Layout
open Avalonia.Media
open Avalonia.FuncUI
open Avalonia.FuncUI.DSL
open Avalonia.FuncUI.Types
open DlsiteClient.Core.Models
open DlsiteClient.App.Localization
open DlsiteClient.App.Services
open DlsiteClient.App.Fmt
open DlsiteClient.App.UiAsync
open DlsiteClient.App.Palette

let private colorOutline = Outline
let private colorOnSurfaceVariant = InkMuted

let private stateColor (s: DownloadJobState) =
    match s with
    | DownloadJobState.Downloading -> Clay
    | DownloadJobState.Paused -> Clay
    | DownloadJobState.Failed -> Error
    | DownloadJobState.CorruptArchive -> Error
    | DownloadJobState.Extracting -> Moss
    | DownloadJobState.Completed -> Moss
    | _ -> InkMuted

let private fileName (s: string) =
    if String.IsNullOrEmpty s then "" else s

let private confirmRedownload (svc: AppServices) (workno: string) (bump: unit -> unit) =
    if svc.Shell.Confirm(t "downloads.corruptArchive", t "downloads.redownload") then
        svc.Downloads.RequestRedownload workno
        bump ()

let private statusLabel (cached: bool) (j: DownloadJob) =
    match j.State with
    | DownloadJobState.CorruptArchive -> t "downloads.corruptArchive"
    | DownloadJobState.Failed when cached -> t "downloads.extractFailed"
    | DownloadJobState.Failed -> t "downloads.state.failed"
    | DownloadJobState.Extracting -> t "downloads.state.extracting"
    | DownloadJobState.Queued -> t "downloads.state.queued"
    | DownloadJobState.Downloading -> t "downloads.state.downloading"
    | DownloadJobState.Paused -> t "downloads.state.paused"
    | DownloadJobState.Completed -> t "downloads.state.completed"
    | other -> other.ToString()

let private jobRow
    (svc: AppServices)
    (j: DownloadJob)
    (section: string)
    (bump: unit -> unit)
    =
    let cached = svc.Downloads.HasCachedArchive j.Workno
    let stateCol = stateColor j.State
    Border.create [
        Border.margin (4.0, 2.0, 4.0, 6.0)
        Border.padding (10.0, 8.0)
        Border.cornerRadius 6.0
        Border.background Surface
        Border.borderBrush colorOutline
        Border.borderThickness (0.0, 0.0, 0.0, 1.0)
        Border.child (
            DockPanel.create [
                DockPanel.children [
                    StackPanel.create [
                        DockPanel.dock Dock.Right
                        StackPanel.verticalAlignment VerticalAlignment.Center
                        StackPanel.orientation Orientation.Horizontal
                        StackPanel.spacing 4.0
                        StackPanel.children [
                            if section = "upNext" then
                                Button.create [
                                    Button.content (t "downloads.moveUp")
                                    Button.onClick (fun _ -> svc.Downloads.MoveUpNext(j.Workno, -1); bump ())
                                ]
                                Button.create [
                                    Button.content (t "downloads.moveDown")
                                    Button.onClick (fun _ -> svc.Downloads.MoveUpNext(j.Workno, 1); bump ())
                                ]
                            match j.State with
                            | DownloadJobState.Downloading ->
                                Button.create [
                                    Button.content (t "downloads.pause")
                                    Button.onClick (fun _ -> svc.Downloads.Pause j.Workno; bump ())
                                ]
                            | DownloadJobState.Paused ->
                                Button.create [
                                    Button.content (t "downloads.resume")
                                    Button.onClick (fun _ -> svc.Downloads.ResumeJob j.Workno; bump ())
                                ]
                            | DownloadJobState.Failed ->
                                if cached then
                                    Button.create [
                                        Button.content (t "downloads.retryExtract")
                                        Button.onClick (fun _ ->
                                            svc.Downloads.RequestRetryExtract j.Workno
                                            bump ())
                                    ]
                                Button.create [
                                    Button.content (t "downloads.resume")
                                    Button.onClick (fun _ -> svc.Downloads.ResumeJob j.Workno; bump ())
                                ]
                            | DownloadJobState.CorruptArchive ->
                                Button.create [
                                    Button.content (t "downloads.redownload")
                                    Button.onClick (fun _ -> confirmRedownload svc j.Workno bump)
                                ]
                            | DownloadJobState.Completed ->
                                Button.create [
                                    Button.content (t "downloads.openLibrary")
                                    Button.onClick (fun _ ->
                                        svc.Shell.OpenFolder svc.Settings.Current.LibraryDirectory)
                                ]
                            | _ -> TextBlock.create [ TextBlock.text "" ]
                        ]
                    ]
                    StackPanel.create [
                        StackPanel.spacing 4.0
                        StackPanel.children [
                            StackPanel.create [
                                StackPanel.orientation Orientation.Horizontal
                                StackPanel.spacing 8.0
                                StackPanel.children [
                                    Border.create [
                                        Border.width 8.0
                                        Border.height 8.0
                                        Border.cornerRadius 4.0
                                        Border.background stateCol
                                        Border.verticalAlignment VerticalAlignment.Center
                                    ]
                                    TextBlock.create [
                                        TextBlock.text j.WorkName
                                        TextBlock.fontSize 14.0
                                        TextBlock.fontWeight FontWeight.Medium
                                        TextBlock.textTrimming TextTrimming.CharacterEllipsis
                                    ]
                                ]
                            ]
                            ProgressBar.create [
                                ProgressBar.minimum 0.0
                                ProgressBar.maximum 100.0
                                ProgressBar.value j.ProgressPercent
                                ProgressBar.height 6.0
                                ProgressBar.horizontalAlignment HorizontalAlignment.Stretch
                            ]
                            TextBlock.create [
                                TextBlock.textTrimming TextTrimming.CharacterEllipsis
                                TextBlock.fontSize 12.0
                                TextBlock.foreground colorOnSurfaceVariant
                                TextBlock.text (
                                    fmt5 "{0:0.0}%  {1}  {2} / {3}  [{4}]"
                                        j.ProgressPercent
                                        (fileName j.CurrentFileName)
                                        (sizePlain j.BytesDownloaded)
                                        (sizePlain j.BytesTotal)
                                        (statusLabel cached j))
                            ]
                            if not (String.IsNullOrEmpty j.ErrorMessage) then
                                TextBlock.create [
                                    TextBlock.text j.ErrorMessage
                                    TextBlock.fontSize 12.0
                                    TextBlock.foreground Error
                                    TextBlock.textWrapping TextWrapping.Wrap
                                ]
                        ]
                    ]
                ]
            ])
    ]
    :> IView

let private sectionCard (label: string) (count: int) (headingExtra: IView option) (rows: IView list) : IView =
    Border.create [
        Border.margin (0.0, 0.0, 0.0, 12.0)
        Border.padding 8.0
        Border.cornerRadius 8.0
        Border.background Surface
        Border.borderBrush colorOutline
        Border.borderThickness 1.0
        Border.child (
            StackPanel.create [
                StackPanel.spacing 4.0
                StackPanel.children [
                    DockPanel.create [
                        DockPanel.margin (6.0, 2.0, 6.0, 4.0)
                        DockPanel.children [
                            match headingExtra with
                            | Some extra ->
                                Border.create [
                                    DockPanel.dock Dock.Right
                                    Border.child extra
                                ] :> IView
                            | None -> TextBlock.create [ TextBlock.text "" ] :> IView
                            TextBlock.create [
                                TextBlock.text (fmt2 "{0} ({1})" label count)
                                TextBlock.fontSize 14.0
                                TextBlock.fontWeight FontWeight.SemiBold
                                TextBlock.verticalAlignment VerticalAlignment.Center
                            ]
                        ]
                    ]
                    if rows.IsEmpty then
                        TextBlock.create [
                            TextBlock.text (t "downloads.empty")
                            TextBlock.margin 10.0
                            TextBlock.fontSize 13.0
                            TextBlock.foreground colorOnSurfaceVariant
                        ]
                        :> IView
                    else
                        for r in rows do r
                ]
            ])
    ]
    :> IView

let view () =
    Component(fun ctx ->
        let svc = get ()
        let tick = ctx.useState 0
        let resuming = ctx.useState false
        ctx.useEffect (
            (fun () ->
                let handler = Action(fun () -> Avalonia.Threading.Dispatcher.UIThread.Post(fun () -> tick.Set(tick.Current + 1)))
                svc.Downloads.add_Changed handler
                { new IDisposable with
                    member _.Dispose() = svc.Downloads.remove_Changed handler }),
            [ EffectTrigger.AfterInit ])

        let _ = tick.Current
        let active = svc.Downloads.Active
        let upNext = svc.Downloads.UpNext
        let done' = svc.Downloads.Completed
        let bump () = tick.Set(tick.Current + 1)

        let resumeAll : IView =
            Button.create [
                Button.content (t "downloads.resumeAll")
                Button.minHeight 36.0
                Button.isEnabled (not resuming.Current &&
                    ((upNext |> Seq.exists (fun j -> j.State = DownloadJobState.Paused)) ||
                     (done' |> Seq.exists (fun j -> j.State = DownloadJobState.Failed))))
                Button.onClick (fun _ ->
                    resuming.Set true
                    run
                        (fun () -> svc.Downloads.ResumeAll())
                        (fun () -> resuming.Set false; bump ())
                        (fun _ -> resuming.Set false; bump ()))
            ] :> IView

        let heading : IView =
            Border.create [
                Border.background Surface
                Border.borderBrush colorOutline
                Border.borderThickness (0.0, 0.0, 0.0, 1.0)
                Border.padding (20.0, 16.0)
                Border.child (
                    DockPanel.create [
                        DockPanel.children [
                            Border.create [
                                DockPanel.dock Dock.Right
                                Border.verticalAlignment VerticalAlignment.Center
                                Border.child resumeAll
                            ]
                            StackPanel.create [
                                StackPanel.spacing 6.0
                                StackPanel.verticalAlignment VerticalAlignment.Center
                                StackPanel.children [
                                    TextBlock.create [
                                        TextBlock.text (t "nav.downloads")
                                        TextBlock.fontSize 20.0
                                        TextBlock.fontWeight FontWeight.SemiBold
                                        TextBlock.foreground Ink
                                    ]
                                    TextBlock.create [
                                        TextBlock.fontSize 13.0
                                        TextBlock.foreground colorOnSurfaceVariant
                                        TextBlock.text (
                                            fmt6 "{0} {1}  {2} {3}  {4} {5}"
                                                (sizeBps svc.Downloads.CurrentSpeedBps) (t "downloads.speed")
                                                (sizeBps svc.Downloads.MaxSpeedBps) (t "downloads.maxSpeed")
                                                (sizePlain svc.Downloads.SessionBytes) (t "downloads.total"))
                                    ]
                                    if svc.Downloads.IsPausedForAuth || svc.Auth.NeedsRelogin then
                                        Border.create [
                                            Border.horizontalAlignment HorizontalAlignment.Left
                                            Border.background ClayContainer
                                            Border.borderBrush Clay
                                            Border.borderThickness 1.0
                                            Border.cornerRadius 6.0
                                            Border.padding (10.0, 4.0)
                                            Border.child (
                                                TextBlock.create [
                                                    TextBlock.text (t "auth.queuePaused")
                                                    TextBlock.foreground ClayHover
                                                    TextBlock.fontSize 12.0
                                                ])
                                        ]
                                ]
                            ]
                        ]
                    ])
            ]
            :> IView

        let clearCompleted : IView =
            Button.create [
                Button.content (t "downloads.clearAll")
                Button.onClick (fun _ -> svc.Downloads.ClearCompleted(); bump ())
            ] :> IView

        let sections : IView list =
            [ sectionCard (t "downloads.active") active.Count None
                  (active |> Seq.map (fun j -> jobRow svc j "active" bump) |> Seq.toList)
              sectionCard (t "downloads.upNext") upNext.Count None
                  (upNext |> Seq.map (fun j -> jobRow svc j "upNext" bump) |> Seq.toList)
              sectionCard (t "downloads.completed") done'.Count (Some clearCompleted)
                  (done' |> Seq.map (fun j -> jobRow svc j "done" bump) |> Seq.toList) ]

        DockPanel.create [
            DockPanel.background Paper
            DockPanel.children [
                Border.create [
                    DockPanel.dock Dock.Top
                    Border.child heading
                ]
                ScrollViewer.create [
                    ScrollViewer.padding (20.0, 16.0, 20.0, 8.0)
                    ScrollViewer.content (
                        StackPanel.create [
                            StackPanel.spacing 4.0
                            StackPanel.children sections
                        ])
                ]
            ]
        ])
