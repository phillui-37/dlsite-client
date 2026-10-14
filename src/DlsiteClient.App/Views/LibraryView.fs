module DlsiteClient.App.Views.LibraryView

open System
open System.Runtime.InteropServices
open Avalonia.Controls
open Avalonia.Layout
open Avalonia.Media
open Avalonia.Threading
open Avalonia.FuncUI
open Avalonia.FuncUI.DSL
open Avalonia.FuncUI.Types
open DlsiteClient.Core.Models
open DlsiteClient.Core.Services
open DlsiteClient.App.Localization
open DlsiteClient.App.Services
open DlsiteClient.App.Fmt
open DlsiteClient.App.ThumbCache
open DlsiteClient.App.UiAsync

[<DllImport("user32.dll", CharSet = CharSet.Unicode)>]
extern int private MessageBoxW(nativeint hWnd, string text, string caption, uint32 typ)

let private MB_YESNO = 0x4u
let private MB_ICONQUESTION = 0x20u
let private IDYES = 6

let private orDash (s: string | null) =
    if String.IsNullOrEmpty s then "-" else s

let private pageSizeOptions = [| 20; 50; 100 |]

let private clampPageSize (n: int) =
    if n = 50 || n = 100 then n else 20

type private SortCol =
    | Purchase
    | Title
    | Maker
    | WorkType
    | Rating
    | Sales
    | Downloaded
    | Size
    | Update

let private allColKeys =
    [ "purchaseDate"; "title"; "maker"; "workType"; "rating"; "salesDate"; "downloaded"; "extractSize"; "update" ]

let private parseVisible (csv: string) =
    csv.Split([| ',' |], StringSplitOptions.RemoveEmptyEntries ||| StringSplitOptions.TrimEntries)
    |> Array.toList
    |> fun xs -> if xs.IsEmpty then allColKeys else xs

let private colVisible (vis: string list) (key: string) =
    vis |> List.exists (fun v -> String.Equals(v, key, StringComparison.OrdinalIgnoreCase))

let private sortKey (c: SortCol) (w: WorkItem) : IComparable =
    match c with
    | Purchase -> (orDash w.RegistDate) :> IComparable
    | Title -> w.WorkName :> IComparable
    | Maker -> w.MakerName :> IComparable
    | WorkType -> w.WorkType :> IComparable
    | Rating -> w.Point :> IComparable
    | Sales -> (orDash w.SalesDate) :> IComparable
    | Downloaded -> w.IsDownloaded :> IComparable
    | Size -> w.ContentsFileSize :> IComparable
    | Update -> w.HasUpdate :> IComparable

let private thumbUrl (w: WorkItem) =
    if String.IsNullOrWhiteSpace w.ThumbUrl then "" else w.ThumbUrl

let private workTypeLabel (svc: AppServices) (code: string) =
    let api = svc.Library.WorkTypeDisplay code
    if not (String.Equals(api, code, StringComparison.Ordinal)) then api
    else
        let key = "workType." + code
        let loc = t key
        if loc = key then code else loc

