# Requirements — DLsite Client

Clean-room Nest replacement for library management only. Windows and Linux support; SteamOS is the Linux target. Store browsing and purchasing are outside scope.

## Library

- Filters: All / Games / Images / Videos / Audio / Other + work-type checkboxes; fav / hidden / downloaded / update available; tag filter with background loading
- Filter chrome stays **collapsed by default**; expand only when the user opens a section (work types, columns, advanced filters)
- Work-type codes shown via i18n / Nest work-type names (never raw-only when a label exists)
- Modes: list (selectable sortable columns), grid (thumb + title), hybrid
- Columns: purchase date, title+thumb (fixed), maker, work format, store rating as text `x/5` (default `0/5`, no stars), sales date, download flag only, extract size separate; **no Viewer**
- **Paging:** default page size 20; user may choose 50 or 100
- Explicit Update button when update exists (all modes). Update = purchase `update_date` newer than local revision stamp (`LocalUpdateDate`). Our extracts stamp on finish; Nest-imported folders infer stamp from install folder mtime (never assume equal to remote `update_date`).
- Context menu: install / uninstall / fav / hide
- Multi-select: batch Download, Update, Uninstall (confirm)
- Double-click → detail page
- Search: fuzzy title + maker only
- Metadata + thumbnails load **off the UI thread**; placeholders/skeletons until ready; thumbs fill in as fetches complete
- **Local install detect:** reconcile library folder against purchases. Nest layout is `{libraryDirectory}/Game/{makerName}/{workName}` (also accept `{makerName}/{workName}` and `{workno}` at the library root). Run after purchase sync and on library view boot (off UI thread). Downloaded column header must fit full i18n label (no clip).

## Tags

- Prefer Nest `Product.tags`; fallback `product.json` genres; HTML scrape last resort with geo-block soft-fail
- Never block library/download on tag fetch

## DLC

- Probe Nest + `product.json` for parent/DLC; if no clean signal, **ignore feature** (no scrape)

## Downloads (in-window tab)

- Steam-like: Active / Up Next (manual reorder) / Completed — no Nest popup window
- Cache download with HTTP Range resume; extract to library; delete related cache
- zip / rar / 7z + split archives via external 7z
- Concurrent download/extract caps; extract default concurrency 1
- DL fail → resume; extract fail → integrity test (`7z t`); corrupt → re-download; OK → retry extract
- Clear on the completed list removes completed, failed, and corrupt rows, and deletes that work's `{cache}/{workno}` folder only
- Rate limit + HTTP(S) proxy in settings
- UI must not crash on large queues / big extracts

## Launcher

- Scan EXEs; multi-exe picker + “always use”; persist per workno
- Protocol `dlsite-client://play/{workno}`
- Per-work Japan locale launch flag

## Settings

- Logout, cache/game folders, start on login (Windows Run key / Linux XDG autostart)
- UI language ja / zh-TW (locale JSON files)
- Download concurrency, rate limit, proxy
- Log level + log path; rotation fixed in app (not user-configurable)
- Nest path import confirm-only (no credentials) — Windows Nest `settings.xml` only
- Optional Linux **Proton path** override for launching Windows EXEs
- **Apply changes immediately** (no separate Confirm / OK); network/IO from settings must not block the UI thread

## Auth

- OAuth on login.dlsite.com → Nest login POST → store SID in OS credentials (Windows Credential Manager / FreeDesktop Secret Service)
- **Session expiry (M6):** on Nest auth failure (e.g. 401 / login-required body), clear stored SID, surface re-login UI, and pause the download queue until signed in again

## Linux (M5)

- XDG dirs for app data / library / cache / logs
- Protocol handler + autostart via `.desktop`
- Launch Windows game EXEs via Proton or Wine; native binaries run directly
- Ship as **AppImage**

## i18n

- ja + zh-TW; strings only in `locales/*.json`

## Library UI refresh (2026-10-16)

- Use a compact navigation rail, a clear library heading, and a separate content surface.
- Keep advanced filters collapsed behind one control. Use keyboard-focusable buttons for navigation and filter actions.
- Keep table headers and rows in the same horizontal scroll area.
- Show Update actions in list, grid, and hybrid modes.
- Proton launch must set a persistent per-install prefix and the Steam client path.

- Downloads has a Resume all button. It queues paused and failed jobs in one operation, preserves partial cache and queue order, and respects the auth pause. Completed, active, and corrupt jobs are unchanged.
- Re-queueing a completed work replaces its completed queue row. SQLite keeps one row per work number.

## Install integrity and platform completion (2026-10-16)

- Extract into a separate folder on the library filesystem. Failed or cancelled extraction must leave the installed work unchanged.
- On update, retain the entire previous installation under `.dlsite-client/backups`. Preserve files absent from the new archive and known save files in the new install. Keep conflicting old files in the backup. Never delete the backup as part of update or cache cleanup.
- If replacement fails, restore the old installation. Use the recorded install path when updating an imported work.
- Discover Windows EXEs without case sensitivity. On Linux also discover executable ELF files and executable scripts. Ignore symlinks during recursive discovery.
- AppImage protocol and autostart entries use the persistent `APPIMAGE` path and proper desktop-entry quoting.
- Find Linux 7-Zip through PATH (`7zz`, `7z`, `7za`); do not require Windows executables.
- Windows MSI uses standard wizard, destination choice, progress, completion, cancellation and maintenance dialogs. Keep per-user and all-user scope options. Uninstall must retain library, cache, settings and save data.
- SteamOS validation uses a new isolated VM. Never attach host physical disks or the real game library to recovery tools.
- Ship 7-Zip with MSI and AppImage, including its license. A standard install must extract archives without a separate 7-Zip install.

