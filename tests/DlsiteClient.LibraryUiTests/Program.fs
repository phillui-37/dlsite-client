open System
open System.IO
open System.Net
open System.Net.Http
open System.Threading
open System.Threading.Tasks
open Avalonia
open Avalonia.Controls
open Avalonia.Controls.Primitives
open Avalonia.Headless
open Avalonia.Interactivity
open Avalonia.Input
open Avalonia.Automation
open Avalonia.LogicalTree
open Avalonia.Threading
open Microsoft.Extensions.DependencyInjection
open DlsiteClient.Core
open DlsiteClient.Core.Models
open DlsiteClient.Core.Services
open DlsiteClient.Core.Storage
open DlsiteClient.Platform
open DlsiteClient.Platform.Linux
open DlsiteClient.App

// No network, account, downloads or real library are used.
type OfflineHandler() =
    inherit HttpMessageHandler()
    override _.SendAsync(_, _) = Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK, Content = new StringContent("{}")))
type OfflineClients() =
    interface IHttpClientFactory with
        member _.CreateClient _ = new HttpClient(new OfflineHandler())

type UiGames() =
    let mutable running = false
    member _.Running with get() = running and set value = running <- value
    interface IGameProcesses with member _.IsRunning _ = running

type UiStarter() =
    let mutable calls = 0
    member _.Calls = calls
    interface IProcessLocale with member _.StartWithLocale(_,_,_,_) = Interlocked.Increment(&calls) |> ignore

type RecordingShell() =
    let mutable opened = ""
    member _.Opened = opened
    interface IShell with
        member _.OpenFolder path = opened <- path
        member _.OpenUrl _ = ()
        member _.Confirm(_,_) = false
let screenshotDirectory = Environment.GetEnvironmentVariable("DLSITE_UI_SCREENSHOTS")
AppBuilder.Configure<App>().UseSkia().WithInterFont().UseHeadless(AvaloniaHeadlessPlatformOptions(UseHeadlessDrawing = false)).SetupWithoutStarting() |> ignore
let root = Path.Combine(Path.GetTempPath(), "dlsite-library-ui-" + Guid.NewGuid().ToString("N"))
Directory.CreateDirectory root |> ignore
let paths =
    { new IPathConfig with
        member _.AppDataRoot = root
        member _.DefaultLibraryDirectory = Path.Combine(root, "absent-library")
        member _.DefaultCacheDirectory = Path.Combine(root, "cache")
        member _.DefaultLogDirectory = Path.Combine(root, "logs") }
let services = ServiceCollection()
services.AddLogging() |> ignore
services.AddSingleton<ICredentialStore, LinuxCredentialStore>() |> ignore
services.AddSingleton<IGameProcesses, UiGames>() |> ignore
services.AddSingleton<IProcessLocale, UiStarter>() |> ignore
services.AddSingleton<IShell, RecordingShell>() |> ignore
services.AddDlsiteCore paths |> ignore
services.AddSingleton<IHttpClientFactory, OfflineClients>() |> ignore
services.AddSingleton<LauncherService>(fun sp ->
    LauncherService(sp.GetRequiredService<LibraryDatabase>(), sp.GetRequiredService<IProcessLocale>(),
        sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<LauncherService>>(), sp.GetRequiredService<IGameProcesses>(),
        LaunchLockDirectory = Path.Combine(root, "launch-locks"))) |> ignore
let provider = services.BuildServiceProvider()
let settings = provider.GetRequiredService<SettingsService>()
settings.Update(fun s -> s.LibraryDirectory <- Path.Combine(root, "not-created"); s.UiLocale <- "ja"; s.VisibleColumns <- "purchaseDate,maker,workType,rating,salesDate,downloaded,extractSize,update")
Localization.globalI18n.Load("ja", Path.GetFullPath "locales")
let db = provider.GetRequiredService<LibraryDatabase>()
let works =
    [ for i in 1..25 ->
        WorkItem(Workno = "RJ" + i.ToString("D3"), WorkName = "WORK-" + i.ToString("D3"),
                 RegistDate = DateTime(2026,1,1).AddDays(float i).ToString("yyyy-MM-dd"),
                 TagsJson = (if i % 2 = 0 then "[\"school\"]" else "[\"school uniform\"]"),
                 IsDownloaded = true, LocalUpdateDate = "2025-01-01", UpdateDate = if i % 2 = 0 then "2026-01-01" else "2024-01-01") ]
