# Requirements — DLsite Client

Nest replacement. Windows first; UI must stay cross-platform capable.

## Library

- Filters: All / Games / Images / Videos / Audio / Other + work-type checkboxes; fav / hidden / downloaded; tag filter when tags exist
- Filter chrome stays **collapsed by default**; expand only when the user opens a section (work types, columns, advanced filters)
- Work-type codes shown via i18n / Nest work-type names (never raw-only when a label exists)
- Modes: list (selectable sortable columns), grid (thumb + title), hybrid
- Columns: purchase date, title+thumb (fixed), maker, work format, store rating as text `x/5` (default `0/5`, no stars), sales date, download flag only, extract size separate; **no Viewer**
- **Paging:** default page size 20; user may choose 50 or 100
- Explicit Update button when update exists (all modes)
- Context menu: install / uninstall / fav / hide
- Multi-select: batch Download, Update, Uninstall (confirm)
- Double-click → detail page
- Search: fuzzy title + maker only
- Metadata + thumbnails load **off the UI thread**; placeholders/skeletons until ready; thumbs fill in as fetches complete
- **Local install detect:** reconcile library folder against purchases. Nest layout is `{makerName}/{workName}`; also accept our `{workno}` folder. Run after purchase sync and on library view boot (off UI thread). Downloaded column header must fit full i18n label (no clip).

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
