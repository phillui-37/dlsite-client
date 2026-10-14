# DLsite Client

Cross-platform Nest replacement: **Avalonia.FuncUI (F#)** UI + **.NET 10** Core. Windows first; Linux via Proton/Wine + AppImage.

Docs in [`docs/`](docs/) are the source of truth ([`AGENTS.md`](AGENTS.md)). Milestones **M0–M6** are complete ([`docs/MILESTONES.md`](docs/MILESTONES.md)).

## Features

- **Auth** — Nest OIDC via Avalonia WebAuthenticationBroker; SID in OS credential store; session expiry clears creds, pauses downloads, prompts re-login
- **Library** — list / grid / hybrid, filters, search, paging, async thumbs, Nest `{maker}/{work}` install detect
- **Downloads** — Active / Up Next / Completed queue, Range resume, 7z extract triage, rate limit + proxy
- **Launcher** — multi-exe picker, Japan locale hint, protocol `dlsite-client://play/{workno}`
- **Settings** — folders, concurrency, proxy, log level, Nest path import (dirs only), Linux Proton path
- **i18n** — ja + zh-TW (`locales/*.json` only)

## Build / run

### Windows

```powershell
.\scripts\build.ps1
dotnet run --project src/DlsiteClient.App -c Release -f net10.0-windows
```

Publish (needs VS C++ / `vcvars64` for AOT; WiX for MSI):

```powershell
.\scripts\publish-aot.ps1
.\scripts\publish-msi.ps1
```

### Linux

```bash
dotnet build src/DlsiteClient.App/DlsiteClient.App.fsproj -c Release -f net10.0
dotnet run --project src/DlsiteClient.App -c Release -f net10.0
./scripts/publish-appimage.sh          # self-contained folder + AppImage staging
./scripts/publish-appimage.sh --aot    # AOT when toolchain present
```

Optional runtime: `secret-tool` (libsecret) for credentials; Proton or Wine for Windows game EXEs.

Details: [`docs/PACKAGING.md`](docs/PACKAGING.md).

## Extract tool

Install 7-Zip for zip / rar / 7z (+ splits):

```powershell
scoop install 7zip
```

```bash
# Linux — package providing the `7z` CLI (e.g. p7zip)
```

## Login

**Settings → Login** opens Nest OAuth (WebView). Manual `__DLsite_SID` paste remains as a debug fallback. Sync pulls purchases after sign-in.

## Nest import

Settings → Nest import reads **only** `libraryDirectory` / `cacheDirectory` from Nest `settings.xml` (Windows `%APPDATA%\DLsiteNest\`). Credentials are never imported.

## Protocol

`dlsite-client://play/{workno}` — registered on Windows launch; on Linux via user `.desktop` + `xdg-mime`.

## Docs

| Doc | Contents |
|-----|----------|
| [`docs/REQUIREMENTS.md`](docs/REQUIREMENTS.md) | Product requirements |
| [`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md) | Solution layout + data flow |
| [`docs/API.md`](docs/API.md) | Nest HTTP surface notes |
| [`docs/DECISIONS.md`](docs/DECISIONS.md) | ADRs |
| [`docs/MILESTONES.md`](docs/MILESTONES.md) | Milestone status |
| [`docs/PACKAGING.md`](docs/PACKAGING.md) | Windows MSI / Linux AppImage |