let private contextMenu
    (svc: AppServices)
    (w: WorkItem)
    (onGoDownloads: unit -> unit)
    (onOpenDetail: unit -> unit)
    (bump: unit -> unit)
    : IView<ContextMenu> =
    ContextMenu.create [
        ContextMenu.viewItems [
            if w.IsDownloaded then
                MenuItem.create [
                    MenuItem.header (t "detail.play")
                    MenuItem.onClick (fun _ ->
                        run
                            (fun () -> svc.Launcher.PrepareLaunch w.Workno)
                            (fun d ->
                                if d.NeedsUserChoice then onOpenDetail ()
                                else
                                    svc.Launcher.ConfirmAndLaunch(
                                        w.Workno, d.ExePath, false, d.UseJapanLocale))
                            (fun _ -> onOpenDetail ()))
                ]
            MenuItem.create [
                MenuItem.header (t "library.action.download")
                MenuItem.onClick (fun _ ->
                    svc.Downloads.Enqueue([ w ], false)
                    onGoDownloads ())
            ]
            MenuItem.create [
                MenuItem.header (t "library.action.install")
                MenuItem.onClick (fun _ ->
                    svc.Downloads.Enqueue([ w ], false)
                    onGoDownloads ())
            ]
            MenuItem.create [
                MenuItem.header (
                    if w.IsFavorite then t "library.action.unfavorite" else t "library.action.favorite")
                MenuItem.onClick (fun _ ->
                    svc.Library.SetFavorite(w.Workno, not w.IsFavorite)
                    bump ())
            ]
            MenuItem.create [
                MenuItem.header (
                    if w.IsHidden then t "library.action.unhide" else t "library.action.hide")
                MenuItem.onClick (fun _ ->
                    svc.Library.SetHidden(w.Workno, not w.IsHidden)
                    bump ())
            ]
            if w.IsDownloaded then
                MenuItem.create [
                    MenuItem.header (t "library.action.uninstall")
                    MenuItem.onClick (fun _ ->
                        runAsyncUnit
                            (fun () -> svc.Library.MarkUninstalledAsync w.Workno)
                            bump
                            (fun _ -> bump ()))
                ]
        ]
    ]

let private skeletonRow () : IView =
    Border.create [
        Border.margin (4.0, 2.0)
        Border.padding 6.0
        Border.background "#F3F4F6"
        Border.cornerRadius 4.0
        Border.child (
            StackPanel.create [
                StackPanel.orientation Orientation.Horizontal
                StackPanel.spacing 8.0
                StackPanel.children [
                    Border.create [ Border.width 40.0; Border.height 40.0; Border.background "#E5E7EB"; Border.cornerRadius 4.0 ]
                    Border.create [ Border.width 180.0; Border.height 14.0; Border.background "#E5E7EB"; Border.cornerRadius 3.0; Border.verticalAlignment VerticalAlignment.Center ]
                    Border.create [ Border.width 100.0; Border.height 14.0; Border.background "#E5E7EB"; Border.cornerRadius 3.0; Border.verticalAlignment VerticalAlignment.Center ]
                ]
            ])
    ]
    :> IView