db.UpsertWorks works
let library = provider.GetRequiredService<LibraryService>()
Services.shared <- Some {
    Provider = provider; Auth = provider.GetRequiredService<AuthSession>(); Library = library
    Downloads = provider.GetRequiredService<DownloadQueueService>(); Settings = settings
    Launcher = provider.GetRequiredService<LauncherService>(); Paths = paths
    Protocol = LinuxProtocolHandler(); Startup = LinuxStartupRegistration(); Shell = provider.GetRequiredService<IShell>(); LocalesDir = Path.GetFullPath "locales" }
let window = Window(Width = 1600., Height = 1000.)
window.Content <- Views.LibraryView.view ignore ignore
window.Show()
let pump () =
    for _ in 1..20 do
        Dispatcher.UIThread.RunJobs()
        Thread.Sleep 5
let descendants () = window.GetLogicalDescendants() |> Seq.toList
let buttons () = descendants () |> List.choose (function :? Button as b -> Some b | _ -> None)
let click (b: Button) = b.RaiseEvent(RoutedEventArgs(Button.ClickEvent)); pump ()
let textOf (c: Control) =
    c.GetLogicalDescendants() |> Seq.choose (function :? TextBlock as t -> Some t.Text | _ -> None) |> String.concat " "
let rows () =
    descendants () |> List.choose (function
        | :? CheckBox as cb when cb.Content = null ->
            let mutable parent = cb.GetLogicalParent()
            let mutable title = ""
            while parent <> null && title = "" do
                match parent with
                | :? Border as border ->
                    let text = textOf border
                    if text.Contains "WORK-" then title <- works |> List.find (fun w -> text.Contains w.WorkName) |> fun w -> w.Workno
                | _ -> ()
                parent <- parent.GetLogicalParent()
            if title = "" then None else Some(title, cb)
        | _ -> None)
let checkedIds () = rows () |> List.choose (fun (id, cb) -> if cb.IsChecked = Nullable true then Some id else None) |> Set.ofList
let check expected label =
    let actual = checkedIds ()
    if actual <> Set.ofList expected then failwith (String.Concat(label, ": expected ", String.Join(",",expected), "; actual ",String.Join(",",actual)))
    Console.WriteLine("PASS " + label)
let userToggle (cb: CheckBox) =
    // ToggleButton changes its value before it raises Click for mouse/keyboard input.
    cb.IsChecked <- Nullable(not (cb.IsChecked = Nullable true))
    cb.RaiseEvent(RoutedEventArgs(Button.ClickEvent))
    pump ()
pump ()
check [] "initial selection is empty"
let updateHeader = buttons () |> List.find (fun b -> string b.Content = Localization.t "library.col.update")
let initialFirst = rows () |> List.head |> fst

click updateHeader
click (buttons () |> List.find (fun b -> string b.Content = Localization.t "library.col.update" + " ▲"))


let firstId, firstBox = rows () |> List.head
if firstId = initialFirst then failwith "Update sort did not reorder the fixture"
userToggle firstBox
check [firstId] "update sort selects only the clicked work"
// A redraw must not change the selection.
library.Reload(); pump ()
check [firstId] "library redraw keeps selection identity"
let secondId, secondBox = rows () |> List.find (fun (id, _) -> id <> firstId)
userToggle secondBox
check [firstId;secondId] "second click adds exactly one work"
userToggle (rows () |> List.find (fun (id, _) -> id = firstId) |> snd)
check [secondId] "uncheck removes only the clicked work"
let byLabel key =
    buttons ()
    |> List.find (fun b ->
        let label = string b.Content
        if key <> "library.filters" then label = Localization.t key
        else
            let format = Localization.t "library.filters.count"
            label.StartsWith(format.Substring(0, format.IndexOf("{0}", StringComparison.Ordinal)), StringComparison.Ordinal))
