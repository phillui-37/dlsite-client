# Agent rules — DLsite Client

## Source of truth

1. **Markdown in this repo wins over chat.** Read and update docs before coding.
2. Primary docs: `docs/REQUIREMENTS.md`, `docs/ARCHITECTURE.md`, `docs/DECISIONS.md`, `docs/API.md`, `docs/MILESTONES.md`.
3. API contract: `openapi.yaml` + `API-EXTRACTION-GUIDE.md`.
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