## SteamOS runtime completion

- Bundle WebKitGTK, its helper processes, resources, dependencies and license notices in the AppImage. Do not require changes to SteamOS system packages.
- Restore host loader and runtime environment when starting games or host desktop tools.
- Keep Wine prefixes per installation outside the game directory. Prefer umu-run when available for Proton outside Steam; discover custom Proton tools and external Steam libraries.
- Validate the produced AppImage in the isolated VM, including extraction fallback and login page rendering. Use credentials explicitly supplied for the test; keep values out of logs and remove temporary credential text files after testing. Record untested hardware and account checks explicitly.
- Linux session storage must use Secret Service. Bundle secret-tool, report a failed store operation, and do not silently write session secrets to a file.
- Direct Proton launch must use the Steam Linux Runtime required by toolmanifest.vdf. Missing runtimes must return a localized error. Run launch preparation and prefix creation off the UI thread.
- Use WebKit software rendering when no DRM render node exists. This must not alter the rendering environment of launched games.
- Protocol registration must create a new XDG config directory before calling xdg-mime.
- Include a private Mesa software renderer for WebKit when no DRM render device is available; use host GPU drivers when a render device exists.
- AppImage packaging writes a new image before replacing an existing artifact, so an open test image cannot cause Text file busy.
- On KDE, activate the installed Secret Service compatibility daemon when org.freedesktop.secrets has no owner. Do not change an existing user wallet. The isolated VM may use a new test wallet for storage and persistence checks.

- A credential lookup timeout must let startup continue without a session. A credential-save timeout must remain a login error.

## Nest integration platform scope

- Nest installation discovery, configuration/path import and first-run import prompts are Windows-only. Linux must not show import controls or run import discovery, prompts or import-state writes. Keep ordinary library folder selection and local install detection on Linux.

- Enforce Nest integration scope at compile time. Use the Windows TFM symbol and conditional source/project entries. The Linux app must have no Nest import module, importer service registration or import UI code, and must not reference the Windows platform assembly.

## Batch selection identity

- Bind rendered library rows to Workno, across list, grid and hybrid views. Sorting, filtering, paging and redraws must not move batch selection to another work.
- Only a user checkbox action changes batch selection. Applying checked state during rendering must not change selection.
- Add a headless UI regression that sorts by update availability, checks a visible work and verifies that unrelated works stay unchecked.

## Library filters and detail folders — 2026-10-17

- Add an update-available library filter using WorkItem.HasUpdate. Combine it with existing filters and reset paging on change.
- Keep tag filtering visible even when no tags are cached. Opening it loads missing tags for all library works off the UI thread, with serialized requests and per-session attempt caching. Opening detail also loads missing tags. Persist tags without changing install or download state.
- Prefer purchase API tags, then store-section product.json genres, then genre links in a product page's main_genre block. Ignore other page links. Missing/blocked/failed metadata leaves tags blank; do not bypass geo-blocks or invalidate the login session.
- Detail shows an Open game folder action when an existing install directory is available. Use the platform shell and report failures with the existing status text.
- Store fallback labels in locales only. Tag filter matches a complete tag, not a substring.

## Game launch status and duplicate prevention

- Show a spinner after Start is pressed, including launch preparation. Clear it when the selected executable's process appears. A process appearing does not prove its window is ready.
- Poll process names, with executable paths where available. On Wine, match the selected EXE name and command-line path rather than the Wine/Proton wrapper process.
- Prevent another launch of the same executable while it starts or runs, across application instances and protocol launches. Use a file lease outside the game directory. Release it on failure, timeout or process exit.
- Stop waiting after three minutes and show a localized timeout error. A later retry is allowed. A launcher that starts a different executable cannot be tracked reliably by the selected name.
- Keep launch monitoring off the UI thread. Use injected process checks and short timeouts in tests.

## Release 0.1.2

- Ship the approved library, platform, installer, filter, tag and launch changes as v0.1.2. Produce a fresh Windows Native AOT MSI and Linux AppImage; publish release assets and SHA-256 checksums to the existing repository.
- Run core/UI tests and package checks before publishing. Remove obsolete images, duplicate payloads and test output; retain local conversation records, final validation logs and the reusable VM.

## Local GitHub Actions runner

Run trusted CI and release workflows on this PC. Build Windows MSI on the host and AppImage in the local Linux VM. Hold release publication until the workflows pass.

## Credential helper early exit

If the Linux credential helper closes its input before reading a session value, return the existing Secret Service unavailable error. End the helper process and do not write a plaintext fallback. Test with input larger than the pipe buffer so the race is repeatable.

## Linux first-run app data

A missing XDG configuration directory must still resolve to an absolute user app-data path. Services may create it on first run. Never fall back to the AppImage working directory, which is read-only.

Resolve the launch-lease directory before XDG_DATA_HOME exists as well. A first game launch must not create lock files in the AppImage mount.

## Personal build-host installer checks

MSI tests must not upgrade or remove an installed copy on this PC. Check all related products by UpgradeCode before installing. Manual tests refuse an occupied host; CI may explicitly skip installer execution while still building the release package, with a visible notice.