click (buttons () |> List.find (fun b -> string b.Content = Localization.t "library.col.update" + " ▼"))
check [secondId] "sorting with selection does not select other works"
click (byLabel "library.mode.grid")
check [secondId] "grid keeps the selected work"
let gridId, gridBox = rows () |> List.find (fun (id,_) -> id <> secondId)
userToggle gridBox
check [secondId;gridId] "grid click adds only its work"
library.Reload(); pump ()
check [secondId;gridId] "grid redraw keeps selection identity"
click (byLabel "library.mode.hybrid")
check [secondId;gridId] "hybrid keeps selection"
let searchBox = descendants () |> List.pick (function :? TextBox as tb when string tb.PlaceholderText = Localization.t "library.search" -> Some tb | _ -> None)
let searchWork = works |> List.find (fun w -> w.Workno = secondId)
searchBox.Text <- searchWork.WorkName; pump ()
check [secondId] "filter hides unrelated selected works without transferring selection"
searchBox.Text <- ""; pump ()
check [secondId;gridId] "clearing filter restores the same selected works"
click (byLabel "library.next")
let pageId, pageBox = rows () |> List.head
if pageId = secondId || pageId = gridId then failwith "Paging fixture did not move to another work"
check [] "next page has no inherited checkmarks"
userToggle pageBox
check [pageId] "hybrid click on next page selects only its work"
click (byLabel "library.prev")
check [secondId;gridId] "previous page retains its own selection"
click (byLabel "library.next")
check [pageId] "return to next page retains its own selection"
let assertUi ok label = if not ok then failwith label else Console.WriteLine("PASS " + label)
let filtersOpen () = descendants () |> List.exists (function :? CheckBox as cb when string cb.Content = Localization.t "library.filter.updates" -> true | _ -> false)
assertUi (not (filtersOpen ())) "filters stay collapsed by default"
let ensureFiltersOpen () = if not (filtersOpen ()) then click (byLabel "library.filters")
ensureFiltersOpen ()
let favoritesBox = descendants () |> List.pick (function :? CheckBox as cb when string cb.Content = Localization.t "library.filter.favorites" -> Some cb | _ -> None)
userToggle favoritesBox
assertUi ((rows ()).IsEmpty) "favorite filter may narrow to an empty catalog"
assertUi ((string (byLabel "library.filters").Content) = (Localization.t "library.filters.count").Replace("{0}", "1")) "filter badge counts active filters"
let resetButton = buttons () |> List.find (fun b -> string b.Content = Localization.t "library.filters.reset")
click resetButton
assertUi ((rows ()).Length = 20) "filter disclosure reset restores first page"
let updatesBox () = descendants () |> List.pick (function :? CheckBox as cb when string cb.Content = Localization.t "library.filter.updates" -> Some cb | _ -> None)
userToggle (updatesBox ())
assertUi ((rows ()).Length = 12 && rows () |> List.forall (fun (id,_) -> (works |> List.find (fun w -> w.Workno = id)).HasUpdate)) "update filter resets paging and shows only outdated installs"
ensureFiltersOpen ()
click (buttons () |> List.find (fun b -> (string b.Content).EndsWith(Localization.t "library.tagFilter")))
let tagBox tag = descendants () |> List.pick (function :? CheckBox as cb when string cb.Content = tag -> Some cb | _ -> None)
userToggle (tagBox "school uniform")
assertUi ((rows ()).IsEmpty) "tag and update filters combine in the UI"
userToggle (updatesBox ())
assertUi ((rows ()).Length = 13) "tag filter uses the complete tag name"
userToggle (tagBox "school")
assertUi ((rows ()).IsEmpty) "match-all requires both selected tags"
assertUi ((tagBox "school").IsChecked = Nullable true && (tagBox "school uniform").IsChecked = Nullable true) "multiple tags stay checked"
click (byLabel "library.tags.matchAny")
assertUi ((rows ()).Length = 20) "match-any combines selected tags"
let tagSearchBox = descendants () |> List.pick (function :? TextBox as tb when string tb.PlaceholderText = Localization.t "library.tags.search" -> Some tb | _ -> None)
tagSearchBox.Text <- "uniform"; pump ()
assertUi ((rows ()).Length = 20) "tag search does not change selected filters"
assertUi (descendants () |> List.exists (function :? CheckBox as cb -> string cb.Content = "school" | _ -> false) |> not) "tag search narrows choices"
click (buttons () |> List.find (fun b -> (string b.Content).EndsWith(Localization.t "library.tagFilter")))
click (byLabel "library.tags.clear")
assertUi ((rows ()).Length = 20) "clear tags is available with tag section collapsed"
// A large catalog creates only one batch of controls, while search covers every tag.
works.Head.TagsJson <- System.Text.Json.JsonSerializer.Serialize(Array.append [| "school uniform" |] [| for i in 1..130 -> "zz-tag-" + i.ToString("D3") |])
db.UpsertWorks [works.Head]
library.Reload()
pump ()
click (buttons () |> List.find (fun b -> (string b.Content).EndsWith(Localization.t "library.tagFilter")))
let searchTags () = descendants () |> List.pick (function :? TextBox as tb when string tb.PlaceholderText = Localization.t "library.tags.search" -> Some tb | _ -> None)
(searchTags ()).Text <- ""
pump ()
let catalogBoxes () = descendants () |> List.choose (function :? CheckBox as cb when (string cb.Content).StartsWith("zz-tag-") || string cb.Content = "school" || string cb.Content = "school uniform" -> Some cb | _ -> None)
assertUi ((catalogBoxes ()).Length = 60) "large tag catalog initially renders only sixty choices"
click (byLabel "library.tags.showMore")
assertUi ((catalogBoxes ()).Length = 120) "show more expands the tag batch"
(searchTags ()).Text <- "zz-tag-130"
pump ()
assertUi ((catalogBoxes ()).Length = 1) "search reaches tags beyond rendered batches"
userToggle (tagBox "zz-tag-130")
assertUi ((rows ()).Length = 1) "selecting a searched tag filters the library"
(searchTags ()).Text <- "no-such-tag"
pump ()
assertUi (descendants () |> List.exists (function :? TextBlock as tb -> tb.Text = Localization.t "library.tags.noResults" | _ -> false)) "empty tag search explains no results"
assertUi ((rows ()).Length = 1) "empty search results retain selected filters"
click (byLabel "library.tags.clear")
let detailWork = works.Head
let gameDir = Path.Combine(root, "installed-game")
Directory.CreateDirectory gameDir |> ignore
let gameExe = Path.Combine(gameDir,"Game.exe")
File.WriteAllText(gameExe,"not executed")
detailWork.InstallPath <- gameDir
detailWork.SelectedExe <- gameExe
db.SetDownloaded(detailWork.Workno, true, detailWork.InstallPath, detailWork.LocalUpdateDate)
db.SetSelectedExe(detailWork.Workno, gameExe, false)
library.Reload(); pump ()
window.Content <- Views.DetailView.view detailWork.Workno ignore
pump ()
assertUi (descendants () |> List.exists (function :? TextBlock as tb -> tb.Text = "school uniform" | _ -> false)) "detail displays cached genre tags"
let tagScroller = descendants () |> List.pick (function :? ScrollViewer as v when v.Name = "detail-tags" -> Some v | _ -> None)
assertUi (tagScroller.Bounds.Height <= 120. && tagScroller.Extent.Height > tagScroller.Viewport.Height) "large detail tag list stays in a bounded scroll area"
let folder = byLabel "detail.openFolder"
assertUi folder.IsEnabled "installed work enables Open game folder"
click folder
assertUi ((provider.GetRequiredService<IShell>() :?> RecordingShell).Opened = gameDir) "Open game folder uses the recorded install path"
click (byLabel "detail.play")
let launcher = provider.GetRequiredService<LauncherService>()
let waitFor state =
    let mutable attempts = 0
    while launcher.GetLaunchState(detailWork.Workno) <> state && attempts < 25 do
        pump (); attempts <- attempts + 1
    assertUi (launcher.GetLaunchState(detailWork.Workno) = state) ("launch state reaches " + string state)
