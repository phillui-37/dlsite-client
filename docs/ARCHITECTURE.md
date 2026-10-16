# Architecture

## Solution

| Project | Lang | Role |
|---------|------|------|
| `DlsiteClient.App` | F# | Avalonia.FuncUI Elmish UI |
| `DlsiteClient.Core` | C# | API, DB, queue, extract, launcher |
| `DlsiteClient.Platform` | C# | Abstractions |
| `DlsiteClient.Platform.Windows` | C# | Creds, Nest import, protocol (`dlsite-client://play/{workno}`), soft JP locale env, startup |
| `DlsiteClient.Platform.Linux` | C# | XDG paths, libsecret/`secret-tool`, `.desktop` protocol/autostart, Proton/Wine launch, `xdg-open` |

## Data flow

```
UI (FuncUI) → Core services → Platform traits
Auth → Avalonia WebAuthenticationBroker OIDC (`dlsite.user` / fragment `/home/login`) → POST nest/login → CredentialStore
Library ← GET purchases (+ detail / product.json enrich)
Queue → Range download → cache → 7z extract → library folder
```

## Local state

SQLite under app data: purchases cache, fav/hide, install paths, exe choice, queue order, update_date.

## Secrets

`__DLsite_SID` / tokens in OS credential store only.

## Session expiry

Nest 401/403 (or `meta.code` 401/403) → `NestSessionExpiredException` → clear credential store + in-memory SID → pause download pump until re-login → UI banner.

## Download / extract isolation

- Background workers + channels; UI observes events only
- External `7z` process for extract/test
- Bounded concurrency via runtime-resizable gates; settings change updates download/extract slots + proxy without restart
- Extract fail → `7z t`; corrupt/incomplete → re-download confirm; test OK → retry extract (skip re-download when cache present)
- Job failures do not kill process

## UI thread / IO

- **Never** block the Avalonia UI thread on network, disk walk, credential vault, or SQLite-heavy sync (no `.GetResult()` / `.Wait()` / sync HTTP from views)
- Use `DlsiteClient.App.UiAsync` to run work off-UI and marshal results back with `Dispatcher.UIThread.Post`
- Thumbnails: async cache + placeholder/skeleton; notify UI when a bitmap arrives
- Library list is paged; do not materialize thousands of row visuals at once

## Logging

Microsoft.Extensions.Logging + file sink with fixed rotation; user sets level + directory only.

## Platform compilation

The app target net10.0-windows defines WINDOWS and references the Windows platform project. The neutral net10.0 target is the Linux app and references the Linux platform project. NestImportPrompt.fs is included only for Windows. F# #if WINDOWS blocks control its startup call, settings action, importer service field and registration. The Linux platform has no Nest importer implementation.

## Metadata and launch state

LibraryService serializes missing-tag fetches behind a semaphore, caches attempts and writes only tags_json through a separate SQLite connection. JSON genres precede product-page genre links. Library and detail subscribe to metadata changes. The tag filter starts background library backfill; the detail page requests its own missing tags.

LauncherService owns Preparing / Starting / Running / Failed state and emits Changed. Process checks run off the UI thread. An executable-path file lease excludes another instance until process exit or start timeout. IGameProcesses supports isolated tests; the real implementation checks process names and module paths, or Wine argv paths. The UI shows the existing Material circular ProgressBar while preparing/starting.
