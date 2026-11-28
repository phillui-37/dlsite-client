# Milestones

| ID | Scope | Status |
|----|--------|--------|
| M0 | Docs SSoT + FuncUI F# / C# Core skeleton + Native AOT smoke gate | done (pushed a549dba) |
| M1 | Auth + purchases sync + Nest path import; probe tags + product.json DLC | done (OIDC WebView2 + Nest path confirm) |
| M2 | Library UI (3 modes, filters, cols, search, detail) + i18n + tags/DLC if proven | done |
| M3 | Download queue, multi-select DL/update, resume, extract triage, rate limit, proxy, isolation | done (Up Next reorder, live slots/proxy, extract triage UX) |
| M4 | Launcher, protocol, locale, settings (log/proxy/rate), fav/hide, multi-select uninstall | done (protocol→picker, change exe, library Play, live log) |
| M4.1 | Library perf/UX: off-UI IO, async thumbs, paging 20/50/100, collapsed filters, live settings | done |
| M5 | Linux + keyring (libsecret) + Proton/Wine launch + **AppImage** packaging | runtime implemented; SteamOS VM checks pass; physical Deck / Gaming Mode checks remain |
| M6 | Session expiry: detect Nest 401/auth fail, clear SID, prompt re-login, pause queue | done |

## LM Studio

Optional for boilerplate only (`localhost:1234`). Auth, download correctness, architecture stay human/agent-owned.

## Functional review and UI refresh — 2026-10-16

