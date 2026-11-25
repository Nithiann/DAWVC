"""Linux OpenSSH smoke test; run unprivileged after a Release build.

Requires ssh, sshd and ssh-keygen. Uses only temporary keys/configuration.
Set DAWVC_DOTNET to select a dotnet executable outside PATH.
"""
import getpass
import json
import os
from pathlib import Path
import shlex
import socket
import struct
import subprocess
import tempfile
import time


def main():
    repo = Path(__file__).resolve().parents[1]
    dotnet = os.environ.get("DAWVC_DOTNET", "dotnet")
    dll = repo / "src/DawVcs.Cli/bin/Release/net10.0/dawvc.dll"
    account = getpass.getuser()
    if os.name != "posix" or os.getuid() == 0:
        raise SystemExit("Run this Linux OpenSSH test as an unprivileged account.")

    # Avoid /tmp: StrictModes correctly rejects world-writable ancestors for keys.
    with tempfile.TemporaryDirectory(prefix=".dawvc-ssh-smoke-", dir=repo) as temp:
        root = Path(temp)
        root.chmod(0o700)
        for name in ("hostkey", "identity"):
            subprocess.run(
                ["ssh-keygen", "-q", "-t", "ed25519", "-N", "", "-f", str(root / name)],
                check=True,
            )
        authorized = root / "authorized_keys"
        authorized.write_text((root / "identity.pub").read_text())
        authorized.chmod(0o600)
        local, remote = root / "local", root / "remote"
        local.mkdir()
        remote.mkdir()
        data = bytes([199, 11]) + b"25.2.5.5319"
        (local / "Song.flp").write_bytes(
            bytes.fromhex("464c68640600000000000a006000")
            + b"FLdt" + struct.pack("<I", len(data)) + data
        )

        def cli(*args, env=None, expected=0):
            result = subprocess.run(
                [dotnet, str(dll), *map(str, args)], cwd=repo, env=env,
                capture_output=True, text=True, timeout=45,
            )
            assert result.returncode == expected, (
                args, result.returncode, result.stdout, result.stderr
            )

        cli("init", "--dir", local, "--primary", "Song.flp")
        cli("commit", "-m", "SSH smoke", "--dir", local)
        cli("init", "--bare", "--from", local, "--dir", remote)
        assert not (remote / ".dawvc").exists()
        policy = root / "policy.json"
        policy.write_text(json.dumps({
            "Repositories": {"projects/song": {"Path": str(remote), "AllowPush": True}}
        }))
        wrapper = root / "serve"
        wrapper.write_text(
            "#!/bin/sh\nexec " + " ".join(map(shlex.quote, (
                dotnet, str(dll), "serve", "--config", str(policy)
            ))) + "\n"
        )
        wrapper.chmod(0o700)
        with socket.socket() as listener:
            listener.bind(("127.0.0.1", 0))
            port = listener.getsockname()[1]
        config = root / "sshd_config"
        config.write_text(f"""Port {port}
ListenAddress 127.0.0.1
HostKey {json.dumps(str(root / 'hostkey'))}
AuthorizedKeysFile {json.dumps(str(authorized))}
StrictModes yes
PasswordAuthentication no
KbdInteractiveAuthentication no
UsePAM no
PidFile {json.dumps(str(root / 'sshd.pid'))}
AllowUsers {account}
AllowTcpForwarding no
X11Forwarding no
ForceCommand {shlex.quote(str(wrapper))}
LogLevel VERBOSE
""")
        with (root / "sshd.log").open("w+") as log:
            daemon = subprocess.Popen(
                ["/usr/sbin/sshd", "-D", "-e", "-f", str(config)],
                stdout=log, stderr=log,
            )
            try:
                for _ in range(30):
                    if daemon.poll() is not None:
                        log.seek(0)
                        raise RuntimeError("sshd startup failed: " + log.read())
                    try:
                        with socket.create_connection(("127.0.0.1", port), timeout=0.2):
                            break
                    except OSError:
                        time.sleep(0.1)
                known, sshconfig = root / "known_hosts", root / "ssh_config"
                sshconfig.write_text(
                    f"Host *\n IdentityFile {json.dumps(str(root / 'identity'))}\n"
                    f" IdentitiesOnly yes\n UserKnownHostsFile {json.dumps(str(known))}\n"
                    " GlobalKnownHostsFile /dev/null\n"
                )
                shim = root / "bin"
                shim.mkdir()
                executable = shim / "ssh"
                executable.write_text(
                    "#!/bin/sh\nexec /usr/bin/ssh -F "
                    + shlex.quote(str(sshconfig)) + ' "$@"\n'
                )
                executable.chmod(0o700)
                env = os.environ.copy()
                env["PATH"] = str(shim) + os.pathsep + env["PATH"]
                url = f"dawvc://{account}@127.0.0.1:{port}/projects/song"
                cli("remote", "add", "origin", url, "--dir", local)
                cli("push", "--dir", local, env=env, expected=8)
                print("Unknown host key: refused (exit 8).", flush=True)
                public = (root / "hostkey.pub").read_text().split()
                known.write_text(f"[127.0.0.1]:{port} {public[0]} {public[1]}\n")
                cli("push", "--dir", local, env=env)
                clone = root / "clone"
                cli("clone", url, clone, env=env)
                assert (clone / "Song.flp").read_bytes() == (local / "Song.flp").read_bytes()
                bare_copy = root / "bare-copy"
                cli("clone", url, bare_copy, "--bare", env=env)
                assert not (bare_copy / ".dawvc").exists()
                assert not (bare_copy / "Song.flp").exists()
                copy_worktree = root / "copy-worktree"
                cli("clone", bare_copy, copy_worktree)
                assert (copy_worktree / "Song.flp").read_bytes() == (local / "Song.flp").read_bytes()
                empty_receiver = root / "empty-receiver"
                cli("init", "--bare", "--from", url, "--dir", empty_receiver, env=env)
                assert not list((empty_receiver / "refs/heads").iterdir())
                cli("remote", "add", "seed", empty_receiver, "--dir", local)
                cli("push", "seed", "--dir", local)
                print("Bare init/clone from real SSH and first publication: passed.", flush=True)
                first_bytes = (clone / "Song.flp").read_bytes()
                (local / "Song.flp").write_bytes(first_bytes.replace(b"25.2.5.5319", b"25.2.5.5320"))
                cli("commit", "-m", "SSH update", "--dir", local)
                cli("push", "--dir", local, env=env)
                cli("fetch", "--dir", clone, env=env)
                assert (clone / "Song.flp").read_bytes() == first_bytes
                cli("pull", "--dir", clone, env=env)
                assert (clone / "Song.flp").read_bytes() == (local / "Song.flp").read_bytes()
                # Make two independent versions, then join their histories explicitly.
                (clone / "Song.flp").write_bytes(first_bytes.replace(b"25.2.5.5319", b"25.2.5.5321"))
                cli("commit", "-m", "Collaborator version", "--dir", clone)
                cli("push", "--dir", clone, env=env)
                (local / "Song.flp").write_bytes(first_bytes.replace(b"25.2.5.5319", b"25.2.5.5322"))
                cli("commit", "-m", "Local independent version", "--dir", local)
                cli("push", "--dir", local, env=env, expected=9)
                cli("fetch", "--dir", local, env=env)
                cli("merge", "origin/main", "--take", "incoming", "-m", "Choose collaborator version", "--dir", local)
                assert (local / "Song.flp").read_bytes() == (clone / "Song.flp").read_bytes()
                cli("push", "--dir", local, env=env)
                cli("pull", "--dir", clone, env=env)
                cli("fsck", "--dir", local)
                print("Explicit merge resolves divergence and resumes SSH Push/Pull: passed.", flush=True)
                subprocess.run(
                    ["ssh-keygen", "-q", "-t", "ed25519", "-N", "", "-f", str(root / "wronghost")],
                    check=True,
                )
                wrong = (root / "wronghost.pub").read_text().split()
                known.write_text(f"[127.0.0.1]:{port} {wrong[0]} {wrong[1]}\n")
                cli("fetch", "--dir", clone, env=env, expected=8)
                print("Changed host key: refused (exit 8).", flush=True)
                print("Real OpenSSH push/clone/fetch/pull and byte identity: passed.", flush=True)
            finally:
                daemon.terminate()
                try:
                    daemon.wait(timeout=5)
                except subprocess.TimeoutExpired:
                    daemon.kill()
                    daemon.wait()


if __name__ == "__main__":
    main()
