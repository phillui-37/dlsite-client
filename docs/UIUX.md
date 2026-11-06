# UI/UX review and wabi-sabi direction — 2026-11-07

User request: review the existing UI with ui-ux-pro-max and implement a wabi-sabi style. Follow-up: Material may be deprecated. This authorizes replacing the Material presentation dependency while retaining Avalonia.FuncUI and the existing product behavior.

## Source review

- App.fs initializes Material Light with Blue/Teal. MainView and LibraryView define separate hardcoded blues. Downloads, detail tags and thumbnail placeholders add more local colors. These sources do not provide one visual system.
- Library filters and view-mode controls occupy one horizontal StackPanel. It cannot wrap at the minimum window width. Keep table horizontal scrolling, but let toolbar controls wrap.
- Settings use an unrestricted full-width vertical form with little separation between sections. Use a bounded, left-aligned reading width and stronger section spacing.
- Grid and hybrid cards are pointer-led. Add a keyboard path and useful accessible names for work selection and opening detail. Preserve Workno identity and all handlers.
- Detail actions need spacing, consistent hierarchy, and tags from the same surface palette.
- Retain collapsed advanced filters, ja/zh-TW localization, selection, queue, authentication, launch status, and immediate settings updates.

These findings describe the original source. The implementation and rendered verification are recorded below. The rendered stress fixture also showed that a large detail-tag catalog displaced the main actions; keep the tags in a bounded scroll area so launch/download controls remain reachable.

## Direction

Use Avalonia Simple as the control foundation. Remove Material.Avalonia and Material-only classes. This supersedes the Material dependency and blue palette of ADR-014 and ADR-019.

Warm paper and stone surfaces, charcoal ink, clay primary actions, moss secondary/status accents. Thin separators, small radii, ample section spacing, restrained headings, no decorative shadows or artificial damage. Focus outlines are functional indicators. Keep the interface calm and practical for a dense personal media library. Asymmetry comes from a narrow navigation rail and left-aligned content, not irregular control geometry. Use the existing installed/bundled font path with CJK fallback; no remote font service.

Centralize semantic colors and control styles. Define normal, hover, pressed, selected, disabled and keyboard-focus states. Maintain at least 4.5:1 normal text contrast and 3:1 meaningful focus/control boundaries. Make selection apparent through shape/border/weight as well as color. No new motion is required.

## Designer follow-up: improve the interaction hierarchy

The first wabi-sabi pass successfully changed the palette and components, but the user correctly observed that the interactions and information hierarchy mostly stayed the same. Passing control and keyboard tests did not show that the UX itself was good.

The designer inspected the retained ja library/detail/settings captures and identified three issues: five persistent toolbar bands pushed the library results to about y=280 at 960×800; the three-destination rail stayed prominent inside every view; and login, paths and download preferences appeared in one long settings form. The detail page also gives a failed-thumbnail placeholder substantial space. Those screenshots use synthetic works, so they show hierarchy rather than real catalog imagery.

Implemented a content-first library slice: search and heading stay visible; view and sort stay together in a compact row; media/status/work-type/tag/column controls share one bounded, collapsed disclosure; its localized badge counts active filters and its reset action clears them together; count and page controls sit below the results. The toolbar does not hide selection actions or change how works, downloads or detail are selected. Settings and detail were not redesigned in the focused worktree slice. Main already had grouped Settings and Detail cards; those layouts were retained during integration and now use the shared palette. Further hierarchy changes remain follow-up work.

The captured 960×800 catalog begins at y≈145 in both ja and zh-TW. The test asserts the table title appears above y=230 in both locales at that width; compared with the first-pass capture near y=280, this recovers about 135 px of first-screen space. This is a bounded layout result, not a user study. The UI still needs a user review for satisfaction.

## Verification

- Build both app target frameworks in Release.
- Run the existing offline UI suite and core regression suite.
- Add focused checks for the Simple theme replacement, wrapping at 960/1280/1440 widths, keyboard access and launch busy indicator.
- Use the existing SQLite fixture and app services only; no network, account, downloads or real library.
- Verify actual rendered Avalonia controls where the installed headless/Skia capabilities permit. Inspect library, downloads, settings and detail at narrow and desktop widths in ja and zh-TW, and record any harness limitation with the evidence collected.

## Implementation notes

Use Avalonia Simple 12.1.4 as the foundation and a single semantic palette/style source for app surfaces, text, borders, selection and actions. Remove Material.Avalonia and Material-only classes. Existing interaction and service state remain authoritative; visual changes must not alter queue, auth, update, install, launch or Workno behavior.

