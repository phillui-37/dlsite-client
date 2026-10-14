# Packaging

## Windows

| Artifact | Script | Notes |
|----------|--------|-------|
| Native AOT folder | [`scripts/publish-aot.ps1`](../scripts/publish-aot.ps1) | Needs VS C++ / `vcvars64` |
| MSI wizard | [`scripts/publish-msi.ps1`](../scripts/publish-msi.ps1) | WiX 4/5 over AOT payload; Start Menu + Desktop shortcuts |
| Dev build | [`scripts/build.ps1`](../scripts/build.ps1) | `dotnet build -c Release` |

MSI source: [`packaging/windows/Product.wxs`](../packaging/windows/Product.wxs).

Install:

```powershell
.\scripts\publish-msi.ps1
msiexec /i artifacts\msi\DlsiteClient-0.1.0-win-x64.msi
```

## Linux (backlog — M5+)

- **AppImage** is the target distribution format (single file, no root).
- Deferred with Proton/Wine launcher; do not block Windows shipping.
- Tracked under milestone M5+ in [`MILESTONES.md`](MILESTONES.md).

## AOT crash note (fixed 2026-10-15)

Double-click with “no window” was **not** a hang — Native AOT crashed on F# `sprintf` (`PrintfImpl` reflection). Use `DlsiteClient.App.Fmt` / `String.Format` only in F# UI.
