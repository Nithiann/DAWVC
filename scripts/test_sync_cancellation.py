"""Linux SIGINT checks for sync CLI commands; no real host or keys are used.

Run after a Release build. DAWVC_DOTNET selects a dotnet outside PATH.
A temporary ssh shim blocks connection setup so cancellation is deterministic.
"""
import os
from pathlib import Path
import shlex
import signal
import struct
import subprocess
import sys
import tempfile
import time


def main():
    if os.name != "posix":
        raise SystemExit("This check uses POSIX SIGINT; Windows Ctrl+C needs native validation.")
    repo = Path(__file__).resolve().parents[1]
    dotnet = os.environ.get("DAWVC_DOTNET", "dotnet")
    dll = str(repo / "src/DawVcs.Cli/bin/Release/net10.0/dawvc.dll")
    with tempfile.TemporaryDirectory(prefix="dawvc-sync-cancel-") as temporary:
        root = Path(temporary)
        source = root / "source"
        source.mkdir()
        data = bytes([199, 11]) + b"25.2.5.5319"
        (source / "Song.flp").write_bytes(
            bytes.fromhex("464c68640600000000000a006000")
            + b"FLdt" + struct.pack("<I", len(data)) + data
        )
        for args in (
            ("init", "--dir", source, "--primary", "Song.flp"),
            ("commit", "-m", "Cancellation test", "--dir", source),
            ("remote", "add", "origin", "dawvc://test-host/song", "--dir", source),
        ):
            subprocess.run([dotnet, dll, *map(str, args)], check=True, capture_output=True, timeout=30)
        shim = root / "bin"
        shim.mkdir()
        marker = root / "ssh-ready"
        program = (
            "import os,time;from pathlib import Path;"
            "Path(os.environ['DAWVC_TEST_READY']).write_text(str(os.getpid()));"
            "time.sleep(60)"
        )
        executable = shim / "ssh"
        executable.write_text("#!/bin/sh\nexec " + shlex.quote(sys.executable) + " -c " + shlex.quote(program) + "\n")
        executable.chmod(0o700)
        env = os.environ.copy()
        env["PATH"] = str(shim) + os.pathsep + env["PATH"]
        env["DAWVC_TEST_READY"] = str(marker)
        for command in ("push", "fetch", "pull", "clone"):
            marker.unlink(missing_ok=True)
            args = [command, "--dir", str(source)] if command != "clone" else ["clone", "dawvc://test-host/song", str(root / "clone")]
            process = subprocess.Popen(
                [dotnet, dll, *args], cwd=repo, env=env, stdin=subprocess.PIPE,
                stdout=subprocess.PIPE, stderr=subprocess.PIPE, start_new_session=True,
            )
            try:
                deadline = time.monotonic() + 10
                while process.poll() is None and time.monotonic() < deadline:
                    if marker.exists() and marker.read_text().isdigit():
                        break
                    time.sleep(0.05)
                assert marker.exists() and marker.read_text().isdigit(), f"{command}: SSH setup did not start"
                child_pid = int(marker.read_text())
                process.send_signal(signal.SIGINT)
                output, error = process.communicate(timeout=10)
                assert process.returncode == 130, (command, process.returncode, output.decode(), error.decode())
                try:
                    os.kill(child_pid, 0)
                except ProcessLookupError:
                    pass
                else:
                    raise AssertionError(f"{command}: SSH child still alive after CLI completion")
                print(f"{command}: SIGINT returned 130 and the SSH child exited.", flush=True)
            finally:
                if process.poll() is None:
                    os.killpg(process.pid, signal.SIGKILL)
                    process.wait(timeout=5)


if __name__ == "__main__":
    main()
