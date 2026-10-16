# Decisions (ADR)

## ADR-001: Stack = Avalonia.FuncUI F# + C# Core + .NET 10 AOT

- **Status:** Accepted
- **Why:** Cross-platform UI; Nest affinity in C#; FuncUI avoids XAML AOT pain; competitive with Tauri on RAM for dense lists
- **Versions (2026-10-15):** Avalonia **12.1.4** + FuncUI **2.0.0** + WebView **12.1.1** (MIT) + Material.Avalonia **3.21.0**; Microsoft.Extensions / Sqlite **10.0.13**
- **AOT smoke (2026-10-15):** **PASS** after VS 2026 C++ Desktop tools. Publish with `vcvars64.bat` then `-p:PublishAot=true -r win-x64`. Output: `artifacts/aot-win-x64/`.
- **JSON:** `AppJsonContext` source-gen for settings + Nest DTOs (reflection JSON is disabled under AOT — mode/settings save crashed without it).
- **Fallback:** If linker missing → trimmed self-contained (`PublishAot=false`).

## ADR-002: Linux / Proton (M5)

- **Status:** Accepted (updated M5)
- Windows remains primary ship target; Linux is supported via `DlsiteClient.Platform.Linux`
- **Secrets:** FreeDesktop Secret Service via `secret-tool` when available; otherwise XDG data file mode `0600` (documented degraded fallback)
- **Windows games on Linux:** launch `.exe` through Proton (preferred) or Wine; optional `ProtonPath` in settings
- **Distribution:** AppImage (see `docs/PACKAGING.md`)

## ADR-003: MD is source of truth

- Chat disposable; update docs before code

## ADR-004: No Nest credential import

- Only `libraryDirectory` / `cacheDirectory` with confirmation

## ADR-005: Rating display

- Store rating as text `x/5`; map from API `point` after verify; default `0/5`; no star glyphs; no personal rating UI

## ADR-006: No Viewer / DLsite Play

## ADR-007: Multi-select = Download + Update + Uninstall

## ADR-008: Extract integrity

- On extract fail: `7z t`; corrupt → re-download; test OK → retry extract

## ADR-009: i18n

- ja + zh-TW in `locales/*.json`; no UI literals in code

## ADR-010: Logging

- User: level + path; rotation/retention fixed in app

## ADR-011: No F# sprintf under Native AOT

- **Status:** Accepted (2026-10-15)
- F# `sprintf`/`printf`/`%A` crash AOT via `PrintfImpl` reflection. Use `DlsiteClient.App.Fmt` / `String.Format`.

## ADR-012: Packaging

- Windows: AOT folder + **MSI** (WiX, per-user or all users; UAC only for all users) wizard — `scripts/publish-msi.ps1`
- Linux: **AppImage** (M5) — `scripts/publish-appimage.sh`; see `docs/PACKAGING.md`

## ADR-013: OAuth = Avalonia WebAuthenticationBroker (MIT WebView)

- **Status:** Accepted (M1), corrected 2026-10-15
- Match Nest: `client_id=dlsite.user`, fragment redirect `https://www.dlsite.com/home/login`
- **License:** `Avalonia.Controls.WebView` **≥ 11.4.0 / 12.0.0** is FOSS (MIT). **11.3.x is legacy Accelerate-only** — do not use.
- Package pin: `Avalonia.Controls.WebView` **12.1.1** (aligned with Avalonia 12.1.x).
- API: `WebAuthenticationBroker.AuthenticateAsync` — WebView2 on Windows (`WebAuthenticatorMode.NativeWebDialog`; Nest redirect is not loopback, so Browser mode is out).
- **STA:** mark `Program.main` with `[<STAThread>]` (and `TrySetApartmentState(STA)`). Without it, .NET leaves main as MTA and WebView2 `CreateBuilder` throws `COMException 0x80010106` (`RPC_E_CHANGED_MODE`).
- **UI thread:** call `AuthenticateAsync` without blocking (no `.GetResult()` / `.Wait()` on the UI thread). Blocking opens an empty login window and freezes the app — the broker needs the dispatcher to paint and navigate.
- Manual SID login kept as debug fallback only.

## ADR-014: UI theme = Material.Avalonia

- **Status:** Accepted
- Package: `Material.Avalonia` 3.21.0 — Light / Primary Blue / Secondary Teal (not Fluent)

## ADR-015: Japan locale launch = soft env hint

- **Status:** Accepted (M4)
- Per-work flag sets child `LANG` / `LC_ALL` to `ja_JP.UTF-8`.
- Full Windows AppLocale / Locale Emulator is out of scope (OS-dependent, not FOSS-bundled).
- Protocol `dlsite-client://play/{workno}`: launch immediately when exe is known; if multi-exe needs a pick, open the work detail picker instead of failing closed.

## ADR-016: UI never blocks on IO

- **Status:** Accepted (post-M4 hotfix)
- Root cause of library freeze after login: sync thumbnail HTTP via `.GetResult()` while building hundreds of rows on the UI thread.
- Rule: all network/disk/credential/sync IO goes through `UiAsync` (or existing background services). Views only bind state and post UI updates.
- Library paging default **20** (options 50/100). Settings apply on change (no Confirm). Filters collapsed until expanded. Work-type labels from Nest API + locale fallback.

