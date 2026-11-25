"""Synthetic CLI acceptance; standard-library only, never opens FL Studio.

Runs isolated working copies on ONE host. Optional --remote-parent exercises a
mounted filesystem/UNC destination, not SSH or a real multi-machine session.
"""
import argparse
from datetime import datetime, timezone
import hashlib
import json
import os
from pathlib import Path, PurePosixPath
import platform
import shutil
import stat
import struct
import subprocess
import sys
import tempfile
import time
import wave
import zipfile


class AcceptanceFailure(Exception):
    pass


def digest(path):
    with Path(path).open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest()


def unpack_verified(archive, checksums, destination):
    archive = Path(archive)
    records = []
    for line in Path(checksums).read_text(encoding="utf-8-sig").splitlines():
        fields = line.split(maxsplit=1)
        if len(fields) == 2 and fields[1].lstrip(" *") == archive.name:
            records.append(fields[0].lower())
    actual = digest(archive)
    if records != [actual]:
        raise AcceptanceFailure("Archive checksum is missing, ambiguous or does not match.")
    with zipfile.ZipFile(archive) as bundle:
        entries = bundle.infolist()
        if len(entries) > 64 or sum(e.file_size for e in entries) > 512 * 1024 * 1024:
            raise AcceptanceFailure("Archive exceeds acceptance extraction limits.")
        names = set()
        devices = {"CON", "PRN", "AUX", "NUL", "CONIN$", "CONOUT$"}
        devices.update(prefix + suffix for prefix in ("COM", "LPT") for suffix in "123456789¹²³")
        for entry in entries:
            path = PurePosixPath(entry.filename.replace("\\", "/"))
            key = str(path).casefold()
            if (path.is_absolute() or not path.parts or ".." in path.parts
                    or any(":" in part or part.rstrip(" .") != part or part.split(".")[0].upper() in devices for part in path.parts)
                    or key in names or entry.flag_bits & 1
                    or entry.compress_type not in (zipfile.ZIP_STORED, zipfile.ZIP_DEFLATED)
                    or stat.S_ISLNK(entry.external_attr >> 16)):
                raise AcceptanceFailure("Unsafe or colliding archive path.")
            names.add(key)
        if "dawvc.exe" not in names:
            raise AcceptanceFailure("Archive has no root dawvc.exe.")
        # Validate every path before writing any member.
        for entry in entries:
            target = destination.joinpath(*PurePosixPath(entry.filename.replace("\\", "/")).parts)
            if entry.is_dir():
                target.mkdir(parents=True, exist_ok=True)
                continue
            target.parent.mkdir(parents=True, exist_ok=True)
            with bundle.open(entry) as source, target.open("xb") as output:
                shutil.copyfileobj(source, output)
    return actual


class Runner:
    def __init__(self, command, report, environment, timeout=60):
        self.command, self.report, self.environment, self.timeout = command, report, environment, timeout

    def run(self, label, *args, expected=0):
        started = time.monotonic()
        try:
            result = subprocess.run(self.command + list(map(str, args)), env=self.environment,
                                    capture_output=True, text=True, encoding="utf-8", errors="replace",
                                    timeout=self.timeout)
        except subprocess.TimeoutExpired as error:
            self.report["checks"].append({"name": label, "status": "failed", "reason": "timeout"})
            raise AcceptanceFailure(label + ": timed out.") from error
        passed = result.returncode == expected
        self.report["checks"].append({"name": label, "status": "passed" if passed else "failed",
                                      "expected_exit": expected, "actual_exit": result.returncode,
                                      "elapsed_seconds": round(time.monotonic() - started, 3)})
        if not passed:
            # Do not export captured project paths, native inspection output or credentials.
            raise AcceptanceFailure(f"{label}: expected exit {expected}, received {result.returncode}.")
        return result.stdout.strip()

    def equal(self, label, actual, expected):
        passed = actual == expected
        self.report["checks"].append({"name": label, "status": "passed" if passed else "failed"})
        if not passed:
            raise AcceptanceFailure(label + ": content/state mismatch.")


def write_sample(path, value):
    with wave.open(str(path), "wb") as output:
        output.setparams((1, 2, 44100, 0, "NONE", "not compressed"))
        output.writeframes(struct.pack("<128h", *([value] * 128)))


