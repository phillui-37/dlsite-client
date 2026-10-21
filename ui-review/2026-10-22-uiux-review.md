# UI/UX review — DLsite Client (2026-10-22)

Scope: Avalonia.FuncUI views only (`src/DlsiteClient.App/Views/*.fs`) plus `locales/*.json`.
Core, Platform and docs requirements are unchanged. Evidence: source + rendered screenshots in
`before/` and `after/` (headless Avalonia + Skia, real `App` theme, ja locale, 1440×900, local
SQLite fixture, no network thumbnails).

## Verdict

The UI is not broken, but it had five defects that a user meets on the first working day. They are
fixed. The rest of the list below is ranked and not yet done; do not treat it as done.

## Fixed in this change

| # | What the user hit | Fix | Files |
|---|---|---|---|
| 1 | In list mode the column header scrolled away with the rows. The outer `ScrollViewer` measured the inner one with infinite height, so the inner viewer never scrolled. | Outer viewer: vertical scrolling disabled, horizontal stays. Inner row viewer: own vertical scroll, bounded height, header stays docked. Proven headless: outer offset stays 0 while the inner offset moves 300 px; header visible in `after/library-scrolled.png`. | `LibraryView.fs` |
| 2 | Single-work **Uninstall** deleted the install folder with no confirmation. Batch uninstall asked, single work did not. | Both paths now go through `IShell.Confirm`; the message names the work (ja/zh-TW). | `LibraryView.fs`, `locales` |
| 3 | The context menu had **ダウンロード** and **インストール** with identical behaviour (enqueue + jump to Downloads). | One action stays (`インストール`); **詳細を開く / 開啟詳細** is added so the context menu is not the only keyboard-reachable way into detail. | `LibraryView.fs`, `locales` |
| 4 | List rows, grid cards and hybrid rows were inert to the eye (no pointer cursor, no keyboard path). | Hand cursor on all three row types; Enter on a focused list row opens detail. | `LibraryView.fs` |
| 5 | Hiding the **Update** column silently removed the Update action in list mode. Requirement: update action in all modes. | The Update cell and its width are always reserved; the sort header always shows. | `LibraryView.fs` |
| 6 | Grid cards moved the thumbnail down when a work had an update; selection checkbox took its own row. | One action row: checkbox left, Update right, thumbnail at a fixed offset. Cards align. | `LibraryView.fs` |
| 7 | Batch **Download/Update** acted on the filtered result only, while **Uninstall** acted on the whole selection, for the same visible count. | `enqueue` resolves the full selection through `Library.Works`. | `LibraryView.fs` |
| 8 | Empty library said **作品がありません** in every situation: filtered to nothing, never synced, DB empty. | Filtered case shows a different line plus **絞り込みを解除 / 清除篩選**. | `LibraryView.fs`, `locales` |
| 9 | Downloads page showed raw enum names (`Downloading`, `CorruptArchive`) and buried the error behind an ellipsized file name. | Localised state labels; error on its own wrapping line in the danger colour. | `DownloadsView.fs`, `locales` |
| 10 | Downloads top bar had no title alignment, the Resume-all button floated, and the speed line printed bare numbers without units. | Title block left, Resume-all right, summary line in one quiet line, empty-queue message added. | `DownloadsView.fs`, `locales` |
| 11 | Settings: proxy mode showed `off` / `system` / `custom`, log level showed `Verbose` … `Fatal`; account status printed `SID OK`. | All three localised; stored values unchanged. SID field masked and cleared after login. Section headings spaced. | `SettingsView.fs`, `locales` |
| 12 | Detail printed the raw work-type code (`MNG`) while the library printed `マンガ`; long titles did not wrap; errors used pure red on light grey (3.6:1). | Same label lookup as the library, wrapping title, wrap-panel action row, `#B3261E` error colour. | `DetailView.fs` |
| 13 | Active filter chips and the active sort header were `#1976D2` on `#E3F2FD` (4.03:1) and `#1976D2` on `#F0F3F8` (4.14:1); `前へ`/`次へ` were `#2196F3` on white (3.12:1). Session banner title was 3.45:1. | `#1565C0` for those text roles (5.03:1 / 5.15:1 / 5.75:1); banner title `#BF360C` (5.11:1), subtitle `#8D2B0B`. | `LibraryView.fs`, `MainView.fs` |
| 14 | Hybrid metadata used a Latin middle dot instead of the Japanese one, and the separator was hard-coded in F#. | `common.fieldSeparator` in both locale files. | `LibraryView.fs`, `locales` |