- Added a dark navigation rail, compact search and filter controls, flat table headers, and keyboard-focusable navigation and filter buttons.
- Advanced library controls start collapsed. Table headers and rows share horizontal scrolling.
- Grid and hybrid modes now expose the existing Update action.
- Downloads has Resume all for paused and failed jobs. The operation preserves cache, progress, queue order, and auth pause. It persists once and sends one change event.
- Fixed re-queueing a completed work: replace its old completed row before saving to SQLite.
- Removed F# formatted output from the fatal-error path for Native AOT.
- Proton launch now sets a persistent compatibility data directory and the detected Steam client path. Reference: [Valve Proton launcher](https://github.com/ValveSoftware/Proton/blob/master/proton).
- Validation: Release build for both app target frameworks passed. Ten isolated queue checks passed against a temporary SQLite database. Windows Computer Use confirmed library rendering, advanced-filter controls, and the disabled Resume all button for an empty queue. Windows Native AOT publish and window startup also passed; compiler AOT/trim warnings remain. No live download or Linux game launch was tested.

### Follow-up fixes — 2026-10-16

The three listed defects are fixed: staged updates with retained backups and rollback, native Linux executable discovery, and persistent AppImage registration. Tests passed on Windows (27) and SteamOS recovery (27). See [SteamOS validation and remaining blockers](STEAMOS.md).

The MSI now uses standard WiX wizard and maintenance dialogs, bundles 7-Zip, and uses native dual-purpose installation scope without SummaryInformation patches. Per-user install/repair/removal and data retention passed. A 0.1.0 test baseline upgraded to 0.1.1 successfully. A silent all-user install was correctly refused without administrator rights; elevated install/repair/removal still needs an administrator session.

### SteamOS runtime work — 2026-10-17

Bundled private WebKit/GTK/TLS resources and software Mesa for systems without a render node. Game launches restore host environment values. Added Secret Service failure handling, custom/external Proton discovery, umu support and required-runtime resolution for direct Proton. Windows regression checks: 29. SteamOS regression checks: 41. Real GE-Proton, direct Proton and Wine probe launches passed. The actual AppImage passes login, 683-purchase sync, keyring storage, restart restore, logout and re-login. FUSE and extract-and-run pass; see [SteamOS results](STEAMOS.md).

### Platform-only Nest integration — 2026-10-17

Nest import is now compiled only in the Windows app. Linux excludes its UI, first-run module, service field and registration, and no longer references the Windows platform DLL. Both Release targets build. Assembly metadata and dependency manifests confirm the separation; 29 regression checks pass. Existing packaged artifacts predate this source change.

### Batch selection after sorting — 2026-10-17

Reproduced in the actual library view with Avalonia Headless: after update-available sort, clicking synthetic RJ024 also checked RJ025 and RJ013. Unkeyed row controls retained callbacks for previous works; programmatic IsChecked updates fed back through the same selection handler. Rows now have Workno keys in list/grid/hybrid modes, and selection changes only on checkbox Click. Sixteen headless UI checks cover sort, redraw, selection/unselection, mode changes, filtering and paging. Both Release app targets build; 29 core regression checks pass. Run: dotnet run --project tests/DlsiteClient.LibraryUiTests -c Release. Packaged artifacts have not been rebuilt for this source fix.

### Filters, tags, install folder and launch state — 2026-10-17

Added update-available filtering, complete-tag filtering with background metadata loading, detail tag loading and an Open game folder action. Corrected the store-section product.json endpoint; public HTML genre links are the fallback. Blocked metadata remains blank. Tag writes retain install/download state.

Added a circular launch indicator, executable process monitoring, a three-minute start timeout, running-state display and duplicate-start prevention through a shared executable-path file lease. The lease works across application instances. Different executable names started by bootstrap launchers cannot be inferred reliably.

Validation: 48 Windows core checks and 33 headless UI checks pass. Public product JSON and HTML were checked against a live work. Both app targets build. These changes have not been tested with a live commercial game or repackaged into MSI/AppImage.

### Local CI and release builds — 2026-10-17

GitHub Actions now runs on the Windows PC and its local SteamOS VM (Ubuntu 24.04 build userspace). Run 38945000421 passed 48 Windows core checks, 60 Linux core checks, 33 headless UI checks per platform, Native AOT MSI publishing and MSI maintenance checks, AppImage packaging and bundled-runtime checks. CI exposed and fixed an early-exit Linux credential-helper pipe race; its forced large-input regression also passed three local runs. The VM command agent now uses a persistent service, and a full power-off/start check passed. Windows runner startup uses the current account's Startup folder.

The fresh AppImage desktop check found a first-run empty-path defect when XDG directories did not exist. App-data and launch-lease paths now resolve without requiring existing directories. All 62 Linux checks pass. The fixed FUSE package shows library/settings and the native vivion ID login form at 1280 x 800 with a fresh user config root. No new credentials were entered.

### Basic functionality verification and UI handoff — 2026-11-04

Fixed the OAuth callback validation gap: the generated authorize-state is now retained through WebAuthenticationBroker and must match the callback state before the token is exchanged. The callback parser also rejects a callback URI whose scheme, host, port or path differs from the configured redirect. Regression coverage checks matching, mismatched and missing state, and a wrong callback path.

Fresh local validation: Windows `net10.0-windows` Release build passed with `PublishAot=false` (managed dev build); Linux `net10.0` Release build passed. Core regression suite passed 53 checks and headless library UI suite passed 33 checks. Builds report existing compiler/package warnings; no errors. No live account login, purchased work download, or commercial game launch was performed. One auth verification gap remains: `docs/API.md` and ADR-013 specify a different redirect path from `NestOidc` and the architecture flow. The state and callback-origin/path checks are covered locally, but the actual broker redirect must be confirmed against the currently registered Nest flow before claiming end-to-end login verification.

UI handoff for the next design pass (source review; no rendered-screen review in this verification):
- Main shell: fixed 176 px navigation rail, with Library, Downloads and Settings inside the shared content area; session-expiry banner appears above page content.
- Library: many filters, search, mode/sort controls and batch actions share one compact header; list, grid and hybrid modes are already implemented. Revisit hierarchy and density while preserving the working filter, paging and selection behavior.
- Downloads: Active, Up Next and Completed are stacked vertically in one scrolling page; Resume all is in the heading area. Review queue hierarchy and per-row action discoverability.
- Settings: a long single scrolling form. The manual SID fallback is visible in the account section alongside normal login/logout/sync and is a strong candidate to demote or remove from the primary flow.
- Detail: a work information page with launch state, executable choice and folder action. Review information grouping and launch-action prominence.

Keep this pass UI-only; functional state and flows above are verified and should remain intact. Physical Steam Deck/Gaming Mode and live Nest account flows remain unverified in this run.

### Downloads queue UI rework and redirect-literal audit — 2026-11-04

Downloads page rebuilt around three queue cards (Active, Up Next, Completed) with per-section counts and localized empty states; per-state row actions (pause/resume, retry extract after a failed extraction, confirmation-gated redownload for corrupt archives, open library for finished works) and Up Next reorder controls. `Resume all` stays in the page heading and enables only while a paused or failed job exists; the heading keeps session speed/max/total and the auth-pause banner. All job status text now resolves through `locales/*.json`: added `downloads.state.downloading|paused|failed|completed` (ja, zh-TW) next to the existing extracting/queued keys, removing the last raw enum-name fallbacks in the row status line. Missing keys already referenced by the shipped Library, Detail and Settings views (`library.col.downloaded.yes`, `detail.info`, `detail.launchSettings`, `settings.language.ja`, `settings.language.zh-TW`) were filled in both locales.

Headless UI coverage for DownloadsView: 21 new checks seed persisted jobs (two paused, completed, failed, corrupt-archive) into the isolated database, rebuild the queue service from it, and hold the auth pause so the pump starts no download. They assert section grouping and counts, localized paused/corrupt status labels, per-state action visibility, per-row resume, persisted move-up reorder, resume-all semantics plus its disabled state, the confirmation gate on redownload, clear-completed persistence and empty states. Suite total is now 54 checks (33 prior + 21 new); core regression remains 53 checks. Windows solution Release build with `PublishAot=false` (both TFMs) passes with the usual pre-existing warnings; the Linux target was cross-compiled from Windows this pass, not re-run in a VM.

Redirect-literal audit of the auth gap noted above: the earlier "identical strings" comparison was vacuous — it compared `NestOidc.RedirectPath` with `RedirectUri.AbsolutePath`, both derived from the same constant `/home/login`. The real, still-unresolved discrepancy is three-way: `docs/API.md` and ADR-013 document `https://www.dlsite.com/home/login` as the fragment redirect, while `NestOidc.RedirectPath` is `/home/login` (matching `docs/ARCHITECTURE.md`), and the Nest API calls in `DlsiteApiClient.cs` use a third per-account segment `/home/api`. Only a live Nest login can determine which literal the registered flow expects; until then this is a documentation-consistency and verification gap, not a proven functional blocker. No OAuth constants were changed in this pass.

### UI finalization pass — 2026-11-04

Took ownership of the pending OIDC finalization and completed the UI handoff pass above.

- Verified the in-flight OIDC state fix end to end: the authorize-state is retained through WebAuthenticationBroker and the callback must match it plus the exact redirect scheme/host/port/path before any token exchange. Regression coverage: matching, mismatched and missing state, wrong redirect path, authorize URI exposure, and no session published before credential storage (6 OIDC checks).
- Repaired a mojibake'd doc comment that the in-flight edit introduced in `NestOidc.cs`.
- Library: header re-layered into title+count, search with a segmented list/grid/hybrid switch, media chips, then quick filters with the advanced toggle inline; paging (page size + prev/next) is a right-aligned footer. Downloaded cells and hybrid meta use the localized label (no hardcoded "OK"). Tighter rows and a centered empty state.
- Downloads: page heading with speed/session stats and Resume all at top right; Active / Up Next / Completed are separate cards with counts, empty states, per-row state dots and separated error text.
- Settings: section cards (account / environment / download / logging / Linux) with two-column rows; language and proxy combos show localized labels; the manual SID fallback is collapsed behind a debug disclosure with masked input; the proxy URL field enables only in custom mode.
- Detail: header card (thumb, title, maker, type/rating pills), prominent action row with a filled Play button, info card (adds sales date, localized work type, tag chips) and a launch-settings card grouping the locale flag, saved exe, multi-exe picker and status.
- Shell: nav rail brand block with accent mark; navigation behavior and labels unchanged.
- New strings only in `locales/ja.json` + `locales/zh-TW.json`, including download-state labels (extracting, queued) so raw enum names never render.
- Validation (Windows host, managed Release builds for both TFMs, 0 errors): core regression 53 checks passed; headless library UI suite fully green; ten headless-render screenshots (ja + zh-TW; list/grid/hybrid/advanced/downloads/settings/SID-open/detail) captured from the real views with synthetic data in `.local-notes/shots-2026-11-04`. No live account, purchase, download or commercial game launch in this pass.

### Large tag catalog review — 2026-10-17

Reviewed merged PR #19 and successfully fetched the DLsite /maniax/fs reference. Its genre picker includes keyword search, grouped choices and explicit empty-search feedback. The app follow-up renders tag controls in batches of 60, searches the full cached catalog, shows displayed/total and selected counts, and explains empty searches. Selected chips remain available outside the expanded filters.

Tag queries and catalog enumeration reuse parsed per-work tag sets until the JSON changes. Library service/thumbnail/launch notifications are coalesced on a 100 ms dispatcher timer during background metadata loading. Four-request network concurrency and session failure caching from PR #19 remain in place.

Validation: both Release app targets build (existing warnings remain); 72 core checks and 44 headless UI checks pass. New checks cover changed metadata invalidating the parsed cache and selection/search/show-more behavior with 132 tags. No live-account backfill timing or desktop visual inspection was performed; installers were not rebuilt.