let view (onOpenDetail: string -> unit) (onGoDownloads: unit -> unit) =
    Component(fun ctx ->
        let svc = get ()
        let search = ctx.useState ""
        let media = ctx.useState MediaFilter.All
        let favOnly = ctx.useState false
        let showHidden = ctx.useState false
        let dlOnly = ctx.useState false
        let tagFilter = ctx.useState ""
        let workTypes = ctx.useState (Set.empty<string>)
        let selected = ctx.useState (Set.empty<string>)
        let tick = ctx.useState 0
        let mode = ctx.useState svc.Settings.Current.ViewMode
        let sortCol = ctx.useState SortCol.Purchase
        let sortAsc = ctx.useState false
        let visible = ctx.useState (parseVisible svc.Settings.Current.VisibleColumns)
        let pageSize = ctx.useState (clampPageSize svc.Settings.Current.LibraryPageSize)
        let page = ctx.useState 0
        let expandTypes = ctx.useState false
        let expandCols = ctx.useState false
        let expandTags = ctx.useState false
        let bootstrapping = ctx.useState true

        ctx.useEffect (
            (fun () ->
                let bumpUi () =
                    Dispatcher.UIThread.Post(fun () -> tick.Set(tick.Current + 1))
                let libHandler = Action(bumpUi)
                svc.Library.add_Changed libHandler
                let thumbSub = subscribe (Action(bumpUi))
                runPoolAsyncUnit
                    (fun () -> svc.Library.EnsureWorkTypeNamesAsync())
                    (fun () ->
                        bootstrapping.Set false
                        bumpUi ())
                    (fun _ ->
                        bootstrapping.Set false
                        bumpUi ())
                { new IDisposable with
                    member _.Dispose() =
                        svc.Library.remove_Changed libHandler
                        thumbSub.Dispose() }),
            [ EffectTrigger.AfterInit ])

        let _ = tick.Current
        let bump () = tick.Set(tick.Current + 1)

        let tagArg = if String.IsNullOrWhiteSpace tagFilter.Current then null else tagFilter.Current
        let worksRaw =
            if workTypes.Current.IsEmpty then
                svc.Library.Query(
                    media.Current, search.Current, favOnly.Current, showHidden.Current, dlOnly.Current, tagArg, null)
            else
                let wt = ResizeArray(workTypes.Current) :> System.Collections.Generic.IReadOnlyCollection<string>
                svc.Library.Query(
                    media.Current, search.Current, favOnly.Current, showHidden.Current, dlOnly.Current, tagArg, wt)
            |> Seq.toList

        let works =
            let keyed = worksRaw |> List.map (fun w -> sortKey sortCol.Current w, w)
            let ordered =
                if sortAsc.Current then keyed |> List.sortBy fst
                else keyed |> List.sortByDescending fst
            ordered |> List.map snd

        let total = works.Length
        let size = pageSize.Current
        let pageCount = if total = 0 then 1 else (total + size - 1) / size
        let pageIdx = min page.Current (pageCount - 1) |> max 0
        let pageWorks =
            works
            |> List.skip (pageIdx * size)
            |> List.truncate size

        let setMode (m: LibraryViewMode) =
            mode.Set m
            svc.Settings.Update(fun s -> s.ViewMode <- m)

        let setPageSize (n: int) =
            let v = clampPageSize n
            pageSize.Set v
            page.Set 0
            svc.Settings.Update(fun s -> s.LibraryPageSize <- v)

        let toggleCol (key: string) =
            let next =
                if colVisible visible.Current key then
                    visible.Current |> List.filter (fun k -> not (String.Equals(k, key, StringComparison.OrdinalIgnoreCase)))
                else
                    visible.Current @ [ key ]
            let csv = String.Join(",", next)
            visible.Set next
            svc.Settings.Update(fun s -> s.VisibleColumns <- csv)

        let toggleSort (c: SortCol) =
            if sortCol.Current = c then sortAsc.Set(not sortAsc.Current)
            else
                sortCol.Set c
                sortAsc.Set true
            page.Set 0

        let enqueue isUpdate =
            let set = selected.Current
            let items = works |> List.filter (fun w -> set.Contains w.Workno)
            if items.Length > 0 then
                svc.Downloads.Enqueue(items, isUpdate)
                onGoDownloads ()

        let confirmUninstall () =
            if selected.Current.IsEmpty then ()
            else
                let r =
                    MessageBoxW(
                        0n,
                        t "library.confirmUninstall",
                        t "library.action.uninstall",
                        MB_YESNO ||| MB_ICONQUESTION)
                if r = IDYES then
                    let ids = selected.Current |> Set.toList
                    selected.Set Set.empty
                    runAsyncUnit
                        (fun () ->
                            task {
                                for id in ids do
                                    do! svc.Library.MarkUninstalledAsync id
                            })
                        bump
                        (fun _ -> bump ())

        let titleWithDlc (w: WorkItem) =
            if w.IsDlc && svc.Library.DlcFeatureEnabled then
                w.WorkName + " [" + t "library.dlcBadge" + "]"
            else w.WorkName

        let tagsAvailable = svc.Library.DistinctTags() |> Seq.toList
        let typesAvailable = svc.Library.DistinctWorkTypes() |> Seq.toList
        let vis = visible.Current

        let headerBtn (label: string) (col: SortCol) (key: string) : IView =
            if not (colVisible vis key) then
                TextBlock.create [ TextBlock.width 0.0 ] :> IView
            else
                let mark =
                    if sortCol.Current = col then
                        if sortAsc.Current then " ▲" else " ▼"
                    else ""
                Button.create [
                    Button.content (label + mark)
                    Button.padding (6.0, 2.0)
                    Button.onClick (fun _ -> toggleSort col)
                ]
                :> IView

        let rowSelect (w: WorkItem) : IView =
            CheckBox.create [
                CheckBox.isChecked (selected.Current.Contains w.Workno)
                CheckBox.onIsCheckedChanged (fun e ->
                    match e.Source with
                    | :? CheckBox as cb when cb.IsChecked = Nullable true ->
                        selected.Set(selected.Current.Add w.Workno)
                    | :? CheckBox -> selected.Set(selected.Current.Remove w.Workno)
                    | _ -> ())
            ]
            :> IView

        let updateBtn (w: WorkItem) : IView list =
            if w.HasUpdate && colVisible vis "update" then
                [ Button.create [
                      Button.content (t "library.action.update")
                      Button.onClick (fun _ ->
                          svc.Downloads.Enqueue([ w ], true)
                          onGoDownloads ())
                  ]
                  :> IView ]
            else []

        let listRow (w: WorkItem) : IView =
            let cells: IView list =
                [ rowSelect w
                  if colVisible vis "purchaseDate" then
                      TextBlock.create [ TextBlock.text (orDash w.RegistDate); TextBlock.width 90.0; TextBlock.margin 4.0 ] :> IView
                  Border.create [
                      Border.child (thumbView (thumbUrl w) 40.0 40.0)
                      Border.margin 2.0
                  ]
                  :> IView
                  TextBlock.create [
                      TextBlock.text (titleWithDlc w)
                      TextBlock.width 220.0
                      TextBlock.textTrimming TextTrimming.CharacterEllipsis
                      TextBlock.margin 4.0
                  ]
                  :> IView
                  if colVisible vis "maker" then
                      TextBlock.create [ TextBlock.text w.MakerName; TextBlock.width 120.0; TextBlock.margin 4.0 ] :> IView
                  if colVisible vis "workType" then
                      TextBlock.create [
                          TextBlock.text (workTypeLabel svc w.WorkType)
                          TextBlock.width 100.0
                          TextBlock.margin 4.0
                          TextBlock.textTrimming TextTrimming.CharacterEllipsis
                      ]
                      :> IView
                  if colVisible vis "rating" then
                      TextBlock.create [ TextBlock.text w.RatingText; TextBlock.width 48.0; TextBlock.margin 4.0 ] :> IView
                  if colVisible vis "salesDate" then
                      TextBlock.create [ TextBlock.text (orDash w.SalesDate); TextBlock.width 90.0; TextBlock.margin 4.0 ] :> IView
                  if colVisible vis "downloaded" then
                      TextBlock.create [
                          TextBlock.text (if w.IsDownloaded then "OK" else "-")
                          TextBlock.width 40.0
                          TextBlock.margin 4.0
                      ]
                      :> IView
                  if colVisible vis "extractSize" then
                      TextBlock.create [ TextBlock.text (sizeBytes w.ContentsFileSize); TextBlock.width 72.0; TextBlock.margin 4.0 ] :> IView ]
                @ updateBtn w

            Border.create [
                Border.padding 2.0
                Border.contextMenu (contextMenu svc w onGoDownloads (fun () -> onOpenDetail w.Workno) bump)
                Border.onPointerPressed (fun e ->
                    if e.ClickCount >= 2 && e.GetCurrentPoint(null).Properties.IsLeftButtonPressed then
                        onOpenDetail w.Workno)
                Border.child (
                    StackPanel.create [
                        StackPanel.orientation Orientation.Horizontal
                        StackPanel.spacing 2.0
                        StackPanel.children cells
                    ])
            ]
            :> IView

        let gridCard (w: WorkItem) : IView =
            Border.create [
                Border.width 150.0
                Border.margin 6.0
                Border.padding 6.0
                Border.background "#FAFAFA"
                Border.contextMenu (contextMenu svc w onGoDownloads (fun () -> onOpenDetail w.Workno) bump)
                Border.onPointerPressed (fun e ->
                    if e.ClickCount >= 2 && e.GetCurrentPoint(null).Properties.IsLeftButtonPressed then
                        onOpenDetail w.Workno)
                Border.child (
                    StackPanel.create [
                        StackPanel.spacing 4.0
                        StackPanel.children (
                            [ rowSelect w
                              thumbView (thumbUrl w) 130.0 130.0
                              TextBlock.create [
                                  TextBlock.text (titleWithDlc w)
                                  TextBlock.textWrapping TextWrapping.Wrap
                                  TextBlock.maxHeight 40.0
                              ]
                              :> IView
                              TextBlock.create [
                                  TextBlock.text (workTypeLabel svc w.WorkType)
                                  TextBlock.opacity 0.7
                                  TextBlock.fontSize 11.0
                              ]
                              :> IView ]
                            @ updateBtn w)
                    ])
            ]
            :> IView

        let hybridRow (w: WorkItem) : IView =
            Border.create [
                Border.padding 4.0
                Border.margin (0.0, 2.0)
                Border.contextMenu (contextMenu svc w onGoDownloads (fun () -> onOpenDetail w.Workno) bump)
                Border.onPointerPressed (fun e ->
                    if e.ClickCount >= 2 && e.GetCurrentPoint(null).Properties.IsLeftButtonPressed then
                        onOpenDetail w.Workno)
                Border.child (
                    DockPanel.create [
                        DockPanel.children (
                            [ CheckBox.create [
                                  DockPanel.dock Dock.Left
                                  CheckBox.isChecked (selected.Current.Contains w.Workno)
                                  CheckBox.margin 4.0
                                  CheckBox.onIsCheckedChanged (fun e ->
                                      match e.Source with
                                      | :? CheckBox as cb when cb.IsChecked = Nullable true ->
                                          selected.Set(selected.Current.Add w.Workno)
                                      | :? CheckBox -> selected.Set(selected.Current.Remove w.Workno)
                                      | _ -> ())
                              ]
                              :> IView
                              Border.create [
                                  DockPanel.dock Dock.Left
                                  Border.margin 4.0
                                  Border.child (thumbView (thumbUrl w) 72.0 72.0)
                              ]
                              :> IView ]
                            @ updateBtn w
                            @ [ StackPanel.create [
                                    StackPanel.margin 4.0
                                    StackPanel.spacing 2.0
                                    StackPanel.children [
                                        TextBlock.create [
                                            TextBlock.text (titleWithDlc w)
                                            TextBlock.fontWeight FontWeight.SemiBold
                                        ]
                                        TextBlock.create [
                                            TextBlock.text (
                                                String.Join(
                                                    " · ",
                                                    [|
                                                        w.MakerName
                                                        workTypeLabel svc w.WorkType
                                                        w.RatingText
                                                        orDash w.RegistDate
                                                        sizeBytes w.ContentsFileSize
                                                        (if w.IsDownloaded then "OK" else "-")
                                                    |]))
                                            TextBlock.opacity 0.75
                                        ]
                                    ]
                                ]
                                :> IView ])
                    ])
            ]
            :> IView

        let pager : IView =
            StackPanel.create [
                StackPanel.orientation Orientation.Horizontal
                StackPanel.spacing 8.0
                StackPanel.margin (8.0, 4.0)
                StackPanel.children [
                    TextBlock.create [
                        TextBlock.text (fmt1 (t "library.count") total)
                        TextBlock.verticalAlignment VerticalAlignment.Center
                    ]
                    TextBlock.create [
                        TextBlock.text (t "library.pageSize")
                        TextBlock.verticalAlignment VerticalAlignment.Center
                    ]
                    ComboBox.create [
                        ComboBox.width 72.0
                        ComboBox.dataItems pageSizeOptions
                        ComboBox.selectedItem pageSize.Current
                        ComboBox.onSelectedItemChanged (fun o ->
                            match o with
                            | :? int as n -> setPageSize n
                            | _ -> ())
                    ]
                    Button.create [
                        Button.content (t "library.prev")
                        Button.isEnabled (pageIdx > 0)
                        Button.onClick (fun _ -> page.Set(pageIdx - 1); bump ())
                    ]
                    TextBlock.create [
                        TextBlock.text (fmt2 (t "library.pageOf") (pageIdx + 1) pageCount)
                        TextBlock.verticalAlignment VerticalAlignment.Center
                    ]
                    Button.create [
                        Button.content (t "library.next")
                        Button.isEnabled (pageIdx + 1 < pageCount)
                        Button.onClick (fun _ -> page.Set(pageIdx + 1); bump ())
                    ]
                ]
            ]
            :> IView

        let body : IView =
            if bootstrapping.Current && total = 0 then
                StackPanel.create [
                    StackPanel.children ([ 1..5 ] |> List.map (fun _ -> skeletonRow ()))
                ]
                :> IView
            elif pageWorks.IsEmpty then
                TextBlock.create [
                    TextBlock.text (t "library.empty")
                    TextBlock.margin 24.0
                    TextBlock.opacity 0.7
                ]
                :> IView
            else
                match mode.Current with
                | LibraryViewMode.Grid ->
                    ScrollViewer.create [
                        ScrollViewer.content (
                            WrapPanel.create [
                                WrapPanel.children (pageWorks |> List.map gridCard)
                            ])
                    ]
                    :> IView
                | LibraryViewMode.Hybrid ->
                    ScrollViewer.create [
                        ScrollViewer.content (
                            StackPanel.create [
                                StackPanel.children (pageWorks |> List.map hybridRow)
                            ])
                    ]
                    :> IView
                | _ ->
                    DockPanel.create [
                        DockPanel.children [
                            StackPanel.create [
                                DockPanel.dock Dock.Top
                                StackPanel.orientation Orientation.Horizontal
                                StackPanel.spacing 2.0
                                StackPanel.margin 4.0
                                StackPanel.children [
                                    TextBlock.create [ TextBlock.width 28.0 ]
                                    headerBtn (t "library.col.purchaseDate") Purchase "purchaseDate"
                                    TextBlock.create [ TextBlock.width 44.0; TextBlock.text "" ]
                                    headerBtn (t "library.col.title") Title "title"
                                    headerBtn (t "library.col.maker") Maker "maker"
                                    headerBtn (t "library.col.workType") WorkType "workType"
                                    headerBtn (t "library.col.rating") Rating "rating"
                                    headerBtn (t "library.col.salesDate") Sales "salesDate"
                                    headerBtn (t "library.col.downloaded") Downloaded "downloaded"
                                    headerBtn (t "library.col.extractSize") Size "extractSize"
                                    headerBtn (t "library.col.update") Update "update"
                                ]
                            ]
                            ScrollViewer.create [
                                ScrollViewer.content (
                                    StackPanel.create [
                                        StackPanel.children (pageWorks |> List.map listRow)
                                    ])
                            ]
                        ]
                    ]
                    :> IView

        let toggleHeader (label: string) (open': bool) (setOpen: bool -> unit) : IView =
            Button.create [
                Button.content (if open' then "▾ " + label else "▸ " + label)
                Button.horizontalAlignment HorizontalAlignment.Left
                Button.onClick (fun _ -> setOpen (not open'))
            ]
            :> IView

        DockPanel.create [
            DockPanel.children [
                StackPanel.create [
                    DockPanel.dock Dock.Top
                    StackPanel.orientation Orientation.Vertical
                    StackPanel.spacing 6.0
                    StackPanel.margin 8.0
                    StackPanel.children [
                        TextBox.create [
                            TextBox.watermark (t "library.search")
                            TextBox.text search.Current
                            TextBox.onTextChanged (fun s -> search.Set s; page.Set 0)
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
                                        Button.onClick (fun _ -> media.Set f0; page.Set 0)
                                    ]
                                    :> IView))
                        ]
                        StackPanel.create [
                            StackPanel.orientation Orientation.Horizontal
                            StackPanel.spacing 8.0
                            StackPanel.children [
                                CheckBox.create [
                                    CheckBox.content (t "library.filter.favorites")
                                    CheckBox.isChecked favOnly.Current
                                    CheckBox.onIsCheckedChanged (fun e ->
                                        match e.Source with
                                        | :? CheckBox as cb -> favOnly.Set (cb.IsChecked = Nullable true); page.Set 0
                                        | _ -> ())
                                ]
                                CheckBox.create [
                                    CheckBox.content (t "library.filter.hidden")
                                    CheckBox.isChecked showHidden.Current
                                    CheckBox.onIsCheckedChanged (fun e ->
                                        match e.Source with
                                        | :? CheckBox as cb -> showHidden.Set (cb.IsChecked = Nullable true); page.Set 0
                                        | _ -> ())
                                ]
                                CheckBox.create [
                                    CheckBox.content (t "library.filter.downloaded")
                                    CheckBox.isChecked dlOnly.Current
                                    CheckBox.onIsCheckedChanged (fun e ->
                                        match e.Source with
                                        | :? CheckBox as cb -> dlOnly.Set (cb.IsChecked = Nullable true); page.Set 0
                                        | _ -> ())
                                ]
                                Button.create [
                                    Button.content (t "library.mode.list")
                                    Button.onClick (fun _ -> setMode LibraryViewMode.List)
                                ]
                                Button.create [
                                    Button.content (t "library.mode.grid")
                                    Button.onClick (fun _ -> setMode LibraryViewMode.Grid)
                                ]
                                Button.create [
                                    Button.content (t "library.mode.hybrid")
                                    Button.onClick (fun _ -> setMode LibraryViewMode.Hybrid)
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
                                    Button.onClick (fun _ -> confirmUninstall ())
                                ]
                            ]
                        ]
                        if not typesAvailable.IsEmpty then
                            toggleHeader (t "library.workTypes") expandTypes.Current expandTypes.Set
                        if expandTypes.Current && not typesAvailable.IsEmpty then
                            WrapPanel.create [
                                WrapPanel.children (
                                    typesAvailable
                                    |> List.map (fun wt ->
                                        CheckBox.create [
                                            CheckBox.content (workTypeLabel svc wt)
                                            CheckBox.margin 4.0
                                            CheckBox.isChecked (workTypes.Current.Contains wt)
                                            CheckBox.onIsCheckedChanged (fun e ->
                                                match e.Source with
                                                | :? CheckBox as cb when cb.IsChecked = Nullable true ->
                                                    workTypes.Set(workTypes.Current.Add wt); page.Set 0
                                                | :? CheckBox ->
                                                    workTypes.Set(workTypes.Current.Remove wt); page.Set 0
                                                | _ -> ())
                                        ]
                                        :> IView))
                            ]
                        if not tagsAvailable.IsEmpty then
                            toggleHeader (t "library.tagFilter") expandTags.Current expandTags.Set
                        if expandTags.Current && not tagsAvailable.IsEmpty then
                            ComboBox.create [
                                ComboBox.width 220.0
                                ComboBox.dataItems (("" :: tagsAvailable) |> List.toArray)
                                ComboBox.selectedItem tagFilter.Current
                                ComboBox.onSelectedItemChanged (fun o ->
                                    match o with
                                    | :? string as s -> tagFilter.Set s; page.Set 0
                                    | null -> tagFilter.Set ""; page.Set 0
                                    | _ -> ())
                            ]
                        toggleHeader (t "library.columns") expandCols.Current expandCols.Set
                        if expandCols.Current then
                            WrapPanel.create [
                                WrapPanel.children (
                                    [ "purchaseDate", "library.col.purchaseDate"
                                      "maker", "library.col.maker"
                                      "workType", "library.col.workType"
                                      "rating", "library.col.rating"
                                      "salesDate", "library.col.salesDate"
                                      "downloaded", "library.col.downloaded"
                                      "extractSize", "library.col.extractSize"
                                      "update", "library.col.update" ]
                                    |> List.map (fun (key, loc) ->
                                        CheckBox.create [
                                            CheckBox.content (t loc)
                                            CheckBox.margin 4.0
                                            CheckBox.isChecked (colVisible vis key)
                                            CheckBox.onIsCheckedChanged (fun e ->
                                                match e.Source with
                                                | :? CheckBox as cb ->
                                                    let want = cb.IsChecked = Nullable true
                                                    let have = colVisible visible.Current key
                                                    if want <> have then toggleCol key
                                                | _ -> ())
                                        ]
                                        :> IView))
                            ]
                        pager
                    ]
                ]
                ContentControl.create [ ContentControl.content body ]
            ]
        ])
