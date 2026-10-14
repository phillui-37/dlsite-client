module DlsiteClient.App.Views.LibraryView

open System
open Avalonia
open Avalonia.Controls
open Avalonia.Input
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

// Material-ish data-table column widths (header + row must match).
let private W_CHECK = 40.0
let private W_PURCHASE = 108.0
let private W_THUMB = 48.0
let private W_TITLE = 220.0
let private W_MAKER = 120.0
let private W_TYPE = 110.0
let private W_RATING = 52.0
let private W_SALES = 108.0
let private W_DL = 48.0
let private W_SIZE = 80.0
let private W_UPDATE = 88.0

let private colorSurface = "#FFFFFF"
let private colorSurfaceVariant = "#F5F5F5"
let private colorOnSurfaceVariant = "#5F6368"
let private colorOutline = "#E0E0E0"
let private colorPrimary = "#1976D2"
let private colorPrimaryContainer = "#E3F2FD"
let private colorRowHover = "#F8FBFF"
let private colorSelectedRow = "#E8F0FE"

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

let private cell (width: float) (child: IView) : IView =
    Border.create [
        Border.width width
        Border.minWidth width
        Border.maxWidth width
        Border.verticalAlignment VerticalAlignment.Center
        Border.padding (4.0, 0.0)
        Border.child child
    ]
    :> IView

