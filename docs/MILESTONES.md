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
