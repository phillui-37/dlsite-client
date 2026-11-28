module DlsiteClient.App.Views.LibraryView

open System
open Avalonia
open Avalonia.Automation
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
open DlsiteClient.App.Palette
open DlsiteClient.App.UiAsync

// Data-table column widths (header + row must match).
let private W_CHECK = 40.0
let private W_PURCHASE = 108.0
let private W_THUMB = 48.0
let private W_TITLE = 220.0
let private W_MAKER = 120.0
let private W_TYPE = 110.0
let private W_RATING = 52.0
let private W_SALES = 108.0
let private W_DL = 100.0
let private W_SIZE = 80.0
let private W_UPDATE = 88.0

let private colorSurface = Surface
let private colorSurfaceVariant = SurfaceMuted
let private colorOnSurfaceVariant = InkMuted
let private colorOutline = Outline
let private colorPrimary = Clay
let private colorPrimaryDeep = ClayHover
let private colorPrimaryContainer = ClayContainer
let private colorRowHover = Hover
let private colorSelectedRow = MossContainer

let private orDash (s: string | null) =
    if String.IsNullOrEmpty s then "-" else s

let private pageSizeOptions = [| 20; 50; 100 |]

let private clampPageSize (n: int) =
    if n = 50 || n = 100 then n else 20

type SortCol =
    | Purchase
    | Title
    | Maker
    | WorkType
    | Rating
    | Sales
    | Downloaded
    | Size
    | Update

type LibraryViewState() =
    member val Search = "" with get, set
    member val Media = MediaFilter.All with get, set
    member val Favorites = false with get, set
    member val Hidden = false with get, set
    member val Downloaded = false with get, set
    member val Updates = false with get, set
    member val TagFilter = Set.empty<string> with get, set
    member val TagSearch = "" with get, set
    member val TagLimit = 60 with get, set
    member val MatchAllTags = true with get, set
    member val WorkTypes = Set.empty<string> with get, set
    member val Selected = Set.empty<string> with get, set
    member val SortCol = SortCol.Purchase with get, set
    member val SortAsc = false with get, set
    member val Page = 0 with get, set
    member val LastPageCount = 1 with get, set
    member val ExpandAdvanced = false with get, set
    member val ExpandTypes = false with get, set
    member val ExpandCols = false with get, set
    member val ExpandTags = false with get, set

let createState () = LibraryViewState()

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
            TextBlock.foreground (if secondary then colorOnSurfaceVariant else Ink)
            TextBlock.verticalAlignment VerticalAlignment.Center
        ]
        :> IView)

let private contextMenu
    (svc: AppServices)
    (w: WorkItem)
    (onGoDownloads: unit -> unit)
    (onOpenDetail: unit -> unit)
    (onUninstallFailed: unit -> unit)
    (clearStatus: unit -> unit)
    (bump: unit -> unit)
    : IView<ContextMenu> =
    ContextMenu.create [
        ContextMenu.viewItems [
            MenuItem.create [
                MenuItem.header (t "library.action.openDetail")
                MenuItem.onClick (fun _ -> onOpenDetail ())
            ]
            if w.IsDownloaded then
                MenuItem.create [
                    MenuItem.header (t "detail.play")
                    MenuItem.onClick (fun _ ->
                        if svc.Launcher.TryBeginPreparation w.Workno then
                            run
                                (fun () -> svc.Launcher.PrepareLaunch w.Workno)
                                (fun d ->
                                    if d.NeedsUserChoice then
                                        svc.Launcher.CancelPreparation w.Workno
                                        onOpenDetail ()
                                    else
                                        run
                                            (fun () -> svc.Launcher.ConfirmAndLaunch(w.Workno, d.ExePath, false, d.UseJapanLocale))
                                            (fun () -> ())
                                            (fun _ -> svc.Launcher.CancelPreparation w.Workno; onOpenDetail ()))
                                (fun _ -> svc.Launcher.CancelPreparation w.Workno; onOpenDetail ()))
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
                        if svc.Shell.Confirm(fmt1 (t "library.confirmUninstallOne") w.WorkName, t "library.action.uninstall") then
                            clearStatus ()
                            runAsync
                                (fun () -> svc.Library.MarkUninstalledAsync w.Workno)
                                (fun ok -> if not ok then onUninstallFailed (); bump ())
                                (fun _ -> onUninstallFailed (); bump ()))
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
                    Border.create [ Border.width 40.0; Border.height 40.0; Border.background SurfaceMuted; Border.cornerRadius 4.0 ]
                    Border.create [ Border.width 180.0; Border.height 12.0; Border.background SurfaceMuted; Border.cornerRadius 2.0; Border.verticalAlignment VerticalAlignment.Center ]
                    Border.create [ Border.width 100.0; Border.height 12.0; Border.background SurfaceMuted; Border.cornerRadius 2.0; Border.verticalAlignment VerticalAlignment.Center ]
                ]
            ])
    ]
    :> IView