waitFor GameLaunchState.Starting
assertUi (descendants () |> List.exists (function :? ProgressBar as p -> p.IsIndeterminate | _ -> false)) "Start shows an indeterminate loading indicator"
assertUi (not (byLabel "detail.play").IsEnabled) "Start is disabled while the process is pending"
launcher.ConfirmAndLaunch(detailWork.Workno, gameExe, false, false)
assertUi ((provider.GetRequiredService<IProcessLocale>() :?> UiStarter).Calls = 1) "repeated Start does not open another game"
(provider.GetRequiredService<IGameProcesses>() :?> UiGames).Running <- true
waitFor GameLaunchState.Running
pump ()
assertUi (not (descendants () |> List.exists (function :? ProgressBar as p -> p.IsIndeterminate | _ -> false))) "spinner stops when the process appears"
assertUi (not (byLabel "launcher.running").IsEnabled) "running game keeps Start disabled"
(provider.GetRequiredService<IGameProcesses>() :?> UiGames).Running <- false
waitFor GameLaunchState.Idle
pump ()
assertUi ((byLabel "detail.play").IsEnabled) "process exit enables Start again"
detailWork.InstallPath <- Path.Combine(root,"missing-game")
db.SetDownloaded(detailWork.Workno, true, detailWork.InstallPath, detailWork.LocalUpdateDate)
db.SetSelectedExe(detailWork.Workno, gameExe, false); library.Reload(); pump ()
assertUi (not (byLabel "detail.openFolder").IsEnabled) "missing directory disables Open game folder"
// Exercise keyboard activation through the real headless input boundary.
let mutable keyboardOpened = ""
window.Content <- Views.LibraryView.view (fun id -> keyboardOpened <- id) ignore
pump ()
for modeKey in [ "library.mode.grid"; "library.mode.hybrid" ] do
    click (byLabel modeKey)
    let card = descendants () |> List.pick (function :? Border as b when b.Classes.Contains("work") -> Some b | _ -> None)
    assertUi (not (String.IsNullOrWhiteSpace(AutomationProperties.GetName card))) (modeKey + " has an accessible work name")
    keyboardOpened <- ""
    assertUi (card.Focus(NavigationMethod.Tab)) (modeKey + " accepts keyboard focus")
    window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, "\r")
    window.KeyRelease(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, "\r")
    pump ()
    assertUi (works |> List.exists (fun w -> w.Workno = keyboardOpened)) (modeKey + " opens detail with Enter")

