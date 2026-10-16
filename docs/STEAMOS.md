# SteamOS validation — 2026-10-17

## Result

Current checks pass: 48 Windows core checks, 62 Linux core checks, and 33 headless UI checks on each platform. GitHub CI runs on this PC and its VMware SteamOS VM; Linux builds use Ubuntu 24.04 userspace. Native WebKit login-page rendering and real Proton/Wine executable launches were checked in the recovery desktop. The earlier runtime AppImage passed account login, 683-purchase sync, Secret Service storage, session restore, logout and re-login. This recovery VM does not certify a physical Steam Deck or Gaming Mode.

## Fixed

- Extraction stages updates, retains the full original installation as a backup, preserves local files and known saves, and rolls back a failed replacement rename. Corrupt archives and cancellation leave the live installation unchanged. Cache cleanup does not remove backups. See [backup policy](PACKAGING.md#update-backups).
- Linux discovers executable ELF files and scripts, plus case-insensitive EXEs. It skips symlinks and known Windows helpers.
- Protocol and autostart entries use the persistent AppImage path and XDG directories. A new XDG config directory is created before MIME registration.
- The AppImage includes static 7-Zip, WebKitGTK 4.1, helper processes, TLS modules, GTK resources and dependency libraries. Release WebKit helper paths are relocated. Package versions, source references, licenses and the relocation script are included.
- A separate private Mesa software stack supports machines without a DRM render node. Avalonia uses X11 software rendering on those machines. Hardware with render nodes uses its normal rendering path and host GPU libraries.
- Game and host-tool launches restore the environment that existed before AppRun added private libraries and GTK settings.
- Session values use desktop Secret Service. The package includes secret-tool and a KDE activation helper. Failed storage is a login error; it does not commit the in-memory session. OIDC exchange alone does not publish a session.
- Proton discovery includes custom compatibility tools and external Steam library paths. umu is preferred when present. Direct modern Proton uses the runtime required by toolmanifest.vdf. Missing runtimes produce a localized error. Direct runtime launches expose external game, tool, runtime and compatibility directories. Wine and Proton prefixes stay outside the game installation.
- Linux startup avoids Windows apartment-state calls. Launch IO runs off the UI thread; failures are shown in the application.

## Runtime tests

- Actual FUSE-mounted AppImage: library, settings and native account login passed at 1280 × 800. Extract-and-run also starts and restores the session. Protocol and autostart registration name the persistent AppImage file. Autostart was disabled after its check.
- Private WebKitGTK 2.52.6: the public vivion ID login page renders in the SteamOS framebuffer desktop, without host WebKit installation.
- GE-Proton 11-9, umu 1.4.4 and Steam Runtime 4 (`4.0.20261007.263973`): an owned Windows PE probe runs and exits with status 0. Japanese locale is set.
- Direct GE-Proton through its required Steam runtime also runs the same probe. The test uses an isolated dummy Steam library manifest.
- Wine fallback through GE's Wine and Steam Runtime 4 runs the probe. Repeated launches retain test saves. Captured child library paths contain runtime overrides, not AppImage libraries.
- The standard Proton prefix layout is shared by umu and direct Proton. A retained save marker survives reuse.
- A hung credential helper was tested past the 60-second deadline. Lookup returns no session; it does not throw a startup error. Credential-save failure tests still reject login without committing the API session.
- The Windows MSI per-user install, repair, removal, data retention and 0.1.0-to-0.1.1 upgrade passed in earlier checks. Non-admin all-user install is refused. Elevated installation remains untested.

These executable tests do not establish compatibility with each commercial game, DRM, codecs, audio or GPU workloads.

## Remaining hardware checks

1. Physical Steam Deck: Mesa/GPU, Gamescope and Wayland behavior.
2. Gaming Mode: controller-only navigation, on-screen keyboard and focus behavior.
3. Representative real games: Japanese text, audio/video, DRM and save paths.
4. Real desktop wallet unlock and persistence across a full reboot. The recovery VM starts Xorg/KWin manually; it is not a normal Plasma login session.

## Local VM

- VMware Workstation Pro 26.0.1; `artifacts/steamos-vm/SteamOS-Test.vmx`.
- Four virtual CPUs, 8 GiB RAM, EFI, NAT and an isolated 64 GiB virtual NVMe test disk. No host physical disks or shared game libraries are attached.
- Valve recovery image: [official download](https://store.steampowered.com/steamos/download/?ver=steamdeck). Archive SHA-256: `4254ee02ec34ae8add9aceef1881a2ce675a9d0176171df92e0eaa1bf014c594`.
- No VMware Tools or vmwgfx kernel module is available in the recovery environment. Xorg framebuffer plus KWin provides a 1280 × 800 test desktop. Private Mesa supplies WebKit software rendering.
- An isolated Ubuntu 24.04 root filesystem builds the package. This does not replace SteamOS or install runtime packages on its root filesystem.
- Read-only virtual CDs supply build inputs. A temporary boot service provides the serial test agent. Logs and conversation records stay local.
- Test desktop activation exports DISPLAY and the session bus to D-Bus. A normal Plasma session performs this setup itself.
- Account tests used explicitly supplied credentials. A fresh encrypted KDE test wallet was created. The credential text file and temporary guest input files were deleted after login/logout/re-login checks. The final session was logged out and the newly created test wallet was removed. No password is recorded in this report.

## Re-run checks

```sh
dotnet run --project tests/DlsiteClient.RegressionTests -c Release
```

For a guest without the SDK, publish the test project for `linux-x64` as self-contained, make the launcher executable, and run it with 7-Zip on PATH. Build the AppImage on Ubuntu 24.04 with the dependencies listed in [packaging](PACKAGING.md#linux-runtime-build).

## Earlier runtime validation artifact

- Path: `artifacts/appimage/DlsiteClient-linux-x64.AppImage`.
- Size: 163858936 bytes.
- SHA-256: `f37967751298ac96396188ac148c11ecadf510adeaf0babc4bca8587daa2a0d8`.
- Host and guest hashes match. `artifacts/appimage/SHA256SUMS` contains the check line.
- The VM was shut down after the tests. Its test boot agent and payload CDs remain available for another run.

## Later source change: Windows-only Nest import

The app now excludes Nest import at compile time from its Linux target. Both Release targets build; metadata checks confirm that Linux has no import module, service field or import UI key, and no Windows platform reference. The Linux dependency manifest also excludes the Windows project. Windows retains the import feature and excludes the Linux platform reference. The AppImage listed above predates this source change and has not been rebuilt for it.

## Later source changes: metadata and game-start status

Added update/tag filtering, public genre fallback, detail folder opening and launch state with a circular indicator. The new monitor has a Wine argv branch, but that branch has not been tested in the SteamOS VM. Windows validation passes 48 core checks and 33 headless UI checks; a real subprocess verifies cross-instance lease exclusion. The live public API and compiled HTML parser returned eight matching genre names. Both application targets build. The packaged AppImage above predates these changes.

## 0.1.2 first-run package check

A fresh FUSE-mounted AppImage starts at 1280 x 800, creates its absent XDG config directory outside the read-only mount, and shows library and settings. Its private WebKit renders the vivion ID login form. This check entered no credentials; the earlier live login/storage/restore/logout checks remain separate.

The check found and fixed two absent-directory paths: Linux app data and the shared game-launch lease directory now use SpecialFolderOption.DoNotVerify before directory creation. Linux regression coverage is now 62 checks. CI also fixed an early-exit Secret Service pipe error. Native Windows-only Nest import remains excluded from the Linux package. Commercial-game process tracking, physical Deck and Gaming Mode checks remain open.
