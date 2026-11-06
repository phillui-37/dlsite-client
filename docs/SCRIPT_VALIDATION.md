# Build script validation — 2026-11-07

I checked all eight files in `scripts/`. The contract suite is [script_contract_tests.py](../tests/script_contract_tests.py), runnable with `python -m unittest tests.script_contract_tests -v`. It passed all nine checks on Windows, including PowerShell parsing, Git Bash parsing, the exact eight-script inventory, WebKit guard/relocation checks, MSI test-install boundaries, and runner lock/mount guards.

| Script | Result | Coverage |
| --- | --- | --- |
| `build.ps1` | **Passed** | Ran it. Release build completed for both application target frameworks. |
| `publish-aot.ps1` | **Passed after fix** | Ran it with the host's inherited environment. The first run failed with `MSB4216`: `dotnet` selected the `dotnet-sdk` SDK, while the .NET task host resolved through `dotnet-sdk-lts`. The script now aligns `DOTNET_ROOT`, `DOTNET_ROOT_X64` and `MSBuildSDKsPath` with its selected SDK. An unoverridden rerun produced the win-x64 Native AOT executable (43,830,272 bytes). |
| `publish-msi.ps1` | **Contract checks passed; full build not run** | WiX 7 returned `WIX7015` and required explicit acceptance of its Open Source Maintenance Fee EULA. I left that acceptance and the associated extension installation to the owner. No MSI was produced. |
| `test-msi.ps1` | **Contract checks passed; installer not run** | The checks confirm the test-install path, installed-product guard and uninstall-in-`finally` contract. I did not install or remove a real application. |
| `start-build-vm.ps1` | **Configuration guard passed** | With no `DLSITE_BUILD_VM` configured, the script stops before calling VMware. A full run was blocked: the prepared VM's saved CPU checkpoint could not resume on this host. I preserved the suspended state. |
| `bundle-webkit.py` | **Contract checks passed** | Mocked the Ubuntu version guard and checked dependency, license and ELF relocation contracts. No WebKit bundle was built on Windows. |
| `publish-appimage.sh` | **Guard passed** | Git Bash `-n` passed. An unsupported option returned the expected exit code 2 before touching package output. No AppImage was built. |
| `start-linux-runner.sh` | **Contract checks passed** | Git Bash `-n` passed. Checked lock-before-chroot and guarded-mount source contracts; did not mount a device or start a CI worker. |

The UI checks and output frames are recorded in [UIUX.md](UIUX.md). MSI installation, Linux AppImage packaging and launching a live CI runner remain outside the evidence gathered here.
