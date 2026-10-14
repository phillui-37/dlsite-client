module DlsiteClient.App.Views.DownloadsView

open System
open System.Runtime.InteropServices
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

[<DllImport("user32.dll", CharSet = CharSet.Unicode)>]
extern int private MessageBoxW(nativeint hWnd, string text, string caption, uint32 typ)

let private MB_YESNO = 0x4u
let private MB_ICONQUESTION = 0x20u
let private IDYES = 6

let private fileName (s: string) =
    if String.IsNullOrEmpty s then "" else s

let private confirmRedownload (svc: AppServices) (workno: string) (bump: unit -> unit) =
    let r =
        MessageBoxW(
            0n,
            t "downloads.corruptArchive",
            t "downloads.redownload",
            MB_YESNO ||| MB_ICONQUESTION)
    if r = IDYES then
        svc.Downloads.RequestRedownload workno
        bump ()

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
                StackPanel.children [
                    TextBlock.create [ TextBlock.text j.WorkName; TextBlock.fontSize 16.0 ]
                    ProgressBar.create [
                        ProgressBar.minimum 0.0
                        ProgressBar.maximum 100.0
                        ProgressBar.value j.ProgressPercent
                        ProgressBar.height 6.0
                    ]
                    TextBlock.create [
                        TextBlock.text (
                            let statusLabel =
                                match j.State with
                                | DownloadJobState.CorruptArchive -> t "downloads.corruptArchive"
                                | DownloadJobState.Failed when cached -> t "downloads.extractFailed"
                                | _ -> j.State.ToString()
                            let err =
                                if String.IsNullOrEmpty j.ErrorMessage then ""
                                else " - " + j.ErrorMessage
                            fmt6 "{0:0.00}%  {1}  {2} / {3}  [{4}]{5}"
                                j.ProgressPercent
                                (fileName j.CurrentFileName)
                                (sizePlain j.BytesDownloaded)
                                (sizePlain j.BytesTotal)
                                statusLabel
                                err)
                    ]
                ]
            ]
        ]
    ]

let view () =
    Component(fun ctx ->
        let svc = get ()
        let tick = ctx.useState 0
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

        DockPanel.create [
            DockPanel.children [
                Border.create [
                    DockPanel.dock Dock.Top
                    Border.background "#DBEAFE"
                    Border.padding 12.0
                    Border.child (
                        StackPanel.create [
                            StackPanel.children [
                                TextBlock.create [
                                    TextBlock.text (t "downloads.active")
                                    TextBlock.fontSize 22.0
                                    TextBlock.fontWeight FontWeight.Bold
                                ]
                                TextBlock.create [
                                    TextBlock.text (
                                        fmt6 "{0} {1}  {2} {3}  {4} {5}"
                                            (sizeBps svc.Downloads.CurrentSpeedBps) (t "downloads.speed")
                                            (sizeBps svc.Downloads.MaxSpeedBps) (t "downloads.maxSpeed")
                                            (sizePlain svc.Downloads.SessionBytes) (t "downloads.total"))
                                ]
                            ]
                        ])
                ]
                ScrollViewer.create [
                    ScrollViewer.content (
                        StackPanel.create [
                            StackPanel.children (
                                [ TextBlock.create [
                                    TextBlock.margin 8.0
                                    TextBlock.text (fmt2 "{0} ({1})" (t "downloads.active") active.Count)
                                    TextBlock.fontWeight FontWeight.SemiBold
                                  ]
                                  :> IView ]
                                @ (active |> Seq.map (fun j -> jobRow svc j "active" bump :> IView) |> Seq.toList)
                                @ [ TextBlock.create [
                                      TextBlock.margin 8.0
                                      TextBlock.text (fmt2 "{0} ({1})" (t "downloads.upNext") upNext.Count)
                                      TextBlock.fontWeight FontWeight.SemiBold
                                    ]
                                    :> IView ]
                                @ (upNext |> Seq.map (fun j -> jobRow svc j "upNext" bump :> IView) |> Seq.toList)
                                @ [ DockPanel.create [
                                      DockPanel.margin 8.0
                                      DockPanel.children [
                                          Button.create [
                                              DockPanel.dock Dock.Right
                                              Button.content (t "downloads.clearAll")
                                              Button.onClick (fun _ -> svc.Downloads.ClearCompleted(); bump ())
                                          ]
                                          TextBlock.create [
                                              TextBlock.text (fmt2 "{0} ({1})" (t "downloads.completed") done'.Count)
                                              TextBlock.fontWeight FontWeight.SemiBold
                                          ]
                                      ]
                                    ]
                                    :> IView ]
                                @ (done' |> Seq.map (fun j -> jobRow svc j "done" bump :> IView) |> Seq.toList))
                        ])
                ]
            ]
        ])