Library toolbar rows must wrap when constrained. Settings should use a bounded, left-aligned content column with clear section spacing. Detail action buttons should have consistent gaps. Library work selection and opening detail must be keyboard reachable and named for assistive technology. Update busy-indicator assertions to reflect the Simple theme's rendering.

Keep UI labels localized through ja and zh-TW JSON. Inspect rendered screens before completion; source-only checks do not establish that spacing, wrapping, focus, contrast or selected states look correct.


## Verified result — 2026-11-07

- `scripts/build.ps1` completed the Release build of both app target frameworks. `scripts/publish-aot.ps1` produced the win-x64 Native AOT application (43,830,272-byte executable). The first run exposed a real SDK/task-host mismatch (`MSB4216`); the script now resolves `DOTNET_ROOT`, `DOTNET_ROOT_X64` and `MSBuildSDKsPath` from its selected SDK. A rerun with the machine's inherited environment, without caller overrides, passed.
- The final offline UI run passed **81 assertions**, exit 0. It checks the collapsed filter disclosure, active-filter count, reset, the 960 px catalog position in ja and zh-TW, 24 toolbar-control bounds across 960/1280/1440 in both locales, existing library selection/paging/detail interactions, keyboard Enter activation and bounded detail tags.
- The existing Windows core regression run passed 58 checks; no core behavior changed in this UI iteration.
- The render-enabled run generated 28 Avalonia/Skia frames from synthetic SQLite fixtures under `.alma/uiux-review-compact`. Library list/grid/hybrid, settings, downloads, detail and focus screens were captured in both locales. The 960×800 library list now starts near y=145; the first-pass image starts near y=280. These are synthetic headless frames, not a desktop usability study. The earlier seven first-pass captures remain under `.alma/uiux-review`.
- Measured text pairs: ink/paper 12.98:1; muted ink/surface 6.23:1; muted ink/stone 5.04:1; light text/clay 6.83:1; clay/clay container 5.37:1; moss/moss container 5.95:1. The stronger control-outline token was darkened after its first measurement. Focus/paper is above 7:1. These numbers do not certify every composed control state.
- App dependency assets contain zero Material packages. Styles are defined in `WabiSabi.fs`; the project no longer contains an AXAML style resource. Locale JSON parses without duplicate keys. UI flow and script-contract checks pass.
- MSI packaging/installation, Linux desktop rendering, screen-reader output, largest system text, OS reduced-motion behavior, real accounts, commercial games and live downloads were not tested. No decorative motion was added. Table columns retain horizontal scrolling.

### Reproduce

Use `C:\Users\user\scoop\apps\dotnet-sdk\current\dotnet.exe` (the `dotnet` on this host's ordinary PATH resolves to a runtime-only install). `scripts/publish-aot.ps1` configures its own nested SDK/task-host paths.

```powershell
C:\Users\user\scoop\apps\dotnet-sdk\current\dotnet.exe build src/DlsiteClient.App/DlsiteClient.App.fsproj -c Release -p:PublishAot=false
C:\Users\user\scoop\apps\dotnet-sdk\current\dotnet.exe run --project tests/DlsiteClient.LibraryUiTests -c Release
C:\Users\user\scoop\apps\dotnet-sdk\current\dotnet.exe run --project tests/DlsiteClient.RegressionTests -c Release
$env:DLSITE_UI_SCREENSHOTS = Join-Path (Get-Location) '.alma/uiux-review-compact'
C:\Users\user\scoop\apps\dotnet-sdk\current\dotnet.exe run --project tests/DlsiteClient.LibraryUiTests -c Release
Remove-Item Env:DLSITE_UI_SCREENSHOTS
```

Local logs are kept under `.alma/scripts-audit` and the earlier theme-validation logs under `.alma/*.log`. First-pass source backups are under `.alma/thread-wabi-sabi-20261107`; this UX iteration's pre-edit view is under `.alma/uiux-implementation-20261107`. The initial worktree review had no commit. It is now committed locally as `796236b2c34b288812a35c09aaaeb7bdd62ca2bb` and merged into main as `39854fe17dd28816b842afe225ea508a5942acba`. Nothing has been pushed.

JEV receipts: original theme premise `293db800b21b4439aa0151d8437d7256`; syntax-repair premise `ed64254dcb674757af7c3e5843ef34a2`; Material-only assertion `3116a5474183467586656dbc0ce8234e`; detail-tag layout `808eeb99900440fd8ab174a0dacb6782`; first-pass UI result `375625db79f746ecad812805cb5cad7c`; SDK/task-host mismatch diagnosis `f9770e0e6ea640f6a27f31086c7d3b1b`; compact library flow/layout/render result `d3a198eb6c6441388800d1a3833c860f`. These receipts check their evidence, not overall user satisfaction.
