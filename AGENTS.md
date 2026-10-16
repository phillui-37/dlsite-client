# Agent rules — DLsite Client

## Source of truth

1. **Markdown in this repo wins over chat.** Read and update docs before coding.
2. Primary docs: `docs/REQUIREMENTS.md`, `docs/ARCHITECTURE.md`, `docs/DECISIONS.md`, `docs/API.md`, `docs/MILESTONES.md`.
3. API contract notes: `docs/API.md` (Nest HTTP surface; no OpenAPI artifact in-repo).
4. UI strings: `locales/*.json` only. Never hardcode user-facing copy in F#/C# (debug logs OK).

5. When intent changes, update the relevant MD **first**, then code.

## Stack (locked)

- UI: Avalonia.FuncUI (F#)
- Core / platform: C# (.NET 10)
- Publish target: Native AOT (see `docs/DECISIONS.md` for fallback)
- Windows first; Linux/Proton is M5+

## Safety

- Never import Nest credentials from `settings.xml` — only `libraryDirectory` / `cacheDirectory` with user confirm.
- Session secrets go in OS credential store only.
- Download/extract must not crash the UI process.
- **Never use F# `sprintf`/`printf`/`%A` under Native AOT** — use `DlsiteClient.App.Fmt` / `String.Format`.

## Cursor Cloud specific instructions

- .NET SDK 10 is on `PATH` as `dotnet`. The wrapper sets `DOTNET_ROOT` and `LD_LIBRARY_PATH`.
- Dev build (managed, not Native AOT): `dotnet build src/DlsiteClient.App/DlsiteClient.App.fsproj -c Release`
- Dev run needs `DISPLAY`. `dotnet run --project src/DlsiteClient.App -c Release`. UI locale defaults to `ja`.
- App TFMs are `net10.0-windows` and `net10.0`. `Services.fs` compiles Windows or Linux platform types by target; only the Windows target includes Nest import. Linux core checks pass in SteamOS recovery; see `docs/STEAMOS.md` for release blockers. `/usr/local/lib/dlsite-winshim` supplies `CredRead` and `MessageBoxW` so the window can open. `CredRead` returns not-found. That shim is not a credential store.
- Settings apply on change and use the selected platform startup registration. Do not use the old Windows-only settings limitation as a description of current code.
- Native AOT and MSI stay on Windows (`scripts/publish-aot.ps1`, `vcvars64`). Do not publish AOT on this VM.
- `7z` is installed for archive checks.