let private cellText (width: float) (text: string) (secondary: bool) : IView =
    cell width (
        TextBlock.create [
            TextBlock.text text
            TextBlock.textTrimming TextTrimming.CharacterEllipsis
            TextBlock.fontSize (if secondary then 12.0 else 13.0)
            TextBlock.foreground (if secondary then colorOnSurfaceVariant else "#202124")
            TextBlock.verticalAlignment VerticalAlignment.Center
        ]
        :> IView)

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
        Border.margin (0.0, 1.0)
        Border.padding (8.0, 10.0)
        Border.background colorSurface
        Border.borderBrush colorOutline
        Border.borderThickness (0.0, 0.0, 0.0, 1.0)
        Border.child (
            StackPanel.create [
                StackPanel.orientation Orientation.Horizontal
                StackPanel.spacing 8.0
                StackPanel.children [
                    Border.create [ Border.width 40.0; Border.height 40.0; Border.background "#EEEEEE"; Border.cornerRadius 4.0 ]
                    Border.create [ Border.width 180.0; Border.height 12.0; Border.background "#EEEEEE"; Border.cornerRadius 2.0; Border.verticalAlignment VerticalAlignment.Center ]
                    Border.create [ Border.width 100.0; Border.height 12.0; Border.background "#EEEEEE"; Border.cornerRadius 2.0; Border.verticalAlignment VerticalAlignment.Center ]
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

        let selectedCount = selected.Current.Count
        let hasSelection = selectedCount >= 1
        let vis = visible.Current

        let setMode (m: LibraryViewMode) =
            mode.Set m
            svc.Settings.Update(fun s -> s.ViewMode <- m)
            bump ()

        let setPageSize (n: int) =
            let v = clampPageSize n
            pageSize.Set v
            page.Set 0
            svc.Settings.Update(fun s -> s.LibraryPageSize <- v)
            bump ()

        let toggleCol (key: string) =
            let next =
                if colVisible visible.Current key then
                    visible.Current |> List.filter (fun k -> not (String.Equals(k, key, StringComparison.OrdinalIgnoreCase)))
                else
                    visible.Current @ [ key ]
            let csv = String.Join(",", next)
            visible.Set next
            svc.Settings.Update(fun s -> s.VisibleColumns <- csv)
            bump ()

        let toggleSort (c: SortCol) =
            if sortCol.Current = c then sortAsc.Set(not sortAsc.Current)
            else
                sortCol.Set c
                sortAsc.Set true
            page.Set 0
            bump ()

        let enqueue isUpdate =
            let set = selected.Current
            let items = works |> List.filter (fun w -> set.Contains w.Workno)
            if items.Length > 0 then
                svc.Downloads.Enqueue(items, isUpdate)
                onGoDownloads ()

        let confirmUninstall () =
            if selected.Current.IsEmpty then ()
            elif svc.Shell.Confirm(t "library.confirmUninstall", t "library.action.uninstall") then
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

        let sortHeader (label: string) (col: SortCol) (key: string) (width: float) : IView =
            if key <> "title" && not (colVisible vis key) then
                Border.create [ Border.width 0.0; Border.height 0.0 ] :> IView
            else
                let active = sortCol.Current = col
                let mark =
                    if active then (if sortAsc.Current then " ▲" else " ▼") else ""
                cell width (
                    Button.create [
                        Button.content (label + mark)
                        Button.horizontalAlignment HorizontalAlignment.Stretch
                        Button.horizontalContentAlignment HorizontalAlignment.Left
                        Button.padding (4.0, 8.0)
                        Button.background "Transparent"
                        Button.foreground (if active then colorPrimary else colorOnSurfaceVariant)
                        Button.fontSize 12.0
                        Button.fontWeight (if active then FontWeight.SemiBold else FontWeight.Medium)
                        Button.cursor (Cursor(StandardCursorType.Hand))
                        Button.onClick (fun _ -> toggleSort col)
                    ]
                    :> IView)

        let rowSelect (w: WorkItem) : IView =
            CheckBox.create [
                CheckBox.isChecked (selected.Current.Contains w.Workno)
                CheckBox.horizontalAlignment HorizontalAlignment.Center
                CheckBox.onIsCheckedChanged (fun e ->
                    match e.Source with
                    | :? CheckBox as cb when cb.IsChecked = Nullable true ->
                        selected.Set(selected.Current.Add w.Workno); bump ()
                    | :? CheckBox ->
                        selected.Set(selected.Current.Remove w.Workno); bump ()
                    | _ -> ())
            ]
            :> IView

        let listHeader : IView =
            Border.create [
                Border.background colorSurfaceVariant
                Border.borderBrush colorOutline
                Border.borderThickness (0.0, 0.0, 0.0, 1.0)
                Border.padding (8.0, 0.0)
                Border.child (
                    StackPanel.create [
                        StackPanel.orientation Orientation.Horizontal
                        StackPanel.spacing 0.0
                        StackPanel.children [
                            cell W_CHECK (TextBlock.create [ TextBlock.text "" ] :> IView)
                            sortHeader (t "library.col.purchaseDate") Purchase "purchaseDate" W_PURCHASE
                            // Thumb sits under title header band (spacer + title).
                            cell W_THUMB (TextBlock.create [ TextBlock.text "" ] :> IView)
                            sortHeader (t "library.col.title") Title "title" W_TITLE
                            sortHeader (t "library.col.maker") Maker "maker" W_MAKER
                            sortHeader (t "library.col.workType") WorkType "workType" W_TYPE
                            sortHeader (t "library.col.rating") Rating "rating" W_RATING
                            sortHeader (t "library.col.salesDate") Sales "salesDate" W_SALES
                            sortHeader (t "library.col.downloaded") Downloaded "downloaded" W_DL
                            sortHeader (t "library.col.extractSize") Size "extractSize" W_SIZE
                            sortHeader (t "library.col.update") Update "update" W_UPDATE
                        ]
                    ])
            ]
            :> IView

        let listRow (w: WorkItem) (stripe: bool) : IView =
            let selectedRow = selected.Current.Contains w.Workno
            let bg =
                if selectedRow then colorSelectedRow
                elif stripe then colorSurfaceVariant
                else colorSurface
            let cells: IView list =
                [ cell W_CHECK (rowSelect w)
                  if colVisible vis "purchaseDate" then
                      cellText W_PURCHASE (orDash w.RegistDate) true
                  cell W_THUMB (thumbView (thumbUrl w) 40.0 40.0)
                  cellText W_TITLE (titleWithDlc w) false
                  if colVisible vis "maker" then
                      cellText W_MAKER w.MakerName true
                  if colVisible vis "workType" then
                      cellText W_TYPE (workTypeLabel svc w.WorkType) true
                  if colVisible vis "rating" then
                      cellText W_RATING w.RatingText true
                  if colVisible vis "salesDate" then
                      cellText W_SALES (orDash w.SalesDate) true
                  if colVisible vis "downloaded" then
                      cellText W_DL (if w.IsDownloaded then "OK" else "-") true
                  if colVisible vis "extractSize" then
                      cellText W_SIZE (sizeBytes w.ContentsFileSize) true
                  if colVisible vis "update" then
                      if w.HasUpdate then
                          cell W_UPDATE (
                              Button.create [
                                  Button.content (t "library.action.update")
                                  Button.padding (8.0, 2.0)
                                  Button.onClick (fun _ ->
                                      svc.Downloads.Enqueue([ w ], true)
                                      onGoDownloads ())
                              ]
                              :> IView)
                      else
                          cellText W_UPDATE "" true ]

            Border.create [
                Border.background bg
                Border.borderBrush colorOutline
                Border.borderThickness (0.0, 0.0, 0.0, 1.0)
                Border.padding (8.0, 6.0)
                Border.minHeight 56.0
                Border.contextMenu (contextMenu svc w onGoDownloads (fun () -> onOpenDetail w.Workno) bump)
                Border.onPointerPressed (fun e ->
                    if e.ClickCount >= 2 && e.GetCurrentPoint(null).Properties.IsLeftButtonPressed then
                        onOpenDetail w.Workno)
                Border.child (
                    StackPanel.create [
                        StackPanel.orientation Orientation.Horizontal
                        StackPanel.spacing 0.0
                        StackPanel.children cells
                    ])
            ]
            :> IView

        let gridCard (w: WorkItem) : IView =
            let selectedRow = selected.Current.Contains w.Workno
            Border.create [
                Border.width 156.0
                Border.margin 6.0
                Border.padding 8.0
                Border.cornerRadius 8.0
                Border.background (if selectedRow then colorSelectedRow else colorSurface)
                Border.borderBrush (if selectedRow then colorPrimary else colorOutline)
                Border.borderThickness 1.0
                Border.contextMenu (contextMenu svc w onGoDownloads (fun () -> onOpenDetail w.Workno) bump)
                Border.onPointerPressed (fun e ->
                    if e.ClickCount >= 2 && e.GetCurrentPoint(null).Properties.IsLeftButtonPressed then
                        onOpenDetail w.Workno)
                Border.child (
                    StackPanel.create [
                        StackPanel.spacing 6.0
                        StackPanel.children [
                            rowSelect w
                            thumbView (thumbUrl w) 140.0 140.0
                            TextBlock.create [
                                TextBlock.text (titleWithDlc w)
                                TextBlock.textWrapping TextWrapping.Wrap
                                TextBlock.maxHeight 40.0
                                TextBlock.fontSize 13.0
                                TextBlock.fontWeight FontWeight.Medium
                            ]
                            TextBlock.create [
                                TextBlock.text (workTypeLabel svc w.WorkType)
                                TextBlock.foreground colorOnSurfaceVariant
                                TextBlock.fontSize 11.0
                            ]
                        ]
                    ])
            ]
            :> IView

        let hybridRow (w: WorkItem) : IView =
            let selectedRow = selected.Current.Contains w.Workno
            Border.create [
                Border.padding 8.0
                Border.margin (0.0, 0.0, 0.0, 1.0)
                Border.background (if selectedRow then colorSelectedRow else colorSurface)
                Border.borderBrush colorOutline
                Border.borderThickness (0.0, 0.0, 0.0, 1.0)
                Border.contextMenu (contextMenu svc w onGoDownloads (fun () -> onOpenDetail w.Workno) bump)
                Border.onPointerPressed (fun e ->
                    if e.ClickCount >= 2 && e.GetCurrentPoint(null).Properties.IsLeftButtonPressed then
                        onOpenDetail w.Workno)
                Border.child (
                    DockPanel.create [
                        DockPanel.children [
                            Border.create [
                                DockPanel.dock Dock.Left
                                Border.margin (0.0, 0.0, 8.0, 0.0)
                                Border.verticalAlignment VerticalAlignment.Center
                                Border.child (rowSelect w)
                            ]
                            Border.create [
                                DockPanel.dock Dock.Left
                                Border.margin (0.0, 0.0, 12.0, 0.0)
                                Border.cornerRadius 4.0
                                Border.clipToBounds true
                                Border.child (thumbView (thumbUrl w) 72.0 72.0)
                            ]
                            StackPanel.create [
                                StackPanel.spacing 4.0
                                StackPanel.verticalAlignment VerticalAlignment.Center
                                StackPanel.children [
                                    TextBlock.create [
                                        TextBlock.text (titleWithDlc w)
                                        TextBlock.fontWeight FontWeight.Medium
                                        TextBlock.fontSize 14.0
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
                                        TextBlock.foreground colorOnSurfaceVariant
                                        TextBlock.fontSize 12.0
                                    ]
                                ]
                            ]
                        ]
                    ])
            ]
            :> IView

        let filterChip (label: string) (active: bool) (onClick: unit -> unit) : IView =
            Border.create [
                Border.cornerRadius 16.0
                Border.padding (12.0, 6.0)
                Border.margin (0.0, 0.0, 6.0, 0.0)
                Border.cursor (Cursor(StandardCursorType.Hand))
                Border.background (if active then colorPrimaryContainer else colorSurface)
                Border.borderBrush (if active then colorPrimary else colorOutline)
                Border.borderThickness 1.0
                Border.onTapped (fun _ -> onClick ())
                Border.child (
                    TextBlock.create [
                        TextBlock.text label
                        TextBlock.fontSize 13.0
                        TextBlock.foreground (if active then colorPrimary else "#202124")
                        TextBlock.fontWeight (if active then FontWeight.SemiBold else FontWeight.Normal)
                    ])
            ]
            :> IView

        /// Expand/collapse: read state at click time + bump (avoids stale closure / missed re-render).
        let disclosure (label: string) (isOpen: bool) (toggle: unit -> unit) (body: IView list) : IView list =
            let header =
                Border.create [
                    Border.cornerRadius 4.0
                    Border.padding (8.0, 8.0)
                    Border.margin (0.0, 2.0)
                    Border.cursor (Cursor(StandardCursorType.Hand))
                    Border.background (if isOpen then colorPrimaryContainer else "Transparent")
                    Border.onTapped (fun _ -> toggle ())
                    Border.child (
                        DockPanel.create [
                            DockPanel.children [
                                TextBlock.create [
                                    DockPanel.dock Dock.Right
                                    TextBlock.text (if isOpen then "▾" else "▸")
                                    TextBlock.foreground colorOnSurfaceVariant
                                    TextBlock.margin (8.0, 0.0, 0.0, 0.0)
                                ]
                                TextBlock.create [
                                    TextBlock.text label
                                    TextBlock.fontWeight FontWeight.Medium
                                    TextBlock.fontSize 13.0
                                    TextBlock.foreground "#202124"
                                ]
                            ]
                        ])
                ]
                :> IView
            if isOpen then header :: body else [ header ]

        let pager : IView =
            Border.create [
                Border.padding (4.0, 8.0)
                Border.child (
                    StackPanel.create [
                        StackPanel.orientation Orientation.Horizontal
                        StackPanel.spacing 10.0
                        StackPanel.children [
                            TextBlock.create [
                                TextBlock.text (fmt1 (t "library.count") total)
                                TextBlock.verticalAlignment VerticalAlignment.Center
                                TextBlock.foreground colorOnSurfaceVariant
                                TextBlock.fontSize 13.0
                            ]
                            TextBlock.create [
                                TextBlock.text (t "library.pageSize")
                                TextBlock.verticalAlignment VerticalAlignment.Center
                                TextBlock.foreground colorOnSurfaceVariant
                                TextBlock.fontSize 13.0
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
                                Button.classes [ "outline" ]
                                Button.isEnabled (pageIdx > 0)
                                Button.onClick (fun _ -> page.Set(pageIdx - 1); bump ())
                            ]
                            TextBlock.create [
                                TextBlock.text (fmt2 (t "library.pageOf") (pageIdx + 1) pageCount)
                                TextBlock.verticalAlignment VerticalAlignment.Center
                                TextBlock.fontSize 13.0
                            ]
                            Button.create [
                                Button.content (t "library.next")
                                Button.classes [ "outline" ]
                                Button.isEnabled (pageIdx + 1 < pageCount)
                                Button.onClick (fun _ -> page.Set(pageIdx + 1); bump ())
                            ]
                        ]
                    ])
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
                    TextBlock.margin 32.0
                    TextBlock.horizontalAlignment HorizontalAlignment.Center
                    TextBlock.foreground colorOnSurfaceVariant
                ]
                :> IView
            else
                match mode.Current with
                | LibraryViewMode.Grid ->
                    ScrollViewer.create [
                        ScrollViewer.horizontalScrollBarVisibility Primitives.ScrollBarVisibility.Disabled
                        ScrollViewer.content (
                            WrapPanel.create [
                                WrapPanel.margin 8.0
                                WrapPanel.children (pageWorks |> List.map gridCard)
                            ])
                    ]
                    :> IView
                | LibraryViewMode.Hybrid ->
                    ScrollViewer.create [
                        ScrollViewer.content (
                            StackPanel.create [
                                StackPanel.children (pageWorks |> List.mapi (fun _ w -> hybridRow w))
                            ])
                    ]
                    :> IView
                | _ ->
                    DockPanel.create [
                        DockPanel.children [
                            Border.create [
                                DockPanel.dock Dock.Top
                                Border.child listHeader
                            ]
                            ScrollViewer.create [
                                ScrollViewer.horizontalScrollBarVisibility Primitives.ScrollBarVisibility.Auto
                                ScrollViewer.content (
                                    StackPanel.create [
                                        StackPanel.children (
                                            pageWorks
                                            |> List.mapi (fun i w -> listRow w (i % 2 = 1)))
                                    ])
                            ]
                        ]
                    ]
                    :> IView

        let selectionBar : IView list =
            if not hasSelection then []
            else
                [ Border.create [
                      Border.background colorPrimaryContainer
                      Border.cornerRadius 8.0
                      Border.padding (12.0, 8.0)
                      Border.margin (0.0, 4.0, 0.0, 4.0)
                      Border.child (
                          DockPanel.create [
                              DockPanel.children [
                                  StackPanel.create [
                                      DockPanel.dock Dock.Right
                                      StackPanel.orientation Orientation.Horizontal
                                      StackPanel.spacing 8.0
                                      StackPanel.children [
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
                                  TextBlock.create [
                                      TextBlock.text (fmt1 (t "library.selectedCount") selectedCount)
                                      TextBlock.verticalAlignment VerticalAlignment.Center
                                      TextBlock.fontWeight FontWeight.Medium
                                      TextBlock.foreground colorPrimary
                                  ]
                              ]
                          ])
                  ]
                  :> IView ]

        let toolbarChildren : IView list =
            [ TextBox.create [
                  TextBox.watermark (t "library.search")
                  TextBox.text search.Current
                  TextBox.onTextChanged (fun s -> search.Set s; page.Set 0; bump ())
              ]
              :> IView
              StackPanel.create [
                  StackPanel.orientation Orientation.Horizontal
                  StackPanel.margin (0.0, 4.0, 0.0, 0.0)
                  StackPanel.children (
                      [ MediaFilter.All, "library.filter.all"
                        MediaFilter.Games, "library.filter.games"
                        MediaFilter.Images, "library.filter.images"
                        MediaFilter.Videos, "library.filter.videos"
                        MediaFilter.Audio, "library.filter.audio"
                        MediaFilter.Other, "library.filter.other" ]
                      |> List.map (fun (f0, key) ->
                          filterChip (t key) (media.Current = f0) (fun () ->
                              media.Set f0; page.Set 0; bump ())))
              ]
              :> IView
              StackPanel.create [
                  StackPanel.orientation Orientation.Horizontal
                  StackPanel.spacing 12.0
                  StackPanel.margin (0.0, 4.0, 0.0, 0.0)
                  StackPanel.children [
                      CheckBox.create [
                          CheckBox.content (t "library.filter.favorites")
                          CheckBox.isChecked favOnly.Current
                          CheckBox.onIsCheckedChanged (fun e ->
                              match e.Source with
                              | :? CheckBox as cb ->
                                  favOnly.Set (cb.IsChecked = Nullable true); page.Set 0; bump ()
                              | _ -> ())
                      ]
                      CheckBox.create [
                          CheckBox.content (t "library.filter.hidden")
                          CheckBox.isChecked showHidden.Current
                          CheckBox.onIsCheckedChanged (fun e ->
                              match e.Source with
                              | :? CheckBox as cb ->
                                  showHidden.Set (cb.IsChecked = Nullable true); page.Set 0; bump ()
                              | _ -> ())
                      ]
                      CheckBox.create [
                          CheckBox.content (t "library.filter.downloaded")
                          CheckBox.isChecked dlOnly.Current
                          CheckBox.onIsCheckedChanged (fun e ->
                              match e.Source with
                              | :? CheckBox as cb ->
                                  dlOnly.Set (cb.IsChecked = Nullable true); page.Set 0; bump ()
                              | _ -> ())
                      ]
                      filterChip (t "library.mode.list") (mode.Current = LibraryViewMode.List) (fun () ->
                          setMode LibraryViewMode.List)
                      filterChip (t "library.mode.grid") (mode.Current = LibraryViewMode.Grid) (fun () ->
                          setMode LibraryViewMode.Grid)
                      filterChip (t "library.mode.hybrid") (mode.Current = LibraryViewMode.Hybrid) (fun () ->
                          setMode LibraryViewMode.Hybrid)
                  ]
              ]
              :> IView ]
            @ selectionBar
            @ (if typesAvailable.IsEmpty then []
               else
                   disclosure
                       (t "library.workTypes")
                       expandTypes.Current
                       (fun () -> expandTypes.Set(not expandTypes.Current); bump ())
                       [ Border.create [
                             Border.background colorSurface
                             Border.borderBrush colorOutline
                             Border.borderThickness 1.0
                             Border.cornerRadius 8.0
                             Border.padding 8.0
                             Border.margin (0.0, 0.0, 0.0, 4.0)
                             Border.child (
                                 WrapPanel.create [
                                     WrapPanel.children (
                                         typesAvailable
                                         |> List.map (fun wt ->
                                             CheckBox.create [
                                                 CheckBox.content (workTypeLabel svc wt)
                                                 CheckBox.margin (4.0, 2.0)
                                                 CheckBox.isChecked (workTypes.Current.Contains wt)
                                                 CheckBox.onIsCheckedChanged (fun e ->
                                                     match e.Source with
                                                     | :? CheckBox as cb when cb.IsChecked = Nullable true ->
                                                         workTypes.Set(workTypes.Current.Add wt); page.Set 0; bump ()
                                                     | :? CheckBox ->
                                                         workTypes.Set(workTypes.Current.Remove wt); page.Set 0; bump ()
                                                     | _ -> ())
                                             ]
                                             :> IView))
                                 ])
                         ]
                         :> IView ])
            @ (if tagsAvailable.IsEmpty then []
               else
                   disclosure
                       (t "library.tagFilter")
                       expandTags.Current
                       (fun () -> expandTags.Set(not expandTags.Current); bump ())
                       [ ComboBox.create [
                             ComboBox.width 240.0
                             ComboBox.margin (0.0, 0.0, 0.0, 4.0)
                             ComboBox.dataItems (("" :: tagsAvailable) |> List.toArray)
                             ComboBox.selectedItem tagFilter.Current
                             ComboBox.onSelectedItemChanged (fun o ->
                                 match o with
                                 | :? string as s -> tagFilter.Set s; page.Set 0; bump ()
                                 | null -> tagFilter.Set ""; page.Set 0; bump ()
                                 | _ -> ())
                         ]
                         :> IView ])
            @ disclosure
                  (t "library.columns")
                  expandCols.Current
                  (fun () -> expandCols.Set(not expandCols.Current); bump ())
                  [ Border.create [
                        Border.background colorSurface
                        Border.borderBrush colorOutline
                        Border.borderThickness 1.0
                        Border.cornerRadius 8.0
                        Border.padding 8.0
                        Border.margin (0.0, 0.0, 0.0, 4.0)
                        Border.child (
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
                                            CheckBox.margin (4.0, 2.0)
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
                            ])
                    ]
                    :> IView ]
            @ [ pager ]

        DockPanel.create [
            DockPanel.background colorSurfaceVariant
            DockPanel.children [
                Border.create [
                    DockPanel.dock Dock.Top
                    Border.background colorSurface
                    Border.borderBrush colorOutline
                    Border.borderThickness (0.0, 0.0, 0.0, 1.0)
                    Border.padding 12.0
                    Border.child (
                        StackPanel.create [
                            StackPanel.spacing 4.0
                            StackPanel.children toolbarChildren
                        ])
                ]
                Border.create [
                    Border.margin 8.0
                    Border.background colorSurface
                    Border.cornerRadius 8.0
                    Border.borderBrush colorOutline
                    Border.borderThickness 1.0
                    Border.clipToBounds true
                    Border.child (ContentControl.create [ ContentControl.content body ])
                ]
            ]
        ])
