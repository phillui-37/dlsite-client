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
msiexec /i artifacts\msi\DlsiteClient-0.1.2-win-x64.msi
```

The installer asks for the account scope. **Install for me** goes to `%LOCALAPPDATA%\Programs\DlsiteClient` and does not request UAC. **Install for all users** goes to `%ProgramFiles%\DlsiteClient` and requests UAC. A silent all-users install must already be elevated: `msiexec /i <msi> ALLUSERS=1`.

## Linux (M5)

- **AppImage** is the target distribution format (single file, no root).
- Script: [`scripts/publish-appimage.sh`](../scripts/publish-appimage.sh) — publishes `linux-x64` self-contained (or AOT when toolchain present), wraps with [`packaging/linux/AppRun`](../packaging/linux/AppRun) + desktop/icon metadata under `artifacts/appimage/`.
- Runtime deps: Avalonia native deps; a desktop Secret Service (KWallet or GNOME Keyring) for credential storage; Proton or Wine to run Windows game EXEs.
- Protocol: `dlsite-client://play/{workno}` via user `.desktop` + `xdg-mime`.
- Tracked under milestone M5 in [`MILESTONES.md`](MILESTONES.md).

## AOT crash note (fixed 2026-10-15)

Double-click with “no window” was **not** a hang — Native AOT crashed on F# `sprintf` (`PrintfImpl` reflection). Use `DlsiteClient.App.Fmt` / `String.Format` only in F# UI.

## Installer and update checks (2026-10-16)

- The MSI uses the standard WiX InstallDir wizard with a scope page. Welcome, license, destination, confirmation, progress, completion, cancellation, repair, and removal use WiX dialogs.
- MSI upgrades use transactional removal (`afterInstallInitialize`). The build no longer modifies MSI SummaryInformation. The previous UAC flag patch caused Windows Installer to ignore `MSIINSTALLPERUSER`.
- MSI publishing rebuilds the AOT payload by default. `-SkipPublish` reuses an already verified payload.
- MSI includes `tools/7z.exe`, `tools/7z.dll`, and the 7-Zip license. Building requires a local 7-Zip install. The AppImage script bundles the official static Linux decoder (26.04) with its published SHA-256 and license.
- Run `dotnet run --project tests/DlsiteClient.RegressionTests -c Release` for install integrity, cancellation, queue and platform checks. No account or game library is needed.
- Run `scripts/test-msi.ps1` to test install, repair, removal, payload and user-data retention in `artifacts/msi-test-install`. An elevated PowerShell is needed to test the all-user path. Without it, the script checks that a silent all-user install is refused.
- A 0.1.0 test baseline upgraded to 0.1.1 successfully. The previous registration was removed and a test save remained.

## Update backups

The previous install is retained under `{parent of install}/.dlsite-client/backups/{transaction id}`. The adjacent `.origin.txt` records the original path. Update and cache cleanup never remove these backups. Files absent from the new archive and known save files are copied into staging; all original files, including conflicts, remain in the backup. This uses additional disk space. If power fails between directory renames, the backup remains available for manual recovery.

### Linux runtime build

Build the AppImage on Ubuntu 24.04 amd64. Install `libwebkit2gtk-4.1-0 glib-networking gstreamer1.0-plugins-base libglib2.0-bin libsecret-tools dbus-bin libegl-mesa0 libgl1-mesa-dri desktop-file-utils python3 curl file squashfs-tools` first. Use `scripts/publish-appimage.sh`; use `--skip-publish` only with an existing verified Linux publish folder. The script fetches a SHA-256-pinned appimagetool if it is not on PATH. A changed upstream continuous asset fails the hash check.

`scripts/bundle-webkit.py` includes WebKit helpers, dependency closure, GIO TLS modules, GTK input modules, image loaders, schemas, resources and package copyright files. It excludes glibc and host GPU drivers. A separate Mesa software renderer is selected only when no DRM render node exists. The bundled Secret Service wrapper can activate KDE Secret Service compatibility support. It relocates compiled WebKit helper paths and keeps the AppDir as the app working directory. `AppRun` makes mount-specific GTK caches and records host environment values. Linux game and desktop-tool launches restore those values. Source-package versions and the relocation script are included under `usr/share/licenses/webkit-runtime`.

The runtime baseline is Ubuntu 24.04, not a promise of compatibility with older Linux releases. SteamOS runtime checks are in `docs/STEAMOS.md`.

## GitHub Actions on this PC

The Local CI and release workflow uses the repository runner with labels self-hosted, Windows, X64, dlsite-build. It runs core and headless UI checks, builds the Native AOT MSI, checks MSI maintenance, and starts the Linux build VM. It runs on main/release-branch pushes, tags and manual dispatch. Pull-request events do not run on this personal host.

The Windows runner is installed outside the checkout in D:/actions-runner/dlsite-client. It starts hidden at this account's sign-in. The account must remain signed in for builds. GitHub runner credentials stay outside this repository.

The Linux runner uses label dlsite-appimage and /opt/actions-runner inside the VM's Ubuntu 24.04 chroot. Install .NET SDK 10, Git, WebKitGTK and the packaging dependencies there. The host VM must contain /mnt/dlsite-test/start-actions-runner.sh (from scripts/start-linux-runner.sh) and the prepared serial agent. Set the repository variable DLSITE_BUILD_VM to its absolute .vmx path. scripts/start-build-vm.ps1 starts the VM and runner; scripts/start-linux-runner.sh prevents a second runner with flock. The Linux job runs core/UI checks, publishes and packages the AppImage, then checks the bundled runtime. Tags v* publish both artifacts and SHA256SUMS.txt only after both jobs pass. The VM build runs in Ubuntu userspace; SteamOS desktop/login validation is a separate check.

The SteamOS recovery build VM needs a persistent dlsite-build-agent.service in /etc/systemd/system. It starts /etc/dlsite-build-agent.sh, mounts the agent ISO on /run/dlsite-payload, and runs agent.sh. Do not depend on temporary systemd.run kernel arguments: recovery rewrites grub.cfg. A complete power-off/start test passed with the persistent service. The Ubuntu chroot also needs the 7zip and xdg-utils packages for core regression checks, in addition to the bundled release decoder.

MSI checks enumerate the package's entire UpgradeCode family before installation. Manual execution refuses a host with a related installed or advertised product. CI uses -SkipIfInstalled and records a notice instead of upgrading a personal installation. Use a clean account or VM to run maintenance checks in that case. The initial release validation on this PC ran the maintenance checks with no related product installed.
