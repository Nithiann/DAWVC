"""Guard/failure tests for acceptance tooling; no DAWVC build needed."""
from contextlib import redirect_stderr, redirect_stdout
import hashlib
import io
import json
import os
from pathlib import Path
import sys
import tempfile
import unittest
import zipfile

from test_preview_acceptance import AcceptanceFailure, Runner, main, unpack_verified


class AcceptanceGuardTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name)

    def bundle(self, names):
        archive = self.root / "release.zip"
        with zipfile.ZipFile(archive, "w") as output:
            for name in names:
                if isinstance(name, zipfile.ZipInfo):
                    output.writestr(name, b"private fixture")
                else:
                    output.writestr(name, b"fixture")
        checksum = self.root / "SHA256SUMS.txt"
        checksum.write_text(hashlib.sha256(archive.read_bytes()).hexdigest() + "  release.zip\n", encoding="utf-8-sig")
        return archive, checksum

    def test_checksum_mismatch_refuses_before_extraction(self):
        archive, checksum = self.bundle(["dawvc.exe"])
        archive.write_bytes(archive.read_bytes() + b"tampering")
        with self.assertRaises(AcceptanceFailure):
            unpack_verified(archive, checksum, self.root / "extract")
        self.assertFalse((self.root / "extract").exists())

    def test_ambiguous_checksums_are_rejected(self):
        archive, checksum = self.bundle(["dawvc.exe"])
        text = checksum.read_text(encoding="utf-8-sig")
        checksum.write_text(text + text)
        with self.assertRaises(AcceptanceFailure):
            unpack_verified(archive, checksum, self.root / "extract")

    def test_unsafe_paths_rejected_before_any_member_is_written(self):
        for name in ("../outside", "/absolute", "C:/outside", "folder/.. /outside", "folder./outside", "DAWVC.EXE", "NUL", "COM1.txt", "folder/CONOUT$"):
            with self.subTest(name=name):
                archive, checksum = self.bundle(["dawvc.exe", name])
                with self.assertRaises(AcceptanceFailure):
                    unpack_verified(archive, checksum, self.root / "extract")
                self.assertFalse((self.root / "extract").exists())

    def test_symlink_archive_member_rejected(self):
        link = zipfile.ZipInfo("link")
        link.create_system = 3
        link.external_attr = 0o120777 << 16
        archive, checksum = self.bundle(["dawvc.exe", link])
        with self.assertRaises(AcceptanceFailure):
            unpack_verified(archive, checksum, self.root / "extract")

    def test_archive_entry_budget_refuses_before_extraction(self):
        archive, checksum = self.bundle(["dawvc.exe"] + [f"entry-{number}" for number in range(64)])
        with self.assertRaises(AcceptanceFailure):
            unpack_verified(archive, checksum, self.root / "extract")
        self.assertFalse((self.root / "extract").exists())

    def test_valid_archive_and_bom_checksum_extract_expected_bytes(self):
        archive, checksum = self.bundle(["dawvc.exe", "README.md"])
        actual = unpack_verified(archive, checksum, self.root / "extract")
        self.assertEqual(actual, hashlib.sha256(archive.read_bytes()).hexdigest())
        self.assertEqual((self.root / "extract/dawvc.exe").read_bytes(), b"fixture")

    def test_expected_nonzero_exit_is_a_pass_without_exporting_output(self):
        report = {"checks": []}
        runner = Runner([sys.executable, "-c", "import sys; print('private path'); sys.exit(9)"], report, os.environ.copy())
        runner.run("refusal", expected=9)
        self.assertEqual(report["checks"][0]["status"], "passed")
        self.assertNotIn("private path", json.dumps(report))

    def test_unexpected_exit_fails_with_machine_readable_evidence(self):
        report = {"checks": []}
        runner = Runner([sys.executable, "-c", "raise SystemExit(3)"], report, os.environ.copy())
        with self.assertRaises(AcceptanceFailure):
            runner.run("failure")
        self.assertEqual(report["checks"][0]["actual_exit"], 3)

    def test_timeout_fails_instead_of_hanging(self):
        report = {"checks": []}
        runner = Runner([sys.executable, "-c", "import time; time.sleep(10)"], report, os.environ.copy(), timeout=0.1)
        with self.assertRaises(AcceptanceFailure):
            runner.run("timeout")
        self.assertEqual(report["checks"][0]["reason"], "timeout")

    def test_existing_repository_cannot_be_used_as_hosting_parent(self):
        (self.root / "dawvc.yaml").write_text("private repository sentinel")
        with redirect_stderr(io.StringIO()), self.assertRaises(SystemExit) as failure:
            main(["--cli", sys.executable, "--remote-parent", str(self.root), "--report", str(self.root / "evidence.json")])
        self.assertEqual(failure.exception.code, 2)
        self.assertEqual((self.root / "dawvc.yaml").read_text(), "private repository sentinel")
        self.assertFalse((self.root / "evidence.json").exists())

    def test_missing_binary_writes_failed_report_without_private_paths(self):
        report = self.root / "evidence.json"
        with redirect_stdout(io.StringIO()):
            result = main(["--cli", str(self.root / "private-missing-executable"), "--report", str(report)])
        evidence = json.loads(report.read_text())
        self.assertEqual(result, 1)
        self.assertEqual(evidence["status"], "failed")
        self.assertEqual(evidence["failure"], "FileNotFoundError")
        self.assertNotIn(str(self.root), report.read_text())
        self.assertFalse(evidence["real_fl_studio_validated"])


if __name__ == "__main__":
    unittest.main()
