# Decisions (ADR)

## ADR-001: Stack = Avalonia.FuncUI F# + C# Core + .NET 10 AOT

- **Status:** Accepted
- **Why:** Cross-platform UI; Nest affinity in C#; FuncUI avoids XAML AOT pain; competitive with Tauri on RAM for dense lists
- **AOT smoke (2026-10-15):** **PASS** after VS 2026 C++ Desktop tools. Publish with `vcvars64.bat` then `-p:PublishAot=true -r win-x64`. Output: `artifacts/aot-win-x64/`.
- **Note:** Still has IL2026/IL3050 JSON trim warnings (source-gen cleanup later). Dev builds can stay non-AOT; release prefers AOT when linker present.
- **Fallback:** If linker missing → trimmed self-contained (`PublishAot=false`).

## ADR-002: Linux / Proton later (M5+)

- Windows first; platform traits ready; Linux project is stub

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

- Windows: AOT folder + **MSI** (WiX) wizard — `scripts/publish-msi.ps1`
- Linux: **AppImage** in M5+ backlog
