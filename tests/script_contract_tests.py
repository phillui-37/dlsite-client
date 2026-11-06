from __future__ import annotations

import pathlib
import shutil
import subprocess
import sys
import tempfile
import unittest
from unittest import mock

ROOT = pathlib.Path(__file__).resolve().parents[1]
SCRIPTS = ROOT / "scripts"


class ScriptContracts(unittest.TestCase):
    def test_inventory_is_exactly_eight_scripts(self):
        self.assertEqual(8, len(list(SCRIPTS.iterdir())))
        self.assertEqual(
            {
                "build.ps1", "publish-aot.ps1", "publish-msi.ps1", "test-msi.ps1",
                "start-build-vm.ps1", "bundle-webkit.py", "publish-appimage.sh",
                "start-linux-runner.sh",
            },
            {p.name for p in SCRIPTS.iterdir()},
        )

    def test_bundle_webkit_rejects_wrong_platform_before_touching_app(self):
        import runpy

        app = pathlib.Path(tempfile.gettempdir()) / "must-not-be-created-by-contract-test"
        with mock.patch.object(pathlib.Path, "read_text", return_value='VERSION_ID="22.04"'):
            with mock.patch.object(sys, "argv", [str(SCRIPTS / "bundle-webkit.py"), str(app)]):
                with self.assertRaisesRegex(SystemExit, "Ubuntu 24.04"):
                    runpy.run_path(str(SCRIPTS / "bundle-webkit.py"), run_name="__main__")
        self.assertFalse(app.exists())

    def test_bundle_webkit_encodes_dependency_license_and_relocation_contracts(self):
        source = (SCRIPTS / "bundle-webkit.py").read_text(encoding="utf-8")
        self.assertIn("if 'VERSION_ID=\"24.04\"'", source)
        self.assertIn("'not found' in result.stdout", source)
        self.assertIn("packages.tsv", source)
        self.assertIn("copyright", source)
        self.assertIn("for old, new in", source)
        self.assertIn("len(new) > len(old)", source)
        self.assertIn("return value + b'\\0'", source)

    def test_bash_scripts_parse_with_git_bash(self):
        git_core = subprocess.run(["git", "--exec-path"], text=True, capture_output=True, check=True).stdout.strip()
        bash = str(pathlib.Path(git_core).parents[2] / "bin" / "bash.exe")
        if not pathlib.Path(bash).is_file():
            self.skipTest("Git Bash unavailable; no shell syntax claim")
        for name in ("publish-appimage.sh", "start-linux-runner.sh"):
            with self.subTest(script=name):
                win_path = str(SCRIPTS / name)
                if len(win_path) > 1 and win_path[1] == ":":
                    win_path = "/" + win_path[0].lower() + win_path[2:].replace("\\", "/")
                result = subprocess.run([bash, "-n", win_path], text=True, capture_output=True)
                self.assertEqual(0, result.returncode, result.stderr)

    def test_powerShell_scripts_parse_without_execution(self):
        pwsh = shutil.which("pwsh")
        if not pwsh:
            self.skipTest("PowerShell 7 unavailable; no parser claim")
        for path in sorted(SCRIPTS.glob("*.ps1")):
            with self.subTest(script=path.name):
                # Parser API only: no script body is invoked and no publisher is run.
                command = (
                    "$e=$null; [System.Management.Automation.Language.Parser]::ParseFile('',"
                    "[ref]$null,[ref]$e) | Out-Null; if($e.Count){$e | ForEach-Object {Write-Error $_}; exit 1}"
                ).replace("\u001f", str(path).replace("'", "''"))
                result = subprocess.run([pwsh, "-NoProfile", "-Command", command], text=True, capture_output=True)
                self.assertEqual(0, result.returncode, result.stderr)

    def test_powershell_publishers_have_process_failure_guards(self):
        for name in ("build.ps1", "publish-aot.ps1", "publish-msi.ps1", "start-build-vm.ps1", "test-msi.ps1"):
            source = (SCRIPTS / name).read_text(encoding="utf-8")
            with self.subTest(script=name):
                self.assertIn("ErrorActionPreference", source)
                if name != "test-msi.ps1":
                    self.assertIn("$LASTEXITCODE", source)
                else:
                    self.assertIn("$process.ExitCode", source)

    def test_runner_shell_uses_lock_before_chroot_and_guards_host_mounts(self):
        source = (SCRIPTS / "start-linux-runner.sh").read_text(encoding="utf-8")
        self.assertIn("flock -n 9", source)
        self.assertIn("mountpoint -q", source)
        self.assertIn("exec chroot", source)
        self.assertLess(source.index("flock -n 9"), source.index("exec chroot"))

    def test_appimage_script_requires_existing_payload_for_skip_publish(self):
        source = (SCRIPTS / "publish-appimage.sh").read_text(encoding="utf-8")
        self.assertIn('test -f "${OUT}/DlsiteClient.App"', source)
        self.assertIn("--skip-publish)", source)
        self.assertIn("sha256sum --check --status", source)
        self.assertIn("trap 'rm -f \"$TEMP_IMAGE\"' EXIT", source)

    def test_msi_maintenance_script_scopes_mutations_to_test_install(self):
        source = (SCRIPTS / "test-msi.ps1").read_text(encoding="utf-8")
        self.assertIn('"artifacts\\msi-test-install"', source)
        self.assertIn("RelatedProducts", source)
        self.assertIn("SkipIfInstalled", source)
        self.assertIn("finally", source)
        self.assertIn('Run-Msi "/x $product"', source)


if __name__ == "__main__":
    unittest.main(verbosity=2)
