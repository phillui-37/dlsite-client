# Decisions (ADR)

## ADR-001: Stack = Avalonia.FuncUI F# + C# Core + .NET 10 AOT

- **Status:** Accepted
- **Why:** Cross-platform UI; Nest affinity in C#; FuncUI avoids XAML AOT pain; competitive with Tauri on RAM for dense lists
- **Versions (2026-10-15):** Avalonia **12.1.4** + FuncUI **2.0.0** + WebView **12.1.1** (MIT) + Material.Avalonia **3.21.0**; Microsoft.Extensions / Sqlite **10.0.13**
- **AOT smoke (2026-10-15):** **PASS** after VS 2026 C++ Desktop tools. Publish with `vcvars64.bat` then `-p:PublishAot=true -r win-x64`. Output: `artifacts/aot-win-x64/`.
- **JSON:** `AppJsonContext` source-gen for settings + Nest DTOs (reflection JSON is disabled under AOT — mode/settings save crashed without it).
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

## ADR-013: OAuth = Avalonia WebAuthenticationBroker (MIT WebView)

- **Status:** Accepted (M1), corrected 2026-10-15
- Match Nest: `client_id=dlsite.user`, fragment redirect `https://www.dlsite.com/home/login`
- **License:** `Avalonia.Controls.WebView` **≥ 11.4.0 / 12.0.0** is FOSS (MIT). **11.3.x is legacy Accelerate-only** — do not use.
- Package pin: `Avalonia.Controls.WebView` **12.1.1** (aligned with Avalonia 12.1.x).
- API: `WebAuthenticationBroker.AuthenticateAsync` — WebView2 on Windows.
- **UI thread:** call `AuthenticateAsync` without blocking (no `.GetResult()` / `.Wait()` on the UI thread). Blocking opens an empty login window and freezes the app — the broker needs the dispatcher to paint and navigate.
- Manual SID login kept as debug fallback only.

## ADR-014: UI theme = Material.Avalonia

- **Status:** Accepted
- Package: `Material.Avalonia` 3.21.0 — Light / Primary Blue / Secondary Teal (not Fluent)

## ADR-015: Japan locale launch = soft env hint

- **Status:** Accepted (M4)
- Per-work flag sets child `LANG` / `LC_ALL` to `ja_JP.UTF-8`.
- Full Windows AppLocale / Locale Emulator is out of scope (OS-dependent, not FOSS-bundled).
- Protocol `dlsite-client://play/{workno}`: launch immediately when exe is known; if multi-exe needs a pick, open the work detail picker instead of failing closed.
