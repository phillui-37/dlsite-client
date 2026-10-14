# DLsite Client

Avalonia.FuncUI (F#) + .NET 10 Nest replacement. Docs in `docs/` are the source of truth (`AGENTS.md`).

## Build / publish

```powershell
.\scripts\build.ps1
.\scripts\publish-aot.ps1          # needs VS C++ / vcvars64
.\scripts\publish-msi.ps1          # WiX MSI wizard over AOT payload
dotnet run --project src/DlsiteClient.App -c Release -f net10.0-windows
```

See [`docs/PACKAGING.md`](docs/PACKAGING.md). Linux AppImage is M5+ backlog.

## Extract tool

Install 7-Zip for zip/rar/7z (+ splits):

```powershell
scoop install 7zip
```

## Login

Settings → paste `__DLsite_SID` from a Nest/browser session (OAuth WebView broker is next iteration) → Sync.

## Nest import

Settings → Nest import reads **only** `libraryDirectory` / `cacheDirectory` from `%APPDATA%\DLsiteNest\settings.xml`. No credentials.

## Protocol

`dlsite-client://play/{workno}` registered on Windows launch.
