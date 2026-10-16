using System.IO.Compression;
using System.Reflection;
using DlsiteClient.Core;
using DlsiteClient.Core.Api;
using DlsiteClient.Core.Models;
using DlsiteClient.Core.Services;
using DlsiteClient.Core.Storage;
using DlsiteClient.Platform;
using DlsiteClient.Platform.Linux;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

if (args.Length == 2 && args[0] == "--hold-launch-lease")
{
    using var lease = new FileStream(args[1], FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
    Console.WriteLine("LEASE_READY");
    Console.ReadLine();
    return;
}

if (args.Length == 1 && args[0] == "--credential-lookup-probe" && OperatingSystem.IsLinux())
{
    var value = await new LinuxCredentialStore(new LinuxPathConfig()).GetAsync("test-timeout");
    if (value is not null) throw new Exception("Expected no stored session");
    Console.WriteLine("CREDENTIAL_LOOKUP_EMPTY");
    return;
}

if (args.Length == 3 && args[0] == "--launch-probe" && OperatingSystem.IsLinux())
{
    new LinuxProcessLocale(() => Environment.GetEnvironmentVariable("DLSITE_TEST_PROTON"))
        .StartWithLocale(args[1], args[2], null, true);
    return;
}

var root = Path.Combine(Path.GetTempPath(), "dlsite-regression-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
int passed = 0;
void Check(bool ok, string label) { if (!ok) throw new Exception(label); passed++; Console.WriteLine("PASS " + label); }
void Write(string path, string text) { Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllText(path, text); }
var install = Path.Combine(root, "installed");
Write(Path.Combine(install, "game.exe"), "old-game");
Write(Path.Combine(install, "save", "slot1.dat"), "user-save");
Write(Path.Combine(install, "Save01.rvdata2"), "user-rpg-save");
Write(Path.Combine(install, "Data", "Actors.rvdata2"), "old-game-data");
Write(Path.Combine(install, "user.cfg"), "local-config");
Write(Path.Combine(install, "conflict.dat"), "old-conflict");
string backup;
using (var stage = new InstallStaging(install, Path.Combine(root, "recovery")))
{
    Write(Path.Combine(stage.StagePath, "game.exe"), "new-game");
    Write(Path.Combine(stage.StagePath, "save", "slot1.dat"), "default-save");
    Write(Path.Combine(stage.StagePath, "Save01.rvdata2"), "default-rpg-save");
    Write(Path.Combine(stage.StagePath, "Data", "Actors.rvdata2"), "new-game-data");
    Write(Path.Combine(stage.StagePath, "conflict.dat"), "new-conflict");
    stage.Commit();
    backup = stage.BackupPath;
    Check(File.ReadAllText(Path.Combine(install, "game.exe")) == "new-game", "new payload installed");
    Check(File.ReadAllText(Path.Combine(install, "save", "slot1.dat")) == "user-save", "save folder overrides archive defaults");
    Check(File.ReadAllText(Path.Combine(install, "Save01.rvdata2")) == "user-rpg-save", "RPG Maker saves retained");
    Check(File.ReadAllText(Path.Combine(install, "Data", "Actors.rvdata2")) == "new-game-data", "RPG Maker game data updated");
    Check(File.ReadAllText(Path.Combine(install, "user.cfg")) == "local-config", "local config absent from archive retained");
    Check(File.ReadAllText(Path.Combine(backup, "conflict.dat")) == "old-conflict", "conflicting old files retained in backup");
}
Check(Directory.Exists(backup), "backup survives staging disposal");
Check(File.ReadAllText(backup + ".origin.txt") == install, "backup records original location");
using (var stage = new InstallStaging(install, Path.Combine(root, "recovery")))
{
    Write(Path.Combine(stage.StagePath, "partial.txt"), "partial");
    using var cts = new CancellationTokenSource(); cts.Cancel();
    try { stage.Commit(cts.Token); throw new Exception("Expected cancellation"); } catch (OperationCanceledException) { }
}
Check(File.ReadAllText(Path.Combine(install, "game.exe")) == "new-game", "cancelled commit preserves working install");
Check(!File.Exists(Path.Combine(install, "partial.txt")), "partial payload never reaches installed folder");

if (OperatingSystem.IsWindows())
{
    var rollbackInstall = Path.Combine(root, "rollback-install");
    Write(Path.Combine(rollbackInstall, "game.exe"), "working-game");
    using var stage = new InstallStaging(rollbackInstall, Path.Combine(root, "rollback-recovery"));
    Write(Path.Combine(stage.StagePath, "game.exe"), "replacement");
    using (var handle = NativeLock.CreateFileW(stage.StagePath, 0x80000000, 3, IntPtr.Zero, 3, 0x02000000, IntPtr.Zero))
    {
        Check(!handle.IsInvalid, "test locks staging directory against rename");
        try { stage.Commit(); throw new Exception("Expected rename failure"); }
        catch (IOException) { }
        Check(File.ReadAllText(Path.Combine(rollbackInstall, "game.exe")) == "working-game", "failed replacement rename restores working install");
        Check(!Directory.Exists(stage.BackupPath), "rollback returns the backup to its original path");
    }
}

var services = new ServiceCollection();
services.AddLogging();
services.AddSingleton<ICredentialStore, EmptyCredentials>();
services.AddDlsiteCore(new TestPaths(Path.Combine(root, "app")));
await using var provider = services.BuildServiceProvider();
var db = provider.GetRequiredService<LibraryDatabase>();
var settings = provider.GetRequiredService<SettingsService>();
var states = new[] { DownloadJobState.Paused, DownloadJobState.Failed, DownloadJobState.Completed, DownloadJobState.CorruptArchive, DownloadJobState.Queued };
// Seed the next queue instance from the isolated database, without any login or network call.
db.SaveDownloadJobs(states.Select((state, i) => new DownloadJob { Workno = "RJ" + i, State = state, QueueOrder = i, BytesDownloaded = 123, ProgressPercent = 42, ErrorMessage = "old" }));
await using var resumed = ActivatorUtilities.CreateInstance<DownloadQueueService>(provider);
resumed.PauseForAuth();
resumed.ResumeAll();
var jobs = db.LoadDownloadJobs();
Check(jobs[0].State == DownloadJobState.Queued && jobs[1].State == DownloadJobState.Queued, "resume all persists paused and failed jobs");
Check(jobs[2].State == DownloadJobState.Completed && jobs[3].State == DownloadJobState.CorruptArchive, "resume all leaves completed and corrupt jobs unchanged");
Check(jobs.All(j => j.BytesDownloaded == 123 && j.ProgressPercent == 42), "resume all preserves partial progress");
Check(jobs.Select(j => j.QueueOrder).SequenceEqual(Enumerable.Range(0, 5)) && resumed.IsPausedForAuth, "resume all preserves order and auth gate");
resumed.Enqueue(new[] { new WorkItem { Workno = "RJ2", WorkName = "Update" } }, true);
Check(db.LoadDownloadJobs().Count == 5, "completed work can be queued without duplicate key");

var extractor = provider.GetRequiredService<ArchiveExtractor>();
Check(extractor.ResolveSevenZip() != null, "platform 7-Zip tool found");
var work = new WorkItem { Workno = "RJTEST", WorkName = "Changed title", MakerName = "Maker", IsDownloaded = true, InstallPath = install, UpdateDate = "2026-10-16 00:00:00" };
db.UpsertWorks(new[] { work });
var cache = Path.Combine(settings.Current.CacheDirectory, work.Workno);
Directory.CreateDirectory(cache);
var archive = Path.Combine(cache, "payload.zip");
File.WriteAllText(archive, "broken archive");
var extractMethod = typeof(DownloadQueueService).GetMethod("ExtractWorkAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
await (Task)extractMethod.Invoke(resumed, new object[] { work.Workno, CancellationToken.None })!;
Check(File.ReadAllText(Path.Combine(install, "game.exe")) == "new-game", "failed real extraction preserves installed data");
Check(db.GetWork(work.Workno)!.IsDownloaded && File.Exists(archive), "failed extraction preserves install state and archive");
File.Delete(archive);
using (var zip = ZipFile.Open(archive, ZipArchiveMode.Create))
{
    var entry = zip.CreateEntry("game.exe"); using var writer = new StreamWriter(entry.Open()); writer.Write("newest-game");
}
using (var cancelled = new CancellationTokenSource())
{
    cancelled.Cancel();
    try
    {
        await (Task)extractMethod.Invoke(resumed, new object[] { work.Workno, cancelled.Token })!;
        throw new Exception("Expected cancelled extraction");
    }
    catch (OperationCanceledException) { }
}
Check(File.ReadAllText(Path.Combine(install, "game.exe")) == "new-game", "cancelled real extraction preserves installed data");
Check(db.GetWork(work.Workno)!.IsDownloaded && File.Exists(archive), "cancelled extraction preserves install state and archive");
await (Task)extractMethod.Invoke(resumed, new object[] { work.Workno, CancellationToken.None })!;
Check(File.ReadAllText(Path.Combine(install, "game.exe")) == "newest-game", "real extraction updates recorded imported path");
Check(File.ReadAllText(Path.Combine(install, "save", "slot1.dat")) == "user-save", "real extraction retains user save");
Check(db.GetWork(work.Workno)!.LocalUpdateDate == work.UpdateDate && !Directory.Exists(cache), "revision and cache change only after commit");

var launcher = new LauncherService(db, new NoProcess(), NullLogger<LauncherService>.Instance);
var candidates = Path.Combine(root, "candidates");
Write(Path.Combine(candidates, "GAME.EXE"), "exe");
Write(Path.Combine(candidates, "uninstall.exe"), "exe");
Check(launcher.FindExecutables(candidates).Count == 1, "EXE discovery handles uppercase and excludes uninstallers");
if (OperatingSystem.IsLinux())
{
    var previousConfigHome = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
    try
    {
        var freshConfigHome = Path.Combine(root, "new-config-home");
        Environment.SetEnvironmentVariable("XDG_CONFIG_HOME", freshConfigHome);
        Check(new LinuxPathConfig().AppDataRoot == Path.Combine(freshConfigHome, "dlsite-client") && !Directory.Exists(freshConfigHome),
            "first run resolves an absent XDG config directory without using the working directory");
    }
    finally { Environment.SetEnvironmentVariable("XDG_CONFIG_HOME", previousConfigHome); }
    var previousDataHome = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
    try
    {
        var freshDataHome = Path.Combine(root, "new-data-home");
        Environment.SetEnvironmentVariable("XDG_DATA_HOME", freshDataHome);
        var freshLauncher = new LauncherService(db, new NoProcess(), NullLogger<LauncherService>.Instance);
        Check(freshLauncher.LaunchLockDirectory == Path.Combine(freshDataHome, "dlsite-client", "launch-locks") && !Directory.Exists(freshDataHome),
            "first launch resolves an absent XDG data directory without using the working directory");
    }
    finally { Environment.SetEnvironmentVariable("XDG_DATA_HOME", previousDataHome); }
    var runtimeVariables = LinuxHostEnvironment.Variables.Concat(new[] { "DLSITE_BUNDLED_RUNTIME", "APPDIR", "APPIMAGE" }).ToArray();
    var savedEnvironment = runtimeVariables.Concat(LinuxHostEnvironment.Variables.Select(v => "DLSITE_HOST_" + v))
        .ToDictionary(v => v, Environment.GetEnvironmentVariable);
    try
    {
        Environment.SetEnvironmentVariable("DLSITE_BUNDLED_RUNTIME", "1");
        foreach (var variable in LinuxHostEnvironment.Variables)
        {
            Environment.SetEnvironmentVariable(variable, "/private/runtime");
            Environment.SetEnvironmentVariable("DLSITE_HOST_" + variable, "__DLSITE_UNSET__");
        }
        Environment.SetEnvironmentVariable("DLSITE_HOST_PATH", "/usr/bin:/bin");
        Environment.SetEnvironmentVariable("DLSITE_HOST_LD_LIBRARY_PATH", "/host/lib");
        var process = new System.Diagnostics.ProcessStartInfo("game");
        LinuxHostEnvironment.Restore(process);
        Check(process.Environment["PATH"] == "/usr/bin:/bin" && process.Environment["LD_LIBRARY_PATH"] == "/host/lib", "game launch restores host loader paths");
        Check(!process.Environment.ContainsKey("WEBKIT_EXEC_PATH") && !process.Environment.ContainsKey("GIO_MODULE_DIR") && !process.Environment.ContainsKey("DLSITE_BUNDLED_RUNTIME"), "game launch removes private WebKit environment");
    }
    finally { foreach (var pair in savedEnvironment) Environment.SetEnvironmentVariable(pair.Key, pair.Value); }
    var probeDir = Path.Combine(root, "runner-probes");
    Directory.CreateDirectory(probeDir);
    var probeResult = Path.Combine(probeDir, "result");
    var probe = Path.Combine(probeDir, "wine");
    Write(probe, "#!/bin/sh\nprintf '%s\\n' \"$WINEPREFIX\" \"$1\" \"$LANG\" > '" + probeResult + "'\n");
    File.SetUnixFileMode(probe, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
    var oldPath = Environment.GetEnvironmentVariable("PATH");
    var oldSteam = Environment.GetEnvironmentVariable("STEAM_COMPAT_CLIENT_INSTALL_PATH");
    try
    {
        Environment.SetEnvironmentVariable("PATH", probeDir);
        // A private fake Steam root gives deterministic discovery without installed games.
        var steamRoot = Path.Combine(probeDir, "Steam"); Directory.CreateDirectory(steamRoot);
        Environment.SetEnvironmentVariable("STEAM_COMPAT_CLIENT_INSTALL_PATH", steamRoot);
        var processLocale = new LinuxProcessLocale();
        processLocale.StartWithLocale("GAME.EXE", probeDir, null, true);
        for (var i=0; i<100 && !File.Exists(probeResult); i++) await Task.Delay(20);
        var result = File.ReadAllLines(probeResult);
        Check(result[0].EndsWith("/wine") && !result[0].StartsWith(probeDir) && result[1] == "GAME.EXE" && result[2] == "ja_JP.UTF-8", "Wine uses persistent isolated prefix and Japanese locale");
        File.Delete(probeResult);
        var custom = Path.Combine(steamRoot, "compatibilitytools.d", "GE-Proton-test", "proton");
        Write(custom, "#!/bin/sh\nprintf '%s\\n' \"$STEAM_COMPAT_DATA_PATH\" \"$1\" \"$2\" > '" + probeResult + "'\n");
        File.SetUnixFileMode(custom, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        processLocale.StartWithLocale("GAME.EXE", probeDir, null, false);
        for (var i=0; i<100 && !File.Exists(probeResult); i++) await Task.Delay(20);
        result = File.ReadAllLines(probeResult);
        Check(result[1] == "run" && result[2] == "GAME.EXE", "custom Proton discovery launches selected game");
        var protonPrefix = result[0];
        File.Delete(probeResult);
        var external = Path.Combine(probeDir, "external library");
        var externalProton = Path.Combine(external, "steamapps", "common", "Proton-test", "proton");
        Directory.CreateDirectory(Path.GetDirectoryName(externalProton)!);
        File.Move(custom, externalProton);
        Write(Path.Combine(steamRoot, "steamapps", "libraryfolders.vdf"), "\"libraryfolders\"\n{\n\"1\" {\n\"path\" \"" + external + "\"\n}\n}\n");
        processLocale.StartWithLocale("GAME.EXE", probeDir, null, false);
        for (var i=0; i<100 && !File.Exists(probeResult); i++) await Task.Delay(20);
        Check(File.ReadAllLines(probeResult)[1] == "run", "Proton discovery reads external Steam library paths with spaces");
        File.Delete(probeResult);
        File.Copy(probe, Path.Combine(probeDir, "umu-run"));
        processLocale.StartWithLocale("GAME.EXE", probeDir, null, false);
        for (var i=0; i<100 && !File.Exists(probeResult); i++) await Task.Delay(20);
        result = File.ReadAllLines(probeResult);
        Check(result[0] == protonPrefix && result[1] == "GAME.EXE", "umu and direct Proton share compatibility data without nested prefixes");
        File.Delete(Path.Combine(probeDir, "umu-run"));
        File.Delete(probeResult);
        Write(Path.Combine(Path.GetDirectoryName(externalProton)!, "toolmanifest.vdf"), "\"require_tool_appid\" \"1234\"\n");
        try { processLocale.StartWithLocale("GAME.EXE", probeDir, null, false); throw new Exception("Expected missing runtime"); }
        catch (InvalidOperationException ex) when (ex.Message == "steam-runtime-missing") { }
        Check(!File.Exists(probeResult), "missing required Steam runtime prevents an invalid direct Proton launch");
        Write(Path.Combine(external, "steamapps", "appmanifest_1234.acf"), "\"installdir\" \"SteamLinuxRuntime_test\"\n");
        var runtime = Path.Combine(external, "steamapps", "common", "SteamLinuxRuntime_test", "_v2-entry-point");
        Write(runtime, "#!/bin/sh\nprintf '%s\\n' \"$1\" \"$2\" \"$3\" \"$4\" \"$5\" \"$STEAM_COMPAT_INSTALL_PATH\" \"$STEAM_COMPAT_MOUNTS\" > '" + probeResult + "'\n");
        File.SetUnixFileMode(runtime, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        processLocale.StartWithLocale("GAME.EXE", probeDir, null, false);
        for (var i=0; i<100 && !File.Exists(probeResult); i++) await Task.Delay(20);
        result = File.ReadAllLines(probeResult);
        Check(result[0] == "--verb=waitforexitandrun" && result[2] == externalProton && result[4] == "GAME.EXE", "direct Proton uses the runtime specified by its manifest");
        Check(result[5] == probeDir && result[6].Split(':').Contains(probeDir) && result[6].Split(':').Contains(Path.GetDirectoryName(externalProton)!), "direct runtime exposes external game and Proton directories");
    }
    finally { Environment.SetEnvironmentVariable("PATH", oldPath); Environment.SetEnvironmentVariable("STEAM_COMPAT_CLIENT_INSTALL_PATH", oldSteam); }
    var secretTool = Path.Combine(probeDir, "secret-tool-test");
    Write(secretTool, "#!/bin/sh\necho test-store-failure >&2\nexit 1\n");
    File.SetUnixFileMode(secretTool, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
    var oldSecretTool = Environment.GetEnvironmentVariable("DLSITE_SECRET_TOOL");
    try
    {
        Environment.SetEnvironmentVariable("DLSITE_SECRET_TOOL", secretTool);
        var secretPaths = new TestPaths(Path.Combine(root, "secret-store"));
        var credentials = new LinuxCredentialStore(secretPaths);
        try { await credentials.SaveAsync("test", new string('x', 1024 * 1024)); throw new Exception("Expected secret store failure"); }
        catch (InvalidOperationException ex) when (ex.Message == LinuxCredentialStore.UnavailableErrorCode) { }
        Check(!Directory.Exists(secretPaths.AppDataRoot), "early-exit Secret Service save fails cleanly without a plaintext fallback");
        Check(await credentials.GetAsync("test") is null, "missing Secret Service item leaves the app signed out");
    }
    finally { Environment.SetEnvironmentVariable("DLSITE_SECRET_TOOL", oldSecretTool); }
    var native = Path.Combine(candidates, "game-native"); Write(native, "\u007fELFpayload");
    var script = Path.Combine(candidates, "start.sh"); Write(script, "#!/bin/sh\nexit 0\n");
    File.SetUnixFileMode(native, UnixFileMode.UserRead | UnixFileMode.UserExecute);
    File.SetUnixFileMode(script, UnixFileMode.UserRead | UnixFileMode.UserExecute);
    Write(Path.Combine(candidates, "not-executable"), "\u007fELFpayload");
    Check(launcher.FindExecutables(candidates).Count == 3, "Linux discovers ELF and scripts with execute permission only");
    var image = Path.Combine(root, "test % $ image.AppImage"); Write(image, "test");
    var oldImage = Environment.GetEnvironmentVariable("APPIMAGE");
    var oldData = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
    var oldConfig = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
    try
    {
        Environment.SetEnvironmentVariable("APPIMAGE", image);
        Environment.SetEnvironmentVariable("XDG_DATA_HOME", Path.Combine(root, "xdg-data"));
        Environment.SetEnvironmentVariable("XDG_CONFIG_HOME", Path.Combine(root, "xdg-config"));
        new LinuxProtocolHandler().Register(AppProtocol.Scheme, "/tmp/.mount/old-executable");
        var registration = new LinuxStartupRegistration(); registration.SetEnabled(true);
        var protocol = File.ReadAllText(Path.Combine(LinuxDesktopEntry.DataHome, "applications", "dlsite-client.desktop"));
        var autostart = File.ReadAllText(Path.Combine(LinuxDesktopEntry.ConfigHome, "autostart", "dlsite-client.desktop"));
        Check(protocol.Contains("test %%") && !protocol.Contains(".mount"), "protocol uses persistent AppImage and escapes field codes");
        var mime = File.ReadAllText(Path.Combine(LinuxDesktopEntry.ConfigHome, "mimeapps.list"));
        Check(mime.Contains("x-scheme-handler/" + AppProtocol.Scheme + "=dlsite-client.desktop"), "protocol registers MIME association in a new XDG config directory");
        Check(autostart.Contains("test %%") && !autostart.Contains(".mount"), "autostart uses persistent AppImage");
    }
    finally
    {
        Environment.SetEnvironmentVariable("APPIMAGE", oldImage);
        Environment.SetEnvironmentVariable("XDG_DATA_HOME", oldData);
        Environment.SetEnvironmentVariable("XDG_CONFIG_HOME", oldConfig);
    }
}
var credentialApi = provider.GetRequiredService<DlsiteApiClient>();
var failingAuth = new AuthSession(credentialApi, new FailingCredentials(), NullLogger<AuthSession>.Instance);
try { await failingAuth.ApplyLoginAsync("test-session", null); throw new Exception("Expected credential failure"); }
catch (IOException) { }
Check(!credentialApi.IsLoggedIn && failingAuth.LoginId is null, "failed credential save does not commit login state");
var oidcApi = new DlsiteApiClient(new LoginHttpFactory(), NullLogger<DlsiteApiClient>.Instance);
var oidcAuth = new AuthSession(oidcApi, new FailingCredentials(), NullLogger<AuthSession>.Instance);
try { await oidcAuth.LoginWithOidcAsync("test-token", "test-state", "test-session-state"); throw new Exception("Expected OIDC credential failure"); }
catch (IOException) { }
Check(!oidcApi.IsLoggedIn, "OIDC exchange does not publish a session before credential storage succeeds");
// Tag data is independent of installed state and public metadata failures are soft.
var tagHttp = new TagHttpFactory();
var tagApi = new DlsiteApiClient(tagHttp, NullLogger<DlsiteApiClient>.Instance);
var tagSettings = provider.GetRequiredService<SettingsService>();
var tagWorks = new[] {
    new WorkItem { Workno="TAGAPI", WorkName="API", SiteId="maniax", IsDownloaded=true, InstallPath=install, UpdateDate="2026-01-01", LocalUpdateDate="2025-01-01" },
    new WorkItem { Workno="TAGHTML", WorkName="HTML", SiteId="home" },
    new WorkItem { Workno="TAGBLOCK", WorkName="blocked", SiteId="maniax" }
};
db.UpsertWorks(tagWorks);
var tagLibrary = new LibraryService(db, tagApi, new AuthSession(tagApi,new EmptyCredentials(),NullLogger<AuthSession>.Instance),tagSettings,NullLogger<LibraryService>.Instance);
await tagLibrary.EnsureTagsAsync("TAGAPI");
Check(LibraryService.ParseTags(db.GetWork("TAGAPI")!.TagsJson).SequenceEqual(new[]{"school"}), "store-section product.json genres are cached");
Check(db.GetWork("TAGAPI")!.InstallPath==install && db.GetWork("TAGAPI")!.IsDownloaded, "tag save preserves install state");
var calls = tagHttp.Calls;
await tagLibrary.EnsureTagsAsync("TAGAPI");
Check(tagHttp.Calls==calls, "cached tags are not fetched again");
await tagLibrary.EnsureTagsAsync("TAGHTML");
Check(LibraryService.ParseTags(db.GetWork("TAGHTML")!.TagsJson).SequenceEqual(new[]{"A & B"}), "HTML fallback decodes genre text and excludes unrelated links");
await tagLibrary.EnsureTagsAsync("TAGBLOCK");
calls = tagHttp.Calls;
await tagLibrary.EnsureTagsAsync("TAGBLOCK");
Check(db.GetWork("TAGBLOCK")!.TagsJson=="[]" && tagHttp.Calls==calls, "blocked items remain blank and are attempted once per session");
Check(tagHttp.Paths.All(p=> !p.StartsWith("/api/")), "public metadata URLs include the work store section");
Check(tagLibrary.Query(MediaFilter.All,null,false,false,false,"school",updatesOnly:true).Select(w=>w.Workno).SequenceEqual(new[]{"TAGAPI"}), "update and tag filters combine");
Check(tagLibrary.Query(MediaFilter.All,null,false,false,false,"scho").Count==0, "tag filtering matches whole tags");
Check(ProductPageTags.Parse("<div class='main_genre'>1 GB</div><a href='/genre/9'>outside</a>").Count==0, "HTML ignores size blocks and page navigation");

// Delayed start, process exit, duplicate requests and shared launch leases.
var gameFile = Path.Combine(install,"game.exe");
Write(gameFile,"synthetic");
db.UpsertWorks(new[]{new WorkItem{Workno="LAUNCH",InstallPath=install,SelectedExe=gameFile}});
var gameProcesses = new FakeGames();
var gameStarter = new CountingStarter();
LauncherService NewLauncher() => new(db,gameStarter,NullLogger<LauncherService>.Instance,gameProcesses) {
    PollInterval=TimeSpan.FromMilliseconds(5), StartTimeout=TimeSpan.FromMilliseconds(200), LaunchLockDirectory=Path.Combine(root,"launch-locks") };
var launches = NewLauncher();
Check(launches.TryBeginPreparation("LAUNCH") && !launches.TryBeginPreparation("LAUNCH"), "duplicate preparation is rejected");
launches.ConfirmAndLaunch("LAUNCH",gameFile,false,false);
launches.ConfirmAndLaunch("LAUNCH",gameFile,false,false);
Check(gameStarter.Calls==1 && launches.GetLaunchState("LAUNCH")==GameLaunchState.Starting, "slow start stays pending and duplicate launch is rejected");
var secondLauncher=NewLauncher();
secondLauncher.ConfirmAndLaunch("LAUNCH",gameFile,false,false);
Check(gameStarter.Calls==1, "executable lease prevents a second launcher service from launching again");
gameProcesses.Running=true;
for(var i=0;i<100 && launches.GetLaunchState("LAUNCH")!=GameLaunchState.Running;i++) await Task.Delay(5);
Check(launches.GetLaunchState("LAUNCH")==GameLaunchState.Running, "process detection ends starting status");
launches.ConfirmAndLaunch("LAUNCH",gameFile,false,false);
Check(gameStarter.Calls==1, "running game cannot be launched twice");
gameProcesses.Running=false;
for(var i=0;i<100 && (launches.GetLaunchState("LAUNCH")!=GameLaunchState.Idle || secondLauncher.GetLaunchState("LAUNCH")!=GameLaunchState.Idle);i++) await Task.Delay(5);
Check(launches.GetLaunchState("LAUNCH")==GameLaunchState.Idle, "process exit clears launch state");
launches.ConfirmAndLaunch("LAUNCH",gameFile,false,false);
for(var i=0;i<100 && launches.GetLaunchState("LAUNCH")!=GameLaunchState.Failed;i++) await Task.Delay(5);
Check(launches.GetLaunchError("LAUNCH")=="launcher.startTimeout" && gameStarter.Calls==2, "missing process times out and releases the previous lease");
gameStarter.Throw=true;
try { launches.ConfirmAndLaunch("LAUNCH",gameFile,false,false); throw new Exception("Expected launch failure"); }
catch(IOException) { }
Check(launches.GetLaunchState("LAUNCH")==GameLaunchState.Failed, "launch exception clears pending state");
Check(new GameProcesses().IsRunning(Environment.ProcessPath!), "real process monitor finds the current executable by name and path");
var leaseKey=OperatingSystem.IsWindows() ? Path.GetFullPath(gameFile).ToUpperInvariant() : Path.GetFullPath(gameFile);
var leaseName=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(leaseKey)))+".lock";
var leaseStart=new System.Diagnostics.ProcessStartInfo(Environment.ProcessPath!) { RedirectStandardInput=true, RedirectStandardOutput=true, UseShellExecute=false };
leaseStart.ArgumentList.Add("--hold-launch-lease");
leaseStart.ArgumentList.Add(Path.Combine(root,"launch-locks",leaseName));
using(var holder=System.Diagnostics.Process.Start(leaseStart)!)
{
    try
    {
        var ready=await holder.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(5));
        if(ready!="LEASE_READY") throw new Exception("Lease helper did not start");
        gameStarter.Throw=false;
        var previousCalls=gameStarter.Calls;
        var crossProcessLauncher=NewLauncher();
        crossProcessLauncher.ConfirmAndLaunch("LAUNCH",gameFile,false,false);
        Check(gameStarter.Calls==previousCalls, "separate process lease prevents duplicate launch across app instances");
    }
    finally
    {
        await holder.StandardInput.WriteLineAsync("release");
        await holder.StandardInput.FlushAsync();
        await holder.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
    }
}
Console.WriteLine($"{passed} checks passed. Test data: {root}");

sealed record TestPaths(string AppDataRoot) : IPathConfig
{
    public string DefaultLibraryDirectory => Path.Combine(AppDataRoot, "library");
    public string DefaultCacheDirectory => Path.Combine(AppDataRoot, "cache");
    public string DefaultLogDirectory => Path.Combine(AppDataRoot, "logs");
}
sealed class EmptyCredentials : ICredentialStore
{
    public Task SaveAsync(string k, string v, CancellationToken ct = default) => Task.CompletedTask;
    public Task<string?> GetAsync(string k, CancellationToken ct = default) => Task.FromResult<string?>(null);
    public Task DeleteAsync(string k, CancellationToken ct = default) => Task.CompletedTask;
}
sealed class FailingCredentials : ICredentialStore
{
    public Task SaveAsync(string k, string v, CancellationToken ct = default) => Task.FromException(new IOException("Test store failure"));
    public Task<string?> GetAsync(string k, CancellationToken ct = default) => Task.FromResult<string?>(null);
    public Task DeleteAsync(string k, CancellationToken ct = default) => Task.CompletedTask;
}
sealed class LoginHttpFactory : IHttpClientFactory
{
    public HttpClient CreateClient(string name) => new(new LoginHttpHandler());
}
sealed class LoginHttpHandler : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
        Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
        { Content = new StringContent("{\"meta\":{\"code\":200},\"data\":{\"session_id\":\"test-session\",\"login_id\":\"test-user\"}}", System.Text.Encoding.UTF8, "application/json") });
}
sealed class NoProcess : IProcessLocale
{
    public void StartWithLocale(string p, string w, string? l, bool j) => throw new Exception("Tests must not launch games");
}

static class NativeLock
{
    [System.Runtime.InteropServices.DllImport("kernel32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode, SetLastError = true)]
    public static extern Microsoft.Win32.SafeHandles.SafeFileHandle CreateFileW(string name, uint access, uint share,
        IntPtr security, uint creation, uint flags, IntPtr template);
}

sealed class TagHttpFactory : IHttpClientFactory
{
    public int Calls;
    public List<string> Paths {get;}=[];
    public HttpClient CreateClient(string name)=>new(new TagHttpHandler(this));
}
sealed class TagHttpHandler(TagHttpFactory owner) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)
    {
        owner.Calls++; owner.Paths.Add(request.RequestUri!.AbsolutePath);
        var url=request.RequestUri.ToString();
        if(url.Contains("TAGBLOCK")) return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.Forbidden));
        var json=url.Contains("product.json");
        var text=json ? (url.Contains("TAGAPI") ? "[{\"genres\":[{\"name\":\"school\"}]}]" : "[]") :
            "<a href='/genre/8'>navigation</a><div class='main_genre'><a href='/home/fsr/=/genre/12'>A &amp; <b>B</b></a><a href='/maker/x'>maker</a></div>";
        return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK){Content=new StringContent(text)});
    }
}
sealed class FakeGames : IGameProcesses
{
    public volatile bool Running;
    public bool IsRunning(string path)=>Running;
}
sealed class CountingStarter : IProcessLocale
{
    public int Calls;
    public bool Throw;
    public void StartWithLocale(string p,string w,string? l,bool j) { if(Throw) throw new IOException("test start failure"); Interlocked.Increment(ref Calls); }
}
