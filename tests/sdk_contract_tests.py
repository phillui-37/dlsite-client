import json
from pathlib import Path
import unittest


ROOT = Path(__file__).resolve().parents[1]
SDK = json.loads((ROOT / "global.json").read_text(encoding="utf-8"))["sdk"]
TFM = "net" + ".".join(SDK["version"].split(".")[:2])


class SdkContracts(unittest.TestCase):
    def test_build_and_publish_follow_repo_sdk(self):
        for name, aot in (("build.ps1", "false"), ("publish-aot.ps1", "true")):
            with self.subTest(script=name):
                script = (ROOT / "scripts" / name).read_text(encoding="utf-8")
                self.assertIn(f"-f {TFM}-windows", script)
                self.assertIn(f"-p:PublishAot={aot}", script)
        appimage = (ROOT / "scripts/publish-appimage.sh").read_text(encoding="utf-8")
        self.assertIn(f'TFM="{TFM}"', appimage)
        self.assertRegex(appimage, r'APPIMAGETOOL_VERSION="\d+\.\d+\.\d+"')
        self.assertIn('releases/download/${APPIMAGETOOL_VERSION}/', appimage)

    def test_both_ci_jobs_select_repo_sdk_before_running_dotnet(self):
        workflow = (ROOT / ".github/workflows/ci.yml").read_text(encoding="utf-8")
        for job_name, next_job in (("windows", "linux"), ("linux", "release")):
            with self.subTest(job=job_name):
                job = workflow.split(f"\n  {job_name}:\n", 1)[1].split(f"\n  {next_job}:\n", 1)[0]
                self.assertLess(job.index("global-json-file: global.json"), job.index("dotnet run"))


if __name__ == "__main__":
    unittest.main()