if not (String.IsNullOrEmpty screenshotDirectory) then
    Directory.CreateDirectory screenshotDirectory |> ignore
let capture name =
    if not (String.IsNullOrEmpty screenshotDirectory) then
        window.UpdateLayout()
        pump ()
        use frame = window.CaptureRenderedFrame()
        if isNull frame then failwith ("No rendered frame: " + name)
        frame.Save(Path.Combine(screenshotDirectory, name + ".png"))

for locale in [ "ja"; "zh-TW" ] do
    Localization.globalI18n.Load(locale, Path.GetFullPath "locales")
    for width in [ 960.; 1280.; 1440. ] do
        settings.Update(fun s -> s.ViewMode <- LibraryViewMode.List)
        window.Width <- width
        window.Height <- 800.
        window.Content <- Views.MainView.view ()
        pump ()
        for key in [ "library.mode.list"; "library.mode.grid"; "library.mode.hybrid"; "library.filters"; "library.action.updateAll" ] do
            let button =
                if key <> "library.action.updateAll" then byLabel key
                else
                    let format = Localization.t key
                    let prefix = format.Substring(0, format.IndexOf("{0}", StringComparison.Ordinal))
                    buttons () |> List.find (fun b -> (string b.Content).StartsWith(prefix, StringComparison.Ordinal))
            let point = button.TranslatePoint(Point(0., 0.), window).Value
            assertUi (point.X >= 176. && point.X + button.Bounds.Width <= width && point.Y >= 0. && point.Y + button.Bounds.Height <= 800.)
                (locale + " " + string width + " " + key + " stays inside window")
        if width = 960. then
            let resultsY =
                descendants ()
                |> List.choose (function
                    | :? TextBlock as tb when tb.Text = Localization.t "library.col.title" ->
                        let point = tb.TranslatePoint(Point(0., 0.), window)
                        if point.HasValue then Some point.Value.Y else None
                    | _ -> None)
                |> List.tryHead
            assertUi (resultsY |> Option.exists (fun y -> y < 230.))
                (locale + " compact controls reveal the catalog near the top of 960px view")
        capture (locale + "-library-list-" + string width)
        if width = 960. then
            (byLabel "nav.library").Focus(NavigationMethod.Tab) |> ignore
            capture (locale + "-navigation-focus-960")
        if width <> 1280. then
            click (byLabel "library.mode.grid")
            capture (locale + "-library-grid-" + string width)
            click (byLabel "library.mode.hybrid")
            capture (locale + "-library-hybrid-" + string width)
            click (byLabel "nav.downloads")
            capture (locale + "-downloads-" + string width)
            click (byLabel "nav.settings")
            capture (locale + "-settings-" + string width)
            click (byLabel "nav.library")
            click (byLabel "library.mode.hybrid")
            let firstWork = descendants () |> List.pick (function :? Border as b when b.Classes.Contains("work") -> Some b | _ -> None)
            firstWork.Focus(NavigationMethod.Tab) |> ignore
            window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, "\r")
            window.KeyRelease(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, "\r")
            pump ()
            capture (locale + "-detail-" + string width)

