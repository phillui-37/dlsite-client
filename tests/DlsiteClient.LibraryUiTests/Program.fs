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
AppBuilder.Configure<App>().UseHeadless(AvaloniaHeadlessPlatformOptions()).SetupWithoutStarting() |> ignore
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
let byLabel key = buttons () |> List.find (fun b -> string b.Content = Localization.t key)
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
let updatesBox () = descendants () |> List.pick (function :? CheckBox as cb when string cb.Content = Localization.t "library.filter.updates" -> Some cb | _ -> None)
userToggle (updatesBox ())
assertUi ((rows ()).Length = 12 && rows () |> List.forall (fun (id,_) -> (works |> List.find (fun w -> w.Workno = id)).HasUpdate)) "update filter resets paging and shows only outdated installs"
click (byLabel "library.filters")
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
assertUi (descendants () |> List.exists (function :? ProgressBar as p -> p.IsIndeterminate && p.Classes.Contains("circular") | _ -> false)) "Start shows a circular loading indicator"
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
window.Close()
Services.shared <- None
provider.DisposeAsync().AsTask().GetAwaiter().GetResult()
Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools()
Directory.Delete(root, true)
Console.WriteLine("Library UI selection checks passed.")