Locale files keep the same key set; 21 keys added, both files. No new language, no new file.

## Second round (2026-10-22, after room review)

Decided by lead with votes from cursor-grok and local qwen3.8 (LM Studio). codex thread start failed
in the session, so no third vote.

| Item | Decision | Change |
|---|---|---|
| A1 Downloaded cell showed hard-coded `OK` / `-` | new locale keys | `library.downloaded.yes` / `.no` (`DL済`/`未DL`, `已下載`/`未下載`). Short values only: the cell is 92 DIP of usable width after padding, so `未ダウンロード` would clip. |
| A2 SID watermark was the literal `__DLsite_SID` | locale key, same text | `auth.sidPlaceholder` in both files. The token is the copied cookie, so it stays identical. |
| A3 Library has no sync entry point | **not done, docs first** | `SyncPurchasesAsync` is a full purchase fetch + upsert + reconcile. On the `AfterInit` effect it would hold the skeleton for a network call and run two writers; the empty library is also a valid synced account. Needs a `docs/REQUIREMENTS.md` line before code. |
| A4 `workTypeLabel` duplicated in Detail | moved to shared code | `Localization.fs` after `let t`; `DlsiteClient.App.fsproj` already compiles it before both views, so no project change. |
| A5 failed thumbnail identical to loading | glyph + explicit retry | `ThumbCache.invalidate` drops the cached failure; the `Failed` box shows `!` and one click retries. `e.Handled <- true` stops the row double-click from opening detail. |
| B1 uninstall reported success with files left | **done** | `MarkUninstalledAsync` returns from the `catch`: the flag, the saved exe and the reload only run when the delete succeeds. No signature change, no caller change. |
| B2 Change executable cleared the saved exe before confirm | **done** | Removed the `ClearSelectedExe` call; the picker keeps its choice in component state and the service writes the new exe only on launch. `ClearSelectedExe` now has no caller. |

## Third round (2026-10-22, owner approved, room retired)

Communication moved off the room. Votes came from **gpt-6.1-sol through the codex CLI** (`codex exec -m gpt-6.1-sol`, read-only sandbox) because the DSH `subagent_codex` product still fails at thread start: the local proxy answers `426 Upgrade Required` on the websocket upgrade, and the CLI falls back to SSE while the product does not.

| Item | Decision | Change |
|---|---|---|
| A3 empty library had no sync path | docs line first, then the button | `docs/REQUIREMENTS.md` "Library boot and sync entry"; empty state shows Sync when signed in, no work stored and no filter; runs on the pool, disabled while busy, failure localized (`library.syncFailed`) with the technical detail on a second line. |
| A6 uninstall failure invisible | `Task<bool>` + localized failure | `MarkUninstalledAsync` returns false and keeps the installed state; context menu and batch report `library.uninstallFailed`; the status line is cleared before each attempt. |
| A8 grid/hybrid had no sort control | toolbar control for all modes | sort `ComboBox` + 昇順/降順 buttons in the filter row; list header sort kept. |
| A9 dead API | delete | `LibraryService.ClearSelectedExe` removed. |
| Change-exe keeps the old exe | follow-up of B2 | after the picker opens, "always use" is off, so the old saved exe survives unless the user ticks it. |

Every one of these also got a `docs/REQUIREMENTS.md` line, because a UI change that contradicts the docs is a contract change, not a polish change.

Review loop: gpt-6.1-sol reviewed the diff twice. First pass found four items; all accepted except the parts below. Second pass found two defects, both fixed:
- filter reset did not clear work type / favourites / downloaded / updates / media, so "reset" could leave an empty library.
- the Update column ignored its own visibility checkbox and could not be hidden.