// Regression: paging past page 2 (including when catalog grows after initial mount),
// preserving Library filters/search/sort/page/disclosure across page transitions and
// Download/Update routing in MainView, and Update all button behavior.
Localization.globalI18n.Load("ja", Path.GetFullPath "locales")
Services.shared.Value.Downloads.PauseForAuth()
let headingExists key count =
    let expected = String.Format("{0} ({1})", Localization.t key, count)
    descendants () |> List.exists (function :? TextBlock as tb -> tb.Text = expected | _ -> false)
let dbJobs () = [ for j in db.LoadDownloadJobs() -> j ]
let extraWorks =
    [ for i in 26..65 ->
        WorkItem(Workno = "RJ" + i.ToString("D3"), WorkName = "WORK-" + i.ToString("D3"),
                 RegistDate = DateTime(2026,1,1).AddDays(float i).ToString("yyyy-MM-dd"),
                 TagsJson = (if i % 2 = 0 then "[\"school\"]" else "[\"school uniform\"]"),
                 IsDownloaded = true, LocalUpdateDate = "2025-01-01", UpdateDate = if i % 2 = 0 then "2026-01-01" else "2024-01-01") ]
let fullCatalog = works @ extraWorks
let rowsIn (catalog: WorkItem list) =
    descendants () |> List.choose (function
        | :? CheckBox as cb when cb.Content = null ->
            let mutable parent = cb.GetLogicalParent()
            let mutable title = ""
            while parent <> null && title = "" do
                match parent with
                | :? Border as border ->
                    let text = textOf border
                    if text.Contains "WORK-" then title <- catalog |> List.find (fun w -> text.Contains w.WorkName) |> fun w -> w.Workno
                | _ -> ()
                parent <- parent.GetLogicalParent()
            if title = "" then None else Some(title, cb)
        | _ -> None)
let hasPageLabel p totalPages =
    let expected = Fmt.fmt2 (Localization.t "library.pageOf") p totalPages
    descendants () |> List.exists (function :? TextBlock as tb -> tb.Text = expected | _ -> false)