let private viewCore (sharedState: LibraryViewState option) (onOpenDetail: string -> unit) (onGoDownloads: unit -> unit) =
    Component(fun ctx ->
        let ownedState = ctx.useState (createState ())
        let state = defaultArg sharedState ownedState.Current
        let svc = get ()
        let tagsLoading = ctx.useState false
        let tick = ctx.useState 0
        let bootstrapping = ctx.useState (svc.Library.Works.Count = 0)
        let status = ctx.useState ""
        let statusDetail = ctx.useState ""
        let syncing = ctx.useState false

        ctx.useEffect (
            (fun () ->
                let mutable pendingRefresh = 0
                let refreshTimer = DispatcherTimer()
                refreshTimer.Interval <- TimeSpan.FromMilliseconds 100.0
                refreshTimer.Tick.Add(fun _ ->
                    if System.Threading.Interlocked.Exchange(&pendingRefresh, 0) = 1 then
                        tick.Set(tick.Current + 1))
                refreshTimer.Start()
                let bumpUi () =
                    System.Threading.Interlocked.Exchange(&pendingRefresh, 1) |> ignore
                let libHandler = Action(bumpUi)
                svc.Library.add_Changed libHandler
                svc.Launcher.add_Changed libHandler
                let thumbSub = subscribe (Action(bumpUi))
                runPoolAsyncUnit
                    (fun () ->
                        task {
                            do! svc.Library.EnsureWorkTypeNamesAsync()
                            do! svc.Library.ReconcileLocalInstallsAsync()
                        })
                    (fun () ->
                        bootstrapping.Set false
                        bumpUi ())
                    (fun _ ->
                        bootstrapping.Set false
                        bumpUi ())
                { new IDisposable with
                    member _.Dispose() =
                        svc.Library.remove_Changed libHandler
                        svc.Launcher.remove_Changed libHandler
                        thumbSub.Dispose()
                        refreshTimer.Stop() }),
            [ EffectTrigger.AfterInit ])

        let _ = tick.Current
        let bump () = tick.Set(tick.Current + 1)
        let updateState (f: LibraryViewState -> unit) =
            f state
            bump ()

        let mode = svc.Settings.Current.ViewMode
        let vis = parseVisible svc.Settings.Current.VisibleColumns
        let size = clampPageSize svc.Settings.Current.LibraryPageSize

        let queryWorks () =
            let wt = ResizeArray(state.WorkTypes) :> System.Collections.Generic.IReadOnlyCollection<string>
            let tags = ResizeArray(state.TagFilter) :> System.Collections.Generic.IReadOnlyCollection<string>
            svc.Library.Query(
                state.Media, state.Search, state.Favorites, state.Hidden, state.Downloaded,
                null, wt, state.Updates, tags, state.MatchAllTags)
            |> Seq.toList

        let worksRaw = queryWorks ()

        let works =
            let keyed = worksRaw |> List.map (fun w -> sortKey state.SortCol w, w)
            let ordered =
                if state.SortAsc then keyed |> List.sortBy fst
                else keyed |> List.sortByDescending fst
            ordered |> List.map snd

        let updatableCount =
            works |> List.sumBy (fun w -> if w.HasUpdate then 1 else 0)

        let total = works.Length
        let pageCount = if total = 0 then 1 else (total + size - 1) / size
        let pageIdx = min state.Page (pageCount - 1) |> max 0
        state.LastPageCount <- pageCount
        if state.Page <> pageIdx then
            state.Page <- pageIdx
        let pageWorks =
            works
            |> List.skip (pageIdx * size)
            |> List.truncate size

        let selectedCount = state.Selected.Count
        let hasSelection = selectedCount >= 1

        let setMode (m: LibraryViewMode) =
            svc.Settings.Update(fun s -> s.ViewMode <- m)
            bump ()

        let setPageSize (n: int) =
            let v = clampPageSize n
            if v <> clampPageSize svc.Settings.Current.LibraryPageSize then
                svc.Settings.Update(fun s -> s.LibraryPageSize <- v)
                updateState (fun s -> s.Page <- 0)

        let toggleCol (key: string) =
            let currentVis = parseVisible svc.Settings.Current.VisibleColumns
            let next =
                if colVisible currentVis key then
                    currentVis |> List.filter (fun k -> not (String.Equals(k, key, StringComparison.OrdinalIgnoreCase)))
                else
                    currentVis @ [ key ]
            let csv = String.Join(",", next)
            svc.Settings.Update(fun s -> s.VisibleColumns <- csv)
            bump ()

        let toggleSort (c: SortCol) =
            updateState (fun s ->
                if s.SortCol = c then
                    s.SortAsc <- not s.SortAsc
                else
                    s.SortCol <- c
                    s.SortAsc <- true
                s.Page <- 0)

        let selectedWorks () =
            let set = state.Selected
            svc.Library.Works |> Seq.filter (fun w -> set.Contains w.Workno) |> Seq.toList

        let enqueue isUpdate =
            let items = selectedWorks ()
            if items.Length > 0 then
                svc.Downloads.Enqueue(items, isUpdate)
                onGoDownloads ()

        let confirmUninstall () =
            if state.Selected.IsEmpty then ()
            elif
                svc.Shell.Confirm(
                    fmt1 (t "library.confirmUninstallCount") state.Selected.Count,
                    t "library.action.uninstall") then
                let ids = state.Selected |> Set.toList
                state.Selected <- Set.empty
                status.Set ""
                statusDetail.Set ""
                bump ()
                runAsync
                    (fun () ->
                        task {
                            let mutable failed = 0
                            for id in ids do
                                let! ok = svc.Library.MarkUninstalledAsync id
                                if not ok then failed <- failed + 1
                            return failed
                        })
                    (fun failed ->
                        if failed > 0 then status.Set (t "library.uninstallFailed")
                        bump ())
                    (fun _ -> status.Set (t "library.uninstallFailed"); bump ())

        let titleWithDlc (w: WorkItem) =
            if w.IsDlc && svc.Library.DlcFeatureEnabled then
                w.WorkName + " [" + t "library.dlcBadge" + "]"
            else w.WorkName

        let tagsAvailable = svc.Library.DistinctTags() |> Seq.toList
        let matchingTags =
            tagsAvailable
            |> List.filter (fun tag -> tag.Contains(state.TagSearch.Trim(), StringComparison.OrdinalIgnoreCase))
        let shownTags = matchingTags |> List.truncate state.TagLimit
        let typesAvailable = svc.Library.DistinctWorkTypes() |> Seq.toList

        let sortHeader (label: string) (col: SortCol) (key: string) (width: float) : IView =
            if key <> "title" && not (colVisible vis key) then
                Border.create [ Border.width 0.0; Border.height 0.0 ] :> IView
            else
                let active = state.SortCol = col
                let mark =
                    if active then (if state.SortAsc then " ▲" else " ▼") else ""
                cell width (
                    Button.create [
                        Button.classes [ "table-heading" ]
                        Button.content (label + mark)
                        Button.horizontalAlignment HorizontalAlignment.Stretch
                        Button.horizontalContentAlignment HorizontalAlignment.Left
                        Button.padding (4.0, 8.0)
                        Button.background "Transparent"
                        Button.foreground (if active then colorPrimaryDeep else colorOnSurfaceVariant)
                        Button.fontSize 12.0
                        Button.fontWeight (if active then FontWeight.SemiBold else FontWeight.Medium)
                        Button.cursor (Cursor(StandardCursorType.Hand))
                        Button.onClick (fun _ -> toggleSort col)
                    ]
                    :> IView)

        let rowSelect (w: WorkItem) : IView =
            CheckBox.create [
                Avalonia.FuncUI.Builder.AttrBuilder<CheckBox>.CreateProperty<string>(AutomationProperties.NameProperty, t "library.action.selectWork" + ": " + titleWithDlc w, ValueNone)
                CheckBox.isChecked (state.Selected.Contains w.Workno)
                CheckBox.horizontalAlignment HorizontalAlignment.Center
                // Rendering IsChecked must not feed changes back into selection.
                CheckBox.onClick (fun _ ->
                    updateState (fun s ->
                        s.Selected <-
                            if s.Selected.Contains w.Workno then s.Selected.Remove w.Workno
                            else s.Selected.Add w.Workno))
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
            let selectedRow = state.Selected.Contains w.Workno
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
                      cellText W_TYPE (workTypeLabel (svc.Library.WorkTypeDisplay w.WorkType) w.WorkType) true
                  if colVisible vis "rating" then
                      cellText W_RATING w.RatingText true
                  if colVisible vis "salesDate" then
                      cellText W_SALES (orDash w.SalesDate) true
                  if colVisible vis "downloaded" then
                      cellText W_DL (if w.IsDownloaded then t "library.downloaded.yes" else t "library.downloaded.no") true
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
                          cell W_UPDATE (TextBlock.create [ TextBlock.text "" ] :> IView) ]

            Border.create [
                Border.background bg
                Border.borderBrush colorOutline
                Border.borderThickness (0.0, 0.0, 0.0, 1.0)
                Border.padding (8.0, 6.0)
                Border.minHeight 56.0
                Avalonia.FuncUI.Builder.AttrBuilder<Border>.CreateProperty<string>(AutomationProperties.NameProperty, titleWithDlc w + ", " + t "library.action.openDetail", ValueNone)
                Border.focusable true
                Border.classes [ "work" ]
                Border.cursor (Cursor(StandardCursorType.Hand))
                Border.contextMenu (contextMenu svc w onGoDownloads (fun () -> onOpenDetail w.Workno) (fun () -> status.Set (t "library.uninstallFailed")) (fun () -> status.Set ""; statusDetail.Set "") bump)
                Border.onKeyDown (fun e ->
                    if e.Source :? Border && (e.Key = Key.Enter || e.Key = Key.Space) then
                        onOpenDetail w.Workno
                        e.Handled <- true)
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
            |> View.withKey ("list:" + w.Workno)
            :> IView

        let updateAction (w: WorkItem) : IView =
            Button.create [
                Button.isVisible w.HasUpdate
                Button.content (t "library.action.update")
                Button.horizontalAlignment HorizontalAlignment.Left
                Button.onClick (fun _ ->
                    svc.Downloads.Enqueue([ w ], true)
                    onGoDownloads ())
            ] :> IView

        let gridCard (w: WorkItem) : IView =
            let selectedRow = state.Selected.Contains w.Workno
            Border.create [
                Border.width 156.0
                Border.margin 6.0
                Border.padding 8.0
                Border.cornerRadius 8.0
                Border.background (if selectedRow then colorSelectedRow else colorSurface)
                Border.borderBrush (if selectedRow then colorPrimary else colorOutline)
                Border.borderThickness 1.0
                Avalonia.FuncUI.Builder.AttrBuilder<Border>.CreateProperty<string>(AutomationProperties.NameProperty, titleWithDlc w + ", " + t "library.action.openDetail", ValueNone)
                Border.focusable true
                Border.classes [ "work" ]
                Border.cursor (Cursor(StandardCursorType.Hand))
                Border.contextMenu (contextMenu svc w onGoDownloads (fun () -> onOpenDetail w.Workno) (fun () -> status.Set (t "library.uninstallFailed")) (fun () -> status.Set ""; statusDetail.Set "") bump)
                Border.onKeyDown (fun e ->
                    if e.Source :? Border && (e.Key = Key.Enter || e.Key = Key.Space) then
                        onOpenDetail w.Workno
                        e.Handled <- true)
                Border.onPointerPressed (fun e ->
                    if e.ClickCount >= 2 && e.GetCurrentPoint(null).Properties.IsLeftButtonPressed then
                        onOpenDetail w.Workno)
                Border.child (
                    StackPanel.create [
                        StackPanel.spacing 6.0
                        StackPanel.children [
                            DockPanel.create [
                                DockPanel.children [
                                    Border.create [
                                        Border.horizontalAlignment HorizontalAlignment.Right
                                        Border.child (updateAction w)
                                    ]
                                    Border.create [ Border.child (rowSelect w) ]
                                ]
                            ]
                            thumbView (thumbUrl w) 140.0 140.0
                            TextBlock.create [
                                TextBlock.text (titleWithDlc w)
                                TextBlock.textWrapping TextWrapping.Wrap
                                TextBlock.maxHeight 40.0
                                TextBlock.fontSize 13.0
                                TextBlock.fontWeight FontWeight.Medium
                            ]
                            TextBlock.create [
                                TextBlock.text (workTypeLabel (svc.Library.WorkTypeDisplay w.WorkType) w.WorkType)
                                TextBlock.foreground colorOnSurfaceVariant
                                TextBlock.fontSize 11.0
                            ]
                        ]
                    ])
            ]
            |> View.withKey ("grid:" + w.Workno)
            :> IView

        let hybridRow (w: WorkItem) : IView =
            let selectedRow = state.Selected.Contains w.Workno
            Border.create [
                Border.padding 8.0
                Border.margin (0.0, 0.0, 0.0, 1.0)
                Border.background (if selectedRow then colorSelectedRow else colorSurface)
                Border.borderBrush colorOutline
                Border.borderThickness (0.0, 0.0, 0.0, 1.0)
                Avalonia.FuncUI.Builder.AttrBuilder<Border>.CreateProperty<string>(AutomationProperties.NameProperty, titleWithDlc w + ", " + t "library.action.openDetail", ValueNone)
                Border.focusable true
                Border.classes [ "work" ]
                Border.cursor (Cursor(StandardCursorType.Hand))
                Border.contextMenu (contextMenu svc w onGoDownloads (fun () -> onOpenDetail w.Workno) (fun () -> status.Set (t "library.uninstallFailed")) (fun () -> status.Set ""; statusDetail.Set "") bump)
                Border.onKeyDown (fun e ->
                    if e.Source :? Border && (e.Key = Key.Enter || e.Key = Key.Space) then
                        onOpenDetail w.Workno
                        e.Handled <- true)
                Border.onPointerPressed (fun e ->
                    if e.ClickCount >= 2 && e.GetCurrentPoint(null).Properties.IsLeftButtonPressed then
                        onOpenDetail w.Workno)
                Border.child (
                    DockPanel.create [
                        DockPanel.children [
                            Border.create [
                                DockPanel.dock Dock.Right
                                Border.verticalAlignment VerticalAlignment.Center
                                Border.child (updateAction w)
                            ]
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
                                                t "common.fieldSeparator",
                                                [|
                                                    w.MakerName
                                                    workTypeLabel (svc.Library.WorkTypeDisplay w.WorkType) w.WorkType
                                                    w.RatingText
                                                    orDash w.RegistDate
                                                    sizeBytes w.ContentsFileSize
                                                    (if w.IsDownloaded then t "library.downloaded.yes" else t "library.downloaded.no")
                                                |]))
                                        TextBlock.foreground colorOnSurfaceVariant
                                        TextBlock.fontSize 12.0
                                    ]
                                ]
                            ]
                        ]
                    ])
            ]
            |> View.withKey ("hybrid:" + w.Workno)
            :> IView

        let filterChipView (label: string) (active: bool) (onClick: unit -> unit) : IView<Button> =
            Button.create [

                Button.cornerRadius 8.0
                Button.padding (12.0, 6.0)
                Button.margin (0.0, 0.0, 6.0, 4.0)
                Button.minHeight 36.0
                Button.background (if active then colorPrimaryContainer else colorSurface)
                Button.foreground (if active then colorPrimaryDeep else Ink)
                Button.borderBrush (if active then colorPrimary else colorOutline)
                Button.borderThickness 1.0
                Button.content label
                Button.fontSize 13.0
                Button.fontWeight (if active then FontWeight.SemiBold else FontWeight.Normal)
                Button.onClick (fun _ -> onClick ())
            ]

        let filterChip (label: string) (active: bool) (onClick: unit -> unit) : IView =
            filterChipView label active onClick :> IView

        /// Expand/collapse: read state at click time + bump (avoids stale closure / missed re-render).
        let disclosure (label: string) (isOpen: bool) (toggle: unit -> unit) (body: IView list) : IView list =
            let header =
                Button.create [

                    Button.horizontalAlignment HorizontalAlignment.Stretch
                    Button.horizontalContentAlignment HorizontalAlignment.Left
                    Button.padding (8.0, 8.0)
                    Button.content ((if isOpen then "▾  " else "▸  ") + label)
                    Button.background (if isOpen then colorPrimaryContainer else "Transparent")
                    Button.onClick (fun _ -> toggle ())
                ] :> IView
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
                                ComboBox.selectedItem size
                                ComboBox.onSelectedItemChanged (fun o ->
                                    match o with
                                    | :? int as n when clampPageSize n <> clampPageSize svc.Settings.Current.LibraryPageSize -> setPageSize n
                                    | _ -> ())
                            ]
                            Button.create [
                                Button.content (t "library.prev")

                                Button.isEnabled (pageIdx > 0)
                                Button.onClick (fun _ ->
                                    let maxPage = max 0 (state.LastPageCount - 1)
                                    let cur = min state.Page maxPage |> max 0
                                    let target = max 0 (cur - 1)
                                    if target <> state.Page then
                                        updateState (fun s -> s.Page <- target))
                            ]
                            TextBlock.create [
                                TextBlock.text (fmt2 (t "library.pageOf") (pageIdx + 1) pageCount)
                                TextBlock.verticalAlignment VerticalAlignment.Center
                                TextBlock.fontSize 13.0
                            ]
                            Button.create [
                                Button.content (t "library.next")

                                Button.isEnabled (pageIdx + 1 < pageCount)
                                Button.onClick (fun _ ->
                                    let maxPage = max 0 (state.LastPageCount - 1)
                                    let cur = min state.Page maxPage |> max 0
                                    let target = min maxPage (cur + 1)
                                    if target <> state.Page then
                                        updateState (fun s -> s.Page <- target))
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
                let filtered =
                    not (String.IsNullOrWhiteSpace state.Search)
                    || not state.TagFilter.IsEmpty
                    || not state.WorkTypes.IsEmpty
                    || state.Favorites
                    || state.Hidden
                    || state.Downloaded
                    || state.Updates
                    || state.Media <> MediaFilter.All
                let resetFilters () =
                    updateState (fun s ->
                        s.Search <- ""
                        s.TagFilter <- Set.empty
                        s.WorkTypes <- Set.empty
                        s.Favorites <- false
                        s.Hidden <- false
                        s.Downloaded <- false
                        s.Updates <- false
                        s.Media <- MediaFilter.All
                        s.Page <- 0)
                StackPanel.create [
                    StackPanel.margin 32.0
                    StackPanel.horizontalAlignment HorizontalAlignment.Center
                    StackPanel.spacing 12.0
                    StackPanel.children [
                        TextBlock.create [
                            TextBlock.text (if filtered then t "library.empty.filtered" else t "library.empty")
                            TextBlock.horizontalAlignment HorizontalAlignment.Center
                            TextBlock.foreground colorOnSurfaceVariant
                        ]
                        if filtered then
                            Button.create [
                                Button.content (t "library.filters.reset")
                                Button.horizontalAlignment HorizontalAlignment.Center
                                Button.onClick (fun _ -> resetFilters ())
                            ]
                        elif svc.Auth.IsLoggedIn && svc.Library.Works.Count = 0 then
                            Button.create [

                                Button.content (if syncing.Current then t "auth.syncing" else t "auth.sync")
                                Button.horizontalAlignment HorizontalAlignment.Center
                                Button.isEnabled (not syncing.Current)
                                Button.onClick (fun _ ->
                                    syncing.Set true
                                    status.Set ""
                                    statusDetail.Set ""
                                    runPoolAsyncUnit
                                        (fun () -> svc.Library.SyncPurchasesAsync())
                                        (fun () -> syncing.Set false; bump ())
                                        (fun ex ->
                                            syncing.Set false
                                            status.Set (t "library.syncFailed")
                                            statusDetail.Set (errorText ex)
                                            bump ()))
                            ]
                    ]
                ]
                :> IView
            else
                match mode with
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
                    // Outer viewer scrolls horizontally only (header + rows move together); the
                    // inner viewer owns vertical scrolling so the header stays visible.
                    ScrollViewer.create [
                        ScrollViewer.horizontalScrollBarVisibility Primitives.ScrollBarVisibility.Auto
                        ScrollViewer.verticalScrollBarVisibility Primitives.ScrollBarVisibility.Disabled
                        ScrollViewer.content (
                            DockPanel.create [
                                DockPanel.children [
                                    Border.create [
                                        DockPanel.dock Dock.Top
                                        Border.child listHeader
                                    ]
                                    ScrollViewer.create [
                                        ScrollViewer.horizontalScrollBarVisibility Primitives.ScrollBarVisibility.Disabled
                                        ScrollViewer.maxHeight 560.0
                                        ScrollViewer.content (
                                            StackPanel.create [
                                                StackPanel.children (pageWorks |> List.mapi (fun i w -> listRow w (i % 2 = 1)))
                                            ])
                                    ]
                                ]
                            ])
                    ]
                    :> IView

        let bodyWithPager : IView =
            DockPanel.create [
                DockPanel.children [
                    Border.create [ DockPanel.dock Dock.Bottom; Border.margin (0.0, 6.0, 0.0, 0.0); Border.borderBrush colorOutline; Border.borderThickness (0.0, 1.0, 0.0, 0.0); Border.padding (0.0, 8.0, 0.0, 0.0); Border.child (ContentControl.create [ ContentControl.content pager ]) ]
                    ContentControl.create [ ContentControl.content body ]
                ]
            ] :> IView

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
                                      TextBlock.foreground colorPrimaryDeep
                                  ]
                              ]
                          ])
                  ]
                  :> IView ]

        let activeFilterCount =
            [ state.Media <> MediaFilter.All
              state.Favorites
              state.Hidden
              state.Downloaded
              state.Updates ]
            |> List.sumBy (fun active -> if active then 1 else 0)
            |> fun count -> count + state.WorkTypes.Count + state.TagFilter.Count

        let resetLibraryFilters () =
            updateState (fun s ->
                s.Media <- MediaFilter.All
                s.Favorites <- false
                s.Hidden <- false
                s.Downloaded <- false
                s.Updates <- false
                s.WorkTypes <- Set.empty
                s.TagFilter <- Set.empty
                s.Page <- 0)

        let modePanel : IView =
            WrapPanel.create [
                WrapPanel.margin (0.0, 4.0, 8.0, 0.0)
                WrapPanel.children [
                      filterChip (t "library.mode.list") (mode = LibraryViewMode.List) (fun () ->
                          setMode LibraryViewMode.List)
                      filterChip (t "library.mode.grid") (mode = LibraryViewMode.Grid) (fun () ->
                          setMode LibraryViewMode.Grid)
                      filterChip (t "library.mode.hybrid") (mode = LibraryViewMode.Hybrid) (fun () ->
                          setMode LibraryViewMode.Hybrid)
                ]
            ] :> IView

        let mediaFilters : IView =
              WrapPanel.create [
                  WrapPanel.margin (0.0, 4.0, 0.0, 0.0)
                  WrapPanel.children (
                      [ MediaFilter.All, "library.filter.all"
                        MediaFilter.Games, "library.filter.games"
                        MediaFilter.Images, "library.filter.images"
                        MediaFilter.Videos, "library.filter.videos"
                        MediaFilter.Audio, "library.filter.audio"
                        MediaFilter.Other, "library.filter.other" ]
                      |> List.map (fun (f0, key) ->
                          filterChip (t key) (state.Media = f0) (fun () ->
                              updateState (fun s -> s.Media <- f0; s.Page <- 0))))
              ]
              :> IView

        let statusFilters : IView =
              WrapPanel.create [
                  WrapPanel.margin (0.0, 4.0, 0.0, 0.0)
                  WrapPanel.children [
                      CheckBox.create [
                          CheckBox.content (t "library.filter.favorites")
                          CheckBox.isChecked state.Favorites
                          CheckBox.onIsCheckedChanged (fun e ->
                              match e.Source with
                              | :? CheckBox as cb when (cb.IsChecked = Nullable true) <> state.Favorites ->
                                  let value = cb.IsChecked = Nullable true
                                  updateState (fun s -> s.Favorites <- value; s.Page <- 0)
                              | _ -> ())
                      ]
                      CheckBox.create [
                          CheckBox.content (t "library.filter.hidden")
                          CheckBox.isChecked state.Hidden
                          CheckBox.onIsCheckedChanged (fun e ->
                              match e.Source with
                              | :? CheckBox as cb when (cb.IsChecked = Nullable true) <> state.Hidden ->
                                  let value = cb.IsChecked = Nullable true
                                  updateState (fun s -> s.Hidden <- value; s.Page <- 0)
                              | _ -> ())
                      ]
                      CheckBox.create [
                          CheckBox.content (t "library.filter.downloaded")
                          CheckBox.isChecked state.Downloaded
                          CheckBox.onIsCheckedChanged (fun e ->
                              match e.Source with
                              | :? CheckBox as cb when (cb.IsChecked = Nullable true) <> state.Downloaded ->
                                  let value = cb.IsChecked = Nullable true
                                  updateState (fun s -> s.Downloaded <- value; s.Page <- 0)
                              | _ -> ())
                      ]
                      CheckBox.create [
                          CheckBox.content (t "library.filter.updates")
                          CheckBox.isChecked state.Updates
                          CheckBox.onClick (fun _ ->
                              updateState (fun s -> s.Updates <- not s.Updates; s.Page <- 0))
                      ]
                  ]
              ]
              :> IView

        let sortControls : IView list =
            [
              let sortOptions =
                  [ SortCol.Purchase, "library.col.purchaseDate"
                    SortCol.Title, "library.col.title"
                    SortCol.Maker, "library.col.maker"
                    SortCol.WorkType, "library.col.workType"
                    SortCol.Rating, "library.col.rating"
                    SortCol.Sales, "library.col.salesDate"
                    SortCol.Downloaded, "library.col.downloaded"
                    SortCol.Size, "library.col.extractSize"
                    SortCol.Update, "library.col.update" ]
              WrapPanel.create [
                  WrapPanel.margin (0.0, 4.0, 0.0, 0.0)
                  WrapPanel.children [
                      TextBlock.create [
                          TextBlock.text (t "library.sort")
                          TextBlock.verticalAlignment VerticalAlignment.Center
                          TextBlock.margin (0.0, 0.0, 8.0, 0.0)
                          TextBlock.foreground colorOnSurfaceVariant
                          TextBlock.fontSize 13.0
                      ]
                      ComboBox.create [
                          ComboBox.width 160.0
                          ComboBox.margin (0.0, 0.0, 6.0, 4.0)
                          ComboBox.dataItems (sortOptions |> List.map (fun (_, key) -> t key))
                          ComboBox.selectedItem (
                              sortOptions
                              |> List.find (fun (col, _) -> col = state.SortCol)
                              |> snd
                              |> t)
                          ComboBox.onSelectedItemChanged (fun o ->
                              match o with
                              | :? string as label ->
                                  match sortOptions |> List.tryFind (fun (_, key) -> t key = label) with
                                  | Some (col, _) when col <> state.SortCol ->
                                      updateState (fun s -> s.SortCol <- col; s.Page <- 0)
                                  | _ -> ()
                              | _ -> ())
                      ]
                      Button.create [

                          Button.content (t "library.sort.asc")
                          Button.cornerRadius 8.0
                          Button.padding (12.0, 6.0)
                          Button.margin (0.0, 0.0, 6.0, 4.0)
                          Button.minHeight 36.0
                          Button.background (if state.SortAsc then colorPrimaryContainer else colorSurface)
                          Button.foreground (if state.SortAsc then colorPrimaryDeep else Ink)
                          Button.borderBrush (if state.SortAsc then colorPrimary else colorOutline)
                          Button.borderThickness 1.0
                          Button.fontSize 13.0
                          Button.onClick (fun _ ->
                              if not state.SortAsc then
                                  updateState (fun s -> s.SortAsc <- true; s.Page <- 0))
                      ]
                      Button.create [

                          Button.content (t "library.sort.desc")
                          Button.cornerRadius 8.0
                          Button.padding (12.0, 6.0)
                          Button.margin (0.0, 0.0, 6.0, 4.0)
                          Button.minHeight 36.0
                          Button.background (if state.SortAsc then colorSurface else colorPrimaryContainer)
                          Button.foreground (if state.SortAsc then Ink else colorPrimaryDeep)
                          Button.borderBrush (if state.SortAsc then colorOutline else colorPrimary)
                          Button.borderThickness 1.0
                          Button.fontSize 13.0
                          Button.onClick (fun _ ->
                              if state.SortAsc then
                                  updateState (fun s -> s.SortAsc <- false; s.Page <- 0))
                      ]
                  ]
              ]
              :> IView ]

        let advancedChildren : IView list =
            (if not state.ExpandAdvanced then [] else (
                              (if typesAvailable.IsEmpty then []
                               else
                                   disclosure
                                       (t "library.workTypes")
                                       state.ExpandTypes
                                       (fun () -> updateState (fun s -> s.ExpandTypes <- not s.ExpandTypes))
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
                                                                 CheckBox.content (workTypeLabel (svc.Library.WorkTypeDisplay wt) wt)
                                                                 CheckBox.margin (4.0, 2.0)
                                                                 CheckBox.isChecked (state.WorkTypes.Contains wt)
                                                                 CheckBox.onIsCheckedChanged (fun e ->
                                                                     match e.Source with
                                                                     | :? CheckBox as cb when (cb.IsChecked = Nullable true) <> state.WorkTypes.Contains wt ->
                                                                         let value = cb.IsChecked = Nullable true
                                                                         updateState (fun s ->
                                                                             s.WorkTypes <- if value then s.WorkTypes.Add wt else s.WorkTypes.Remove wt
                                                                             s.Page <- 0)
                                                                     | _ -> ())
                                                             ]
                                                             :> IView))
                                                 ])
                                         ]
                                         :> IView ])
                            @ disclosure
                                  (t "library.tagFilter")
                                  state.ExpandTags
                                  (fun () ->
                                      let opening = not state.ExpandTags
                                      updateState (fun s -> s.ExpandTags <- opening)
                                      if opening && not tagsLoading.Current then
                                          tagsLoading.Set true
                                          runPoolAsyncUnit
                                              (fun () -> svc.Library.EnsureLibraryTagsAsync())
                                              (fun () -> tagsLoading.Set false)
                                              (fun _ -> tagsLoading.Set false))
                                  [ Border.create [
                                        Border.background colorSurfaceVariant
                                        Border.cornerRadius 8.0
                                        Border.padding 12.0
                                        Border.child (
                                            StackPanel.create [
                                                StackPanel.spacing 8.0
                                                StackPanel.children [
                                                    TextBlock.create [
                                                        TextBlock.text (t "library.tags.hint")
                                                        TextBlock.foreground colorOnSurfaceVariant
                                                        TextBlock.textWrapping TextWrapping.Wrap
                                                    ]
                                                    WrapPanel.create [
                                                        WrapPanel.children [
                                                            filterChip (t "library.tags.matchAll") state.MatchAllTags (fun () -> updateState (fun s -> s.MatchAllTags <- true; s.Page <- 0))
                                                            filterChip (t "library.tags.matchAny") (not state.MatchAllTags) (fun () -> updateState (fun s -> s.MatchAllTags <- false; s.Page <- 0))
                                                        ]
                                                    ]
                                                    TextBox.create [
                                                        TextBox.watermark (t "library.tags.search")
                                                        TextBox.text state.TagSearch
                                                        TextBox.onTextChanged (fun value ->
                                                            if value <> state.TagSearch then
                                                                updateState (fun s -> s.TagSearch <- value; s.TagLimit <- 60))
                                                    ]
                                                    if tagsLoading.Current then
                                                        ProgressBar.create [ ProgressBar.height 3.0; ProgressBar.isIndeterminate true ]
                                                    TextBlock.create [
                                                        TextBlock.text (if tagsLoading.Current then t "library.tags.loading" elif tagsAvailable.IsEmpty then t "library.tags.empty" else "")
                                                        TextBlock.isVisible (tagsLoading.Current || tagsAvailable.IsEmpty)
                                                        TextBlock.foreground colorOnSurfaceVariant
                                                    ]
                                                    TextBlock.create [
                                                        TextBlock.text (fmt3 (t "library.tags.summary") shownTags.Length matchingTags.Length state.TagFilter.Count)
                                                        TextBlock.foreground colorOnSurfaceVariant
                                                    ]
                                                    if matchingTags.IsEmpty && not tagsAvailable.IsEmpty then
                                                        TextBlock.create [
                                                            TextBlock.text (t "library.tags.noResults")
                                                            TextBlock.textWrapping TextWrapping.Wrap
                                                        ]
                                                    ScrollViewer.create [
                                                        ScrollViewer.maxHeight 240.0
                                                        ScrollViewer.horizontalScrollBarVisibility Primitives.ScrollBarVisibility.Disabled
                                                        ScrollViewer.content (
                                                            WrapPanel.create [
                                                                WrapPanel.children (
                                                                    shownTags
                                                                    |> List.map (fun tag ->
                                                                        CheckBox.create [
                                                                            CheckBox.content tag
                                                                            CheckBox.margin (4.0, 2.0, 16.0, 2.0)
                                                                            CheckBox.isChecked (state.TagFilter.Contains tag)
                                                                            CheckBox.onClick (fun _ ->
                                                                                updateState (fun s ->
                                                                                    s.TagFilter <- if s.TagFilter.Contains tag then s.TagFilter.Remove tag else s.TagFilter.Add tag
                                                                                    s.Page <- 0))
                                                                        ] |> View.withKey ("tag:" + tag) :> IView))
                                                            ])
                                                    ]
                                                    if shownTags.Length < matchingTags.Length then
                                                        Button.create [
                                                            Button.content (t "library.tags.showMore")
                                                            Button.onClick (fun _ -> updateState (fun s -> s.TagLimit <- s.TagLimit + 60))
                                                        ]
                                                ]
                                            ])
                                    ] :> IView ]
                            @ disclosure
                                  (t "library.columns")
                                  state.ExpandCols
                                  (fun () -> updateState (fun s -> s.ExpandCols <- not s.ExpandCols))
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
                                                                    let have = colVisible (parseVisible svc.Settings.Current.VisibleColumns) key
                                                                    if want <> have then toggleCol key
                                                                | _ -> ())
                                                        ]
                                                        :> IView))
                                            ])
                                    ]
                                    :> IView ]
            ))


        let toolbarSummary : IView =
            WrapPanel.create [
                WrapPanel.margin (0.0, 0.0, 0.0, 4.0)
                WrapPanel.children ([ modePanel ] @ sortControls)
            ] :> IView

        let filterResetButton : IView =
            Button.create [
                Button.content (t "library.filters.reset")
                Button.isEnabled (activeFilterCount > 0)
                Button.onClick (fun _ -> resetLibraryFilters ())
            ] :> IView

        let filterDisclosure : IView list =
            if not state.ExpandAdvanced then []
            else
                [ ScrollViewer.create [
                      ScrollViewer.maxHeight 280.0
                      ScrollViewer.horizontalScrollBarVisibility Primitives.ScrollBarVisibility.Disabled
                      ScrollViewer.content (
                          StackPanel.create [
                              StackPanel.spacing 6.0
                              StackPanel.children ([
                                  WrapPanel.create [ WrapPanel.children [ filterResetButton ] ] :> IView
                                  mediaFilters
                                  statusFilters
                              ] @ advancedChildren)
                          ])
                  ] :> IView ]

        let toolbarChildren : IView list =
            [ DockPanel.create [
                  DockPanel.margin (0.0, 0.0, 0.0, 8.0)
                  DockPanel.children [
                      TextBlock.create [ DockPanel.dock Dock.Left; TextBlock.text (t "nav.library"); TextBlock.fontSize 26.0; TextBlock.fontWeight FontWeight.Bold; TextBlock.foreground Charcoal; TextBlock.verticalAlignment VerticalAlignment.Center; TextBlock.margin (0.0, 0.0, 24.0, 0.0) ]
                      Button.create [ DockPanel.dock Dock.Right; Button.content (fmt1 (t "library.filters.count") activeFilterCount); Button.margin (8.0, 0.0, 0.0, 0.0); Button.minHeight 36.0; Button.onClick (fun _ -> updateState (fun s -> s.ExpandAdvanced <- not s.ExpandAdvanced)) ]
                      Button.create [
                          DockPanel.dock Dock.Right
                          Button.content (fmt1 (t "library.action.updateAll") updatableCount)
                          Button.margin (8.0, 0.0, 0.0, 0.0)
                          Button.minHeight 36.0
                          Button.isEnabled (updatableCount > 0)
                          Button.onClick (fun _ ->
                              let items = queryWorks () |> List.filter (fun w -> w.HasUpdate)
                              if items.Length > 0 then
                                  svc.Downloads.Enqueue(items, true)
                                  onGoDownloads ())
                      ]
                      TextBox.create [ TextBox.minHeight 40.0; TextBox.watermark (t "library.search"); TextBox.text state.Search; TextBox.onTextChanged (fun value -> if value <> state.Search then updateState (fun s -> s.Search <- value; s.Page <- 0)) ]
                  ]
              ] :> IView
              toolbarSummary ]
            @ filterDisclosure
            @ [ for w in svc.Library.Works do
                    let launchState = svc.Launcher.GetLaunchState w.Workno
                    if launchState = GameLaunchState.Preparing || launchState = GameLaunchState.Starting then
                        StackPanel.create [
                            StackPanel.orientation Orientation.Horizontal
                            StackPanel.spacing 8.0
                            StackPanel.children [
                                ProgressBar.create [ ProgressBar.width 64.0; ProgressBar.height 4.0; ProgressBar.verticalAlignment VerticalAlignment.Center; ProgressBar.isIndeterminate true ]
                                TextBlock.create [ TextBlock.text (w.WorkName + " — " + t "launcher.starting") ]
                            ]
                        ] :> IView
                    elif launchState = GameLaunchState.Failed then
                        TextBlock.create [ TextBlock.text (w.WorkName + " — " + t (svc.Launcher.GetLaunchError w.Workno)) ] :> IView ]
            @ selectionBar
            @ (if state.TagFilter.IsEmpty then [] else
                [ WrapPanel.create [
                      WrapPanel.margin (0.0, 8.0, 0.0, 4.0)
                      WrapPanel.children (
                          [ filterChip (t "library.tags.clear") false (fun () -> updateState (fun s -> s.TagFilter <- Set.empty; s.Page <- 0)) ]
                          @ (state.TagFilter |> Set.toList |> List.map (fun tag ->
                              filterChipView (tag + " ×") true (fun () -> updateState (fun s -> s.TagFilter <- s.TagFilter.Remove tag; s.Page <- 0))
                              |> View.withKey ("selected-tag:" + tag)
                              :> IView)))
                  ] :> IView ])
            @ (if String.IsNullOrEmpty status.Current then [] else
                [ TextBlock.create [
                      TextBlock.text status.Current
                      TextBlock.foreground Error
                      TextBlock.fontSize 13.0
                      TextBlock.textWrapping TextWrapping.Wrap
                  ] :> IView ]
               @ (if String.IsNullOrEmpty statusDetail.Current then []
                  else
                      [ TextBlock.create [
                            TextBlock.text statusDetail.Current
                            TextBlock.foreground colorOnSurfaceVariant
                            TextBlock.fontSize 12.0
                            TextBlock.textWrapping TextWrapping.Wrap
                        ] :> IView ]))


        DockPanel.create [
            DockPanel.background colorSurfaceVariant
            DockPanel.children [
                Border.create [
                    DockPanel.dock Dock.Top
                    Border.background colorSurface
                    Border.borderBrush colorOutline
                    Border.borderThickness (0.0, 0.0, 0.0, 1.0)
                    Border.padding (20.0, 16.0)
                    Border.child (
                        StackPanel.create [
                            StackPanel.spacing 4.0
                            StackPanel.children toolbarChildren
                        ])
                ]
                Border.create [
                    Border.margin (20.0, 12.0, 20.0, 16.0)
                    Border.background colorSurface
                    Border.cornerRadius 8.0
                    Border.borderBrush colorOutline
                    Border.borderThickness 1.0
                    Border.clipToBounds true
                    Border.child (ContentControl.create [ ContentControl.content bodyWithPager ])
                ]
            ]
        ])

let viewWithState (state: LibraryViewState) (onOpenDetail: string -> unit) (onGoDownloads: unit -> unit) =
    viewCore (Some state) onOpenDetail onGoDownloads

let view (onOpenDetail: string -> unit) (onGoDownloads: unit -> unit) =
    viewCore None onOpenDetail onGoDownloads