## ADR-017: Nest library folder layout

- **Status:** Accepted (2026-10-15), corrected same day
- Nest `libraryDirectory` (e.g. `D:\DLNest`) is the **parent** of content. Observed tree: `{libraryDirectory}/Game/{makerName}/{workName}/` (cache sibling dirs ignored).
- Detect installs under `Game/` and also at the library root (`{maker}/{work}`, `{workno}`) for older / non-Nest trees.
- Match purchase `MakerName` + `WorkName` (case-insensitive on Windows; strip invalid path chars).
- New extracts: if `{libraryDirectory}/Game` exists, write `{Game}/{maker}/{work}`; else `{libraryDirectory}/{maker}/{work}`.
- Stale `IsDownloaded` cleared when no matching folder remains.
- **Local revision:** do not set `LocalUpdateDate = UpdateDate` on detect (that hid Nest updates). Infer from install-folder mtime; `HasUpdate` is remote `update_date` **after** local stamp. Extracts still stamp `UpdateDate` on success.

## ADR-018: Session expiry handling (M6)

- **Status:** Accepted (M6)
- Detect Nest auth failure on authed calls: HTTP **401/403**, or JSON `meta.code` **401/403**.
- Raise `NestSessionExpiredException` → `AuthSession.InvalidateLocalSessionAsync` clears in-memory SID + OS credential store (no Nest DELETE; session already dead).
- `DownloadQueueService` sets an auth-pause gate (skips pump, cancels active downloads); jobs stay **Queued** so they resume after re-login. User-**Paused** jobs stay paused.
- UI shows a re-login banner (`NeedsRelogin`) until OAuth/SID login succeeds.

## ADR-019: Compact library shell

- Keep Material.Avalonia and the locked stack. Use a dark navigation rail with light content surfaces and blue actions.
- Keep controls usable at 1280 × 800 (Steam Deck desktop). Advanced filters start collapsed.
- Share horizontal scrolling between table header and rows.

## ADR-020: Keep installed data during updates

- Extract into staging on the library filesystem. Commit only after successful extraction. Move the old installation to a retained backup before moving staging into place; restore it if the second move fails.
- Copy local files absent from the archive and known save files into staging. Keep all original files in the retained backup, including conflicts. Backups are outside the game directory and are not cache.
- MSI uses the maintained WiX UI library instead of hand-written progress/error dialogs.

## ADR-021: Private Linux login runtime and game isolation

- Bundle WebKitGTK with dependency closure and GTK resources in AppImage. Build on a fixed Linux baseline; retain package provenance and licenses.
- AppRun records the original host environment before adding private runtime paths. Game and desktop-tool processes restore it.
- Use umu-run when installed to provide the Steam runtime for Proton outside Steam. Retain direct Proton and Wine fallback. All paths use stable per-install compatibility data outside the install tree.
- Linux authentication uses Secret Service through a bundled secret-tool. Missing or locked credential service is a login error, not a successful session save.
- umu WINEPREFIX is the compatibility data root, matching direct Proton STEAM_COMPAT_DATA_PATH; Proton creates pfx inside it. Direct modern Proton uses the required installed Steam runtime entry point.
- umu can create pfx as a self symlink. Create a real pfx directory first so both umu and direct Proton retain the standard Steam compatibility layout and metadata.

VM validation found that private Mesa can make GLX context creation succeed while the framebuffer window stays black. On Linux without a DRM render node, select Avalonia X11 software rendering explicitly. Keep normal rendering on hardware with a render node.

Direct Proton runtime launches expose the game directory, compatibility data, Proton tool directory and runtime directory through STEAM_COMPAT_MOUNTS. Preserve existing mount entries. This supports libraries outside the home directory, including removable storage.

## ADR-022: Compile platform integration by target

Use the SDK WINDOWS symbol for net10.0-windows. Include NestImportPrompt.fs and the Windows platform project only in that target. Compile the matching platform service wiring and Nest settings action with F# preprocessor conditions. The neutral net10.0 app target is the Linux build and references only the Linux platform implementation. Remove its unused Nest importer stub. Shared HTTP authentication and normal local-library discovery remain cross-platform.

## ADR-023: Tag fallback and game-start tracking

Use the work's store section for public product.json. Parse genres first, then only genre links in product HTML main_genre blocks. This small parser uses bounded regex operations and no new runtime package. Missing, blocked or invalid metadata leaves tags blank. A semaphore serializes requests; successful and empty results are cached for the session. Persist only tags_json so an asynchronous metadata response cannot replace installation state.

Use LauncherService state events for the circular start indicator. Match the selected executable by process name and path, including Wine argv paths. A file lease keyed by executable path prevents another app instance from starting the same target. Retain the lease until process exit or the three-minute detection timeout. Process monitoring uses IGameProcesses for deterministic tests. This does not infer a different executable spawned by a bootstrap launcher, and process detection does not imply window readiness.
