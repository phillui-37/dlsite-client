# Packaging

## Windows

| Artifact | Script | Notes |
|----------|--------|-------|
| Native AOT folder | [`scripts/publish-aot.ps1`](../scripts/publish-aot.ps1) | Needs VS C++ / `vcvars64` |
| MSI wizard | [`scripts/publish-msi.ps1`](../scripts/publish-msi.ps1) | WiX 7 x64. Per-user (no UAC) or all users (UAC). Script passes `-acceptEula wix7` (OSMF). Start Menu + Desktop shortcuts |
| Dev build | [`scripts/build.ps1`](../scripts/build.ps1) | `dotnet build -c Release -f net10.0-windows` |

MSI source: [`packaging/windows/Product.wxs`](../packaging/windows/Product.wxs).

Install:

```powershell
.\scripts\publish-msi.ps1
msiexec /i artifacts\msi\DlsiteClient-0.1.0-win-x64.msi
```

The installer asks for the account scope. **Install for me** goes to `%LOCALAPPDATA%\Programs\DlsiteClient` and does not request UAC. **Install for all users** goes to `%ProgramFiles%\DlsiteClient` and requests UAC. A silent all-users install must already be elevated: `msiexec /i <msi> ALLUSERS=1`.

## Linux (M5)

- **AppImage** is the target distribution format (single file, no root).
- Script: [`scripts/publish-appimage.sh`](../scripts/publish-appimage.sh) — publishes `linux-x64` self-contained (or AOT when toolchain present), wraps with [`packaging/linux/AppRun`](../packaging/linux/AppRun) + desktop/icon metadata under `artifacts/appimage/`.
- Runtime deps: Avalonia native deps; optional `secret-tool` (libsecret) for credential store; Proton or Wine to run Windows game EXEs.
- Protocol: `dlsite-client://play/{workno}` via user `.desktop` + `xdg-mime`.
- Tracked under milestone M5 in [`MILESTONES.md`](MILESTONES.md).

## AOT crash note (fixed 2026-10-15)

Double-click with “no window” was **not** a hang — Native AOT crashed on F# `sprintf` (`PrintfImpl` reflection). Use `DlsiteClient.App.Fmt` / `String.Format` only in F# UI.