Rejected from review: turning the asc/desc chips into accessible toggle controls through `AutomationProperties` — this FuncUI build exposes no such DSL helper, and the headless test locates buttons by `Content`. Recorded as an open item instead.

Local qwen3.8 reviewed the same diff. Its reasoning stream argued that the removed `workTypeLabel` would not compile; that is a false positive — the function moved to `Localization.fs` and is fully applied. The model also returned an empty `content` field on large diffs in this LM Studio build, so it is not reliable as an automated gate here.

## Verified and NOT fixed (ranked)

1. **Linux uninstall confirmation depends on zenity/kdialog.** Without either, `LinuxShell.Confirm`
   returns false and the action silently does nothing. Needs an in-app dialog or a declared dependency.
2. **Sorting is ordinal** (`sortKey` casts to `IComparable`), so Japanese titles do not sort in kana
   order. The fix must be proven under Native AOT (ICU collation) before it goes in.
3. **Settings values**: free-text numbers with silent clamping to defaults, no folder pickers, one
   debounce instance shared by every field. gpt-6.1-sol voted for text validation with inline
   localized errors and against `NumericUpDown` (template/trimming risk under AOT). Not shipped: no
   Windows AOT build was available to prove it.
4. **Session/re-login errors are discarded** in the banner (`MainView.fs`), and a post-login sync
   failure becomes invisible once the banner goes away.
5. **Thumbnail retry is manual only.** A failed fetch stays failed until the user clicks the
   placeholder; there is no automatic retry and no offline indicator.
6. **Wheel over the bounded list scroller may go to the outer viewer and not scroll the rows.** Needs
   a rendered wheel test on a real window.
7. **Navigation, mode and direction buttons expose no selected state to assistive technology.**
   This FuncUI build has no `AutomationProperties` helper in its DSL, and the headless tests locate
   buttons by `Content`, so the fix needs a control change, not a property.
8. **Locale is not applied to `CultureInfo.CurrentUICulture`**, so Han glyph shapes follow the OS
   culture, not the selected UI language.
9. **Reviewed and rejected:** the `Update` column visibility checkbox keeps its
   meaning (list-only); grid and hybrid always show the Update action, as the requirements state.

## How the screenshots were made, and the one artefact to know

Headless `Avalonia.Headless` + `UseSkia()`; each view is mounted in a `HostControl` inside a 1440×900
window and captured with `CaptureRenderedFrame`. The scratch harness lives outside the repo.

`after/` has no grid, hybrid or advanced-filter screenshot. In the headless host those clicks did not
produce a re-render (the chip never changes state), while the same clicks do work in the headless
test project that drives `window.Content <- LibraryView.view ...`. Both screenshots are therefore
absent rather than wrong. The three changes that affect those views (card alignment, list scroll,
contrast) are visible in `library-list.png` and `library-scrolled.png`.

## Build notes for this machine (macOS)

- Package versions `12.1.4` / `10.0.13` are not in the local NuGet cache; it has `12.1.3` / `10.0.5`.
  The repo pins the newer ones and stays unchanged.
- `Avalonia.BuildServices` 11.3.2 writes a telemetry log into `~/Library/Application Support/` and
  fails the build when the MSBuild node has no file identity (multi-node builds, e.g. the test
  project). Workaround used: `-p:DirectoryBuildTargetsPath=<stub>.targets` that no-ops `AvaloniaStats`.
- `tests/DlsiteClient.RegressionTests` stops at `platform 7-Zip tool found` on macOS. `ResolveSevenZip`
  branches on `OperatingSystem.IsLinux()`, so macOS falls through to Windows paths. **Same on
  pristine HEAD** — pre-existing, not caused by this change.
- `dotnet build ... -f net10.0` and `-f net10.0-windows -p:PublishAot=false` both succeed;
  `tests/DlsiteClient.LibraryUiTests` passes fully after the changes.
