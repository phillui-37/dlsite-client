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

let private fileName (s: string) =
    if String.IsNullOrEmpty s then "" else s

let private confirmRedownload (svc: AppServices) (workno: string) (bump: unit -> unit) =
    if svc.Shell.Confirm(t "downloads.corruptArchive", t "downloads.redownload") then
        svc.Downloads.RequestRedownload workno
        bump ()

/// Localized label for an internal job state; falls back to the raw name.
let private jobStateLabel (state: DownloadJobState) =
    let key =
        match state with
        | DownloadJobState.Queued -> "downloads.state.queued"
        | DownloadJobState.Downloading -> "downloads.state.downloading"
        | DownloadJobState.Paused -> "downloads.state.paused"
        | DownloadJobState.Extracting -> "downloads.state.extracting"
        | DownloadJobState.Completed -> "downloads.state.completed"
        | DownloadJobState.Failed -> "downloads.state.failed"
        | DownloadJobState.CorruptArchive -> "downloads.state.corruptArchive"
        | _ -> ""
    let label = if key = "" then "" else t key
    if label = key then state.ToString() else label

let private jobRow
    (svc: AppServices)
    (j: DownloadJob)
    (section: string)
    (bump: unit -> unit)
    =
    let cached = svc.Downloads.HasCachedArchive j.Workno
    DockPanel.create [
        DockPanel.margin 6.0
        DockPanel.children [
            StackPanel.create [
                DockPanel.dock Dock.Right
                StackPanel.zIndex 1
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
                    | DownloadJobState.Extracting
                    | DownloadJobState.Queued
                    | _ ->
                        TextBlock.create [ TextBlock.text "" ]
                ]
            ]
            StackPanel.create [
                StackPanel.margin (0.0, 0.0, 8.0, 0.0)
                StackPanel.clipToBounds true
                StackPanel.spacing 3.0
                StackPanel.children [
                    TextBlock.create [
                        TextBlock.textTrimming TextTrimming.CharacterEllipsis
                        TextBlock.text j.WorkName
                        TextBlock.fontSize 14.0
                        TextBlock.fontWeight FontWeight.Medium
                    ]
                    ProgressBar.create [
                        ProgressBar.minimum 0.0
                        ProgressBar.maximum 100.0
                        ProgressBar.value j.ProgressPercent
                        ProgressBar.height 4.0
                    ]
                    TextBlock.create [
                        TextBlock.textTrimming TextTrimming.CharacterEllipsis
                        TextBlock.text (
                            let statusLabel =
                                match j.State with
                                | DownloadJobState.CorruptArchive -> t "downloads.state.corruptArchive"
                                | DownloadJobState.Failed when cached -> t "downloads.extractFailed"
                                | _ -> jobStateLabel j.State
                            fmt5 "{0:0}%  {1}  {2} / {3}  {4}"
                                j.ProgressPercent
                                (fileName j.CurrentFileName)
                                (sizePlain j.BytesDownloaded)
                                (sizePlain j.BytesTotal)
                                statusLabel)
                        TextBlock.foreground "#607087"
                        TextBlock.fontSize 12.0
                    ]
                    if not (String.IsNullOrEmpty j.ErrorMessage) then
                        TextBlock.create [
                            TextBlock.text j.ErrorMessage
                            TextBlock.textWrapping TextWrapping.Wrap
                            TextBlock.foreground "#B3261E"
                            TextBlock.fontSize 12.0
                        ]
                ]
            ]
        ]
    ]

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

        let emptyQueue =
            active.Count = 0 && upNext.Count = 0 && done'.Count = 0
        DockPanel.create [
            DockPanel.children [
                Border.create [
                    DockPanel.dock Dock.Top
                    Border.background "#FFFFFF"
                    Border.borderBrush "#DFE6EF"
                    Border.borderThickness (0.0, 0.0, 0.0, 1.0)
                    Border.padding 20.0
                    Border.child (
                        DockPanel.create [
                            DockPanel.children [
                                Button.create [
                                    DockPanel.dock Dock.Right
                                    Button.content (t "downloads.resumeAll")
                                    Button.verticalAlignment VerticalAlignment.Center
                                    Button.isEnabled (not resuming.Current &&
                                        ((upNext |> Seq.exists (fun j -> j.State = DownloadJobState.Paused)) ||
                                         (done' |> Seq.exists (fun j -> j.State = DownloadJobState.Failed))))
                                    Button.onClick (fun _ ->
                                        resuming.Set true
                                        run
                                            (fun () -> svc.Downloads.ResumeAll())
                                            (fun () -> resuming.Set false; bump ())
                                            (fun _ -> resuming.Set false; bump ()))
                                ]
                                StackPanel.create [
                                    StackPanel.spacing 2.0
                                    StackPanel.children [
                                        TextBlock.create [
                                            TextBlock.text (t "downloads.active")
                                            TextBlock.fontSize 22.0
                                            TextBlock.fontWeight FontWeight.Bold
                                        ]
                                        TextBlock.create [
                                            TextBlock.text (
                                                fmt4 "{0}  {1}  {2} {3}"
                                                    (sizeBps svc.Downloads.CurrentSpeedBps)
                                                    (sizeBps svc.Downloads.MaxSpeedBps)
                                                    (sizePlain svc.Downloads.SessionBytes) (t "downloads.total"))
                                            TextBlock.foreground "#607087"
                                            TextBlock.fontSize 12.0
                                        ]
                                        if svc.Downloads.IsPausedForAuth || svc.Auth.NeedsRelogin then
                                            TextBlock.create [
                                                TextBlock.text (t "auth.queuePaused")
                                                TextBlock.foreground "#B3261E"
                                                TextBlock.margin (0.0, 4.0, 0.0, 0.0)
                                            ]
                                    ]
                                ]
                            ]
                        ])
                ]
                ScrollViewer.create [
                    ScrollViewer.content (
                        StackPanel.create [
                            StackPanel.children (
                                [ TextBlock.create [
                                    TextBlock.margin (12.0, 16.0, 12.0, 4.0)
                                    TextBlock.text (fmt2 "{0} ({1})" (t "downloads.active") active.Count)
                                    TextBlock.fontWeight FontWeight.SemiBold
                                    TextBlock.fontSize 13.0
                                    TextBlock.foreground "#607087"
                                  ]
                                  :> IView ]
                                @ (active |> Seq.map (fun j -> jobRow svc j "active" bump :> IView) |> Seq.toList)
                                @ [ TextBlock.create [
                                      TextBlock.margin (12.0, 16.0, 12.0, 4.0)
                                      TextBlock.text (fmt2 "{0} ({1})" (t "downloads.upNext") upNext.Count)
                                      TextBlock.fontWeight FontWeight.SemiBold
                                      TextBlock.fontSize 13.0
                                      TextBlock.foreground "#607087"
                                    ]
                                    :> IView ]
                                @ (upNext |> Seq.map (fun j -> jobRow svc j "upNext" bump :> IView) |> Seq.toList)
                                @ [ DockPanel.create [
                                      DockPanel.margin (12.0, 16.0, 12.0, 4.0)
                                      DockPanel.children [
                                          Button.create [
                                              DockPanel.dock Dock.Right
                                              Button.classes [ "outline" ]
                                              Button.content (t "downloads.clearAll")
                                              Button.onClick (fun _ -> svc.Downloads.ClearCompleted(); bump ())
                                          ]
                                          TextBlock.create [
                                              TextBlock.text (fmt2 "{0} ({1})" (t "downloads.completed") done'.Count)
                                              TextBlock.fontWeight FontWeight.SemiBold
                                              TextBlock.fontSize 13.0
                                              TextBlock.foreground "#607087"
                                              TextBlock.verticalAlignment VerticalAlignment.Center
                                          ]
                                      ]
                                    ]
                                    :> IView ]
                                @ (done' |> Seq.map (fun j -> jobRow svc j "done" bump :> IView) |> Seq.toList)
                                @ (if emptyQueue then
                                       [ TextBlock.create [
                                           TextBlock.text (t "downloads.empty")
                                           TextBlock.margin (32.0, 48.0, 32.0, 0.0)
                                           TextBlock.horizontalAlignment HorizontalAlignment.Center
                                           TextBlock.foreground "#607087"
                                         ] :> IView ]
                                   else []))
                        ])
                ]
            ]
        ])