settings.Update(fun s -> s.ViewMode <- LibraryViewMode.List; s.LibraryPageSize <- 20)
window.Width <- 1600.
window.Height <- 1000.
window.Content <- Views.MainView.view ()
pump ()
// Grow catalog AFTER initial mount and trigger an in-place re-render (same pager Button instances)
// to prove pager does not freeze initial pageCount or pageIdx.
db.UpsertWorks extraWorks
library.Reload()
click (byLabel "library.mode.list")
assertUi (hasPageLabel 1 4) "multi-page catalog starts on page 1/4"
let p1Ids = rowsIn fullCatalog |> List.map fst |> Set.ofList
click (byLabel "library.next")
assertUi (hasPageLabel 2 4) "next advances from page 1 to page 2/4"
let p2Ids = rowsIn fullCatalog |> List.map fst |> Set.ofList
click (byLabel "library.next")
assertUi (hasPageLabel 3 4) "next advances from page 2 to page 3/4"
let p3Ids = rowsIn fullCatalog |> List.map fst |> Set.ofList
click (byLabel "library.next")
assertUi (hasPageLabel 4 4) "next advances from page 3 to page 4/4"
let p4Ids = rowsIn fullCatalog |> List.map fst |> Set.ofList
assertUi (p1Ids.Count = 20 && p2Ids.Count = 20 && p3Ids.Count = 20 && p4Ids.Count = 5) "all four pages show expected work counts"
assertUi ((Set.unionMany [ p1Ids; p2Ids; p3Ids; p4Ids ]).Count = 65) "pages 1 through 4 show pairwise disjoint works"
click (byLabel "library.prev")
assertUi (hasPageLabel 3 4 && (rowsIn fullCatalog |> List.map fst |> Set.ofList) = p3Ids) "prev returns from page 4 to page 3/4"
click (byLabel "library.prev")
assertUi (hasPageLabel 2 4 && (rowsIn fullCatalog |> List.map fst |> Set.ofList) = p2Ids) "prev returns from page 3 to page 2/4"
click (byLabel "library.prev")
assertUi (hasPageLabel 1 4 && (rowsIn fullCatalog |> List.map fst |> Set.ofList) = p1Ids) "prev returns from page 2 to page 1/4"

// Apply filters, sort, and page 2 inside MainView, then verify persistence across navigation and Download/Update routing.
ensureFiltersOpen ()
userToggle (updatesBox ())
click (buttons () |> List.find (fun b -> string b.Content = Localization.t "library.col.title"))
click (byLabel "library.next")
assertUi (hasPageLabel 2 2) "filtered updates catalog reaches page 2/2"
let filteredPage2Ids = rowsIn fullCatalog |> List.map fst
assertUi (filteredPage2Ids.Length = 12 && filteredPage2Ids |> List.forall (fun id -> (fullCatalog |> List.find (fun w -> w.Workno = id)).HasUpdate)) "filtered page 2 shows remaining 12 outdated works"
let expectedBadge = (Localization.t "library.filters.count").Replace("{0}", "1")
assertUi (string (byLabel "library.filters").Content = expectedBadge) "filter badge reflects updates filter before navigation"

let assertFilteredStatePreserved context =
    assertUi (string (byLabel "library.filters").Content = expectedBadge) (context + " preserves active filter count")
    assertUi (filtersOpen ()) (context + " preserves open filter disclosure")
    assertUi ((updatesBox ()).IsChecked = Nullable true) (context + " preserves updates filter checkbox")
    assertUi (hasPageLabel 2 2) (context + " preserves current page 2/2")
    assertUi ((rowsIn fullCatalog |> List.map fst) = filteredPage2Ids) (context + " preserves filtered and sorted page rows")

click (byLabel "nav.settings")
click (byLabel "nav.library")
assertFilteredStatePreserved "Settings -> Library"

click (byLabel "nav.downloads")
click (byLabel "nav.library")
assertFilteredStatePreserved "Downloads -> Library"

let firstFilteredCard = descendants () |> List.pick (function :? Border as b when b.Classes.Contains("work") -> Some b | _ -> None)
assertUi (firstFilteredCard.Focus(NavigationMethod.Tab)) "filtered row focuses to open detail"
window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, "\r")
window.KeyRelease(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, "\r")
pump ()
click (byLabel "common.back")
assertFilteredStatePreserved "Detail -> Library"

// Trigger per-row Update action (routes to Downloads) and return to Library.
click (buttons () |> List.find (fun b -> string b.Content = Localization.t "library.action.update" && not (b.Classes.Contains "table-heading")))
assertUi (headingExists "downloads.active" 0) "row update routes to Downloads page"
click (byLabel "nav.library")
assertFilteredStatePreserved "Row Update -> Downloads -> Library"

// Apply search + tag filter + batch Download (routes to Downloads) and return to Library.
let navSearchBox () = descendants () |> List.pick (function :? TextBox as tb when string tb.PlaceholderText = Localization.t "library.search" -> Some tb | _ -> None)
(navSearchBox ()).Text <- "WORK-04"; pump ()
click (buttons () |> List.find (fun b -> (string b.Content).EndsWith(Localization.t "library.tagFilter")))
userToggle (tagBox "school")
let searchTagIds = rowsIn fullCatalog |> List.map fst
assertUi (searchTagIds.Length = 11 && List.contains "RJ042" searchTagIds && not (List.contains "RJ002" searchTagIds)) "search + updates + school tag narrows to 11 matching works"
let firstSearchRowId, firstSearchRowBox = rowsIn fullCatalog |> List.head
userToggle firstSearchRowBox
click (buttons () |> List.find (fun b ->
    string b.Content = Localization.t "library.action.download"
    && not (b.Classes.Contains "nav")
    && not (b.Classes.Contains "table-heading")))