def workflow(runner, root, remote):
    local, other = root / "Producer A", root / "Producer B"
    local.mkdir()
    # Controlled minimal FLP envelope, intentionally opaque/legacy; not a real DAW acceptance fixture.
    data = bytes([199, 11]) + b"25.2.5.5319"
    project = bytes.fromhex("464c68640600000000000a006000") + b"FLdt" + struct.pack("<I", len(data)) + data
    (local / "Song.flp").write_bytes(project)
    write_sample(local / "Recording.wav", 100)
    runner.run("init", "init", "--dir", local, "--primary", "Song.flp")
    runner.run("stage recording", "add", "Recording.wav", "--dir", local)
    runner.run("commit baseline", "commit", "-m", "Acceptance baseline", "--dir", local)
    runner.run("provision bare receiver", "init", "--bare", "--from", local, "--dir", remote)
    runner.run("configure origin", "remote", "add", "origin", remote, "--dir", local)
    runner.run("first push", "push", "origin", "main", "--dir", local)
    runner.run("clone collaborator", "clone", remote, other)
    runner.equal("cloned FLP byte identity", (other / "Song.flp").read_bytes(), project)
    runner.equal("cloned recording byte identity", digest(other / "Recording.wav"), digest(local / "Recording.wav"))
    write_sample(local / "Recording.wav", 200)
    local_recording = (local / "Recording.wav").read_bytes()
    runner.run("commit local version", "commit", "-m", "Local recording", "--dir", local)
    write_sample(other / "Recording.wav", 300)
    incoming_recording = (other / "Recording.wav").read_bytes()
    runner.run("commit incoming version", "commit", "-m", "Incoming recording", "--dir", other)
    runner.run("push incoming version", "push", "origin", "main", "--dir", other)
    local_ref = local / ".dawvc/refs/heads/main"
    local_tip = local_ref.read_text().strip()
    runner.run("reject diverged push", "push", "origin", "main", "--dir", local, expected=9)
    runner.run("fetch incoming history", "fetch", "origin", "--dir", local)
    incoming_tip = (local / ".dawvc/refs/remotes/origin/main").read_text().strip()
    # Force is deliberately absent. Dirty refusal must preserve both user bytes and branch.
    write_sample(local / "Recording.wav", 400)
    dirty = (local / "Recording.wav").read_bytes()
    runner.run("reject dirty merge", "merge", "origin/main", "--take", "incoming", "-m", "Must refuse", "--dir", local, expected=4)
    runner.equal("dirty recording preserved", (local / "Recording.wav").read_bytes(), dirty)
    runner.equal("dirty merge preserves branch", local_ref.read_text().strip(), local_tip)
    (local / "Recording.wav").write_bytes(local_recording)
    runner.run("explicit incoming merge", "merge", "origin/main", "--take", "incoming", "-m", "Choose incoming recording", "--dir", local)
    runner.equal("selected FLP byte identity", (local / "Song.flp").read_bytes(), project)
    runner.equal("selected recording byte identity", (local / "Recording.wav").read_bytes(), incoming_recording)
    merge_tip = local_ref.read_text().strip()
    runner.equal("merge advances branch", merge_tip != local_tip and merge_tip != incoming_tip, True)
    for label, tip, recording in (("local", local_tip, local_recording), ("incoming", incoming_tip, incoming_recording)):
        restored = root / ("Restored " + label)
        runner.run("restore " + label + " parent", "checkout", tip, "--restore-to", restored, "--dir", local)
        runner.equal(label + " parent FLP preserved", (restored / "Song.flp").read_bytes(), project)
        runner.equal(label + " parent recording preserved", (restored / "Recording.wav").read_bytes(), recording)
    runner.run("push merge", "push", "origin", "main", "--dir", local)
    runner.run("pull through second parent", "pull", "origin", "main", "--dir", other)
    runner.equal("collaborator reaches merge", (other / ".dawvc/refs/heads/main").read_text().strip(), merge_tip)
    runner.equal("collaborator recording preserved", (other / "Recording.wav").read_bytes(), incoming_recording)
    for label, path in (("producer", local), ("collaborator", other)):
        runner.run(label + " fsck", "fsck", "--dir", path)
    fresh = root / "Fresh after merge"
    runner.run("fresh clone of merged history", "clone", remote, fresh)
    restored_local = root / "Restored local from fresh clone"
    runner.run("restore local parent after transfer", "checkout", local_tip, "--restore-to", restored_local, "--dir", fresh)
    runner.equal("local parent FLP survives transfer", (restored_local / "Song.flp").read_bytes(), project)
    runner.equal("local parent recording survives transfer", (restored_local / "Recording.wav").read_bytes(), local_recording)
    mirror = root / "Bare copy"
    runner.run("bare clone", "clone", remote, mirror, "--bare")
    runner.equal("bare clone has no workspace", (mirror / ".dawvc").exists() or (mirror / "Song.flp").exists(), False)
    runner.equal("bare clone retains merged tip", (mirror / "refs/heads/main").read_text().strip(), merge_tip)


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    mode = parser.add_mutually_exclusive_group(required=True)
    mode.add_argument("--archive", type=Path, help="Windows release ZIP; requires --checksums")
    mode.add_argument("--cli", type=Path, help="Explicit native executable")
    mode.add_argument("--dll", type=Path, help="Framework-dependent CLI DLL; requires --dotnet")
    parser.add_argument("--dotnet", type=Path)
    parser.add_argument("--checksums", type=Path)
    parser.add_argument("--remote-parent", type=Path, help="Existing operator-owned filesystem/UNC parent; never an existing repository")
    parser.add_argument("--report", type=Path, required=True)
    args = parser.parse_args(argv)
    if args.archive and (not args.checksums or os.name != "nt"):
        parser.error("--archive requires --checksums and Windows execution")
    if args.dll and not args.dotnet:
        parser.error("--dll requires an explicit --dotnet executable")
    if args.remote_parent:
        if not args.remote_parent.is_dir():
            parser.error("--remote-parent must be an existing directory")
        args.remote_parent = args.remote_parent.resolve(strict=True)
        if (args.remote_parent / ".dawvc").exists() or (args.remote_parent / "dawvc.yaml").exists():
            parser.error("--remote-parent must be a hosting parent, not an existing repository")
    report = {"schema_version": 1, "status": "failed", "started_utc": datetime.now(timezone.utc).isoformat(), "platform": platform.system(),
              "architecture": platform.machine(), "fixture": "synthetic_opaque_flp_and_generated_pcm",
              "workstations": "two_directories_on_one_host", "transport": "filesystem_external_parent" if args.remote_parent else "filesystem_local",
              "real_fl_studio_validated": False, "real_multi_machine_validated": False,
              "ssh_validated": False, "checks": []}
    try:
        with tempfile.TemporaryDirectory(prefix="dawvc-preview-") as temporary:
            root = Path(temporary)
            environment = os.environ.copy()
            if args.archive:
                report["archive_sha256"] = unpack_verified(args.archive, args.checksums, root / "package")
                executable = root / "package/dawvc.exe"
                command = [str(executable)]
            elif args.cli:
                executable = args.cli.resolve(strict=True)
                command = [str(executable)]
            else:
                executable = args.dll.resolve(strict=True)
                command = [str(args.dotnet.resolve(strict=True)), str(executable)]
            report["binary_sha256"] = digest(executable)
            report["execution"] = "dotnet_dll" if args.dll else "native_executable"
            if not args.dll:
                # Exercise the native distribution with invalid runtime roots. This is not a clean-VM proof.
                environment.update(DOTNET_ROOT=str(root / "no-runtime"), DOTNET_ROOT_X64=str(root / "no-runtime"), DOTNET_MULTILEVEL_LOOKUP="0")
            runner = Runner(command, report, environment)
            report["version"] = runner.run("version", "--version")
            runner.run("merge help", "merge", "--help")
            with tempfile.TemporaryDirectory(prefix="dawvc-acceptance-remote-", dir=args.remote_parent or root) as remote:
                workflow(runner, root, Path(remote))
        report["status"] = "passed"
    except (AcceptanceFailure, OSError, ValueError, zipfile.BadZipFile) as error:
        # Generic filesystem errors may contain private absolute paths; keep them out of evidence.
        report["failure"] = str(error) if isinstance(error, AcceptanceFailure) else type(error).__name__
    args.report.parent.mkdir(parents=True, exist_ok=True)
    args.report.write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
    print(f"Preview acceptance: {report['status']} ({len(report['checks'])} checks).")
    return 0 if report["status"] == "passed" else 1


if __name__ == "__main__":
    sys.exit(main())