assertUi (headingExists "downloads.active" 0 && dbJobs () |> List.exists (fun j -> j.Workno = firstSearchRowId)) "batch download enqueues work and routes to Downloads page"
click (byLabel "nav.library")
assertUi ((navSearchBox ()).Text = "WORK-04") "batch Download -> Library preserves search text"
assertUi (string (byLabel "library.filters").Content = (Localization.t "library.filters.count").Replace("{0}", "2")) "batch Download -> Library preserves updates + tag filter count"
assertUi ((rowsIn fullCatalog |> List.map fst) = searchTagIds) "batch Download -> Library preserves filtered rows"
assertUi ((rowsIn fullCatalog |> List.find (fun (id, _) -> id = firstSearchRowId) |> snd).IsChecked = Nullable true) "batch Download -> Library preserves selection"

// Regression: Update all button queues all updatable works matching current filters across pages,
// updates its live count, disables at 0, and preserves Library state when returning from Downloads.
let updateAllBtn () =
    let format = Localization.t "library.action.updateAll"
    let prefix = format.Substring(0, format.IndexOf("{0}", StringComparison.Ordinal))
    buttons () |> List.find (fun b -> (string b.Content).StartsWith(prefix, StringComparison.Ordinal))
assertUi (string (updateAllBtn ()).Content = Fmt.fmt1 (Localization.t "library.action.updateAll") 11 && (updateAllBtn ()).IsEnabled) "Update all reflects live filtered updatable count (11)"
click (updateAllBtn ())
assertUi (headingExists "downloads.active" 0) "Update all routes to Downloads page"
let queuedFiltered = dbJobs () |> List.filter (fun j -> List.contains j.Workno searchTagIds)
let queuedFilteredUpdates = queuedFiltered |> List.filter (fun j -> j.IsUpdate)
assertUi (queuedFiltered.Length = 11 && queuedFilteredUpdates.Length = 10) "Update all enqueues remaining 10 filtered updatable works with IsUpdate without duplicating queued work"
click (byLabel "nav.library")
assertUi ((navSearchBox ()).Text = "WORK-04" && (rowsIn fullCatalog |> List.map fst) = searchTagIds) "Update all -> Library preserves search and filtered rows"

// Clear search/filters so all 32 updatable works across 2+ pages are available; verify Update all queues all 32.
(navSearchBox ()).Text <- ""; pump ()
click (buttons () |> List.find (fun b -> string b.Content = Localization.t "library.filters.reset"))
assertUi (string (updateAllBtn ()).Content = Fmt.fmt1 (Localization.t "library.action.updateAll") 32 && (updateAllBtn ()).IsEnabled) "Update all shows all 32 updatable works across all pages"
click (updateAllBtn ())
let allQueuedOutdated = dbJobs () |> List.filter (fun j -> fullCatalog |> List.exists (fun w -> w.Workno = j.Workno && w.HasUpdate))
assertUi (allQueuedOutdated.Length = 32 && (allQueuedOutdated |> List.filter (fun j -> j.IsUpdate) |> List.length) = 31) "Update all enqueues all 32 outdated works across pages without duplicates"
click (byLabel "nav.library")
// When filtered to 0 updatable works, Update all disables with count 0.
(navSearchBox ()).Text <- "WORK-001"; pump ()
assertUi (string (updateAllBtn ()).Content = Fmt.fmt1 (Localization.t "library.action.updateAll") 0 && not (updateAllBtn ()).IsEnabled) "Update all disables when 0 updatable works match"

window.Close()
Services.shared <- None
provider.DisposeAsync().AsTask().GetAwaiter().GetResult()
Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools()
Directory.Delete(root, true)
Console.WriteLine("Library UI selection checks passed.")
