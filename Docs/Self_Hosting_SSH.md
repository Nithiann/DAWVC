# Self-hosting DAWVC over SSH

PC, NAS and server hosting use the same protocol. The host needs a compatible .NET
10 DAWVC deployment and an OpenSSH server. Clients need DAWVC and the OpenSSH
client (`ssh`) in PATH. No permanent DAWVC daemon runs: SSH starts a temporary
`dawvc serve` process for each connection, which stops after disconnect. The host
must be online and reachable. Standard GitHub/GitLab Git endpoints do not speak
this protocol; a future hosted UI can use the same DAWVC receiver.

## Host configuration

Use a dedicated SSH account with OS access only to the repositories it may serve.
Configure a host-owned policy at `~/.config/dawvc/serve.json`, or select an absolute
policy path in the SSH forced command. The client cannot select that file.

```json
{
  "Repositories": {
    "projects/song": {
      "Path": "/srv/dawvc/song",
      "AllowPush": true
    },
    "published/mix": {
      "Path": "/srv/dawvc/published-mix",
      "AllowPush": false
    }
  },
  "SessionTimeoutSeconds": 600,
  "MaxRequests": 100000,
  "MaxObjectBytes": 8589934592,
  "MaxTransferBytes": 34359738368
}
```

Only configured aliases are readable; writes default to denied. Paths must be
absolute initialized DAWVC repositories with dawvc.yaml. Bare repositories use
objects/ and refs/heads/ in the root; ordinary repositories use .dawvc/. There is
now an `init --bare --from` command for host-local provisioning (see below).
Push to an ordinary repository's active branch is refused. An initial remote
must have the same repository ID as its source; do not independently initialize
unrelated repositories and expect their IDs to match. Never copy machine-local
bindings, staging databases or credentials when provisioning a bare remote.

On Windows, use absolute paths such as `D:\\DawVcs\\Song` in JSON and configure
OpenSSH to run the Windows DAWVC deployment. Repository aliases remain portable.
A NAS needs a supported runtime/deployment or compatible container plus SSH; an
SMB share alone does not provide an SSH endpoint. There is no vendor dependency.

## Provisioning a host repository

Run locally on the host, using a source path the host can read (including a mounted
share), or a trusted dawvc:// source:

```powershell
# Empty receiver, same project identity; no history copied yet:
dawvc init --bare --from D:\Music\Song --dir D:\DawVcs\Song
# Alternatively seed all branches and history immediately, without a working tree:
dawvc clone D:\Music\Song D:\DawVcs\Song --bare
```

Choose one command and an absent/empty destination. Then configure its absolute
path in `serve.json`, set `AllowPush` explicitly, and authorize the SSH account/key.
On the producer PC:

```powershell
dawvc remote add origin dawvc://producer@host/projects/song
dawvc push origin main
```

The source and target IDs match by construction. Existing unrelated repos are
not adopted or overwritten; `--force` is not supported for bare cloning. A source
URL does not create a target on that SSH host: the destination is always a local
filesystem path on the machine running provisioning. Bindings, source index and
working files are never copied. Keep provisioning parents operator-controlled;
real NAS/SMB rename semantics and Windows OpenSSH still need platform validation.

## SSH authorization

For a POSIX OpenSSH host, a repository-specific authorized_keys entry can use:

```text
restrict,command="/usr/local/bin/dawvc serve --config /srv/dawvc/policy-alice.json" ssh-ed25519 <public-key> alice
```

The `command` and `restrict` options are essential for a restricted service key:
serve ignores SSH_ORIGINAL_COMMAND, and the client cannot escape into a shell.
The policy file must be owned/administered by the operator and not writable by
that restricted account. Give each principal/key a suitable policy; a shared
unrestricted SSH account does not provide per-user DAWVC authorization.

For a dedicated service account, apply equivalent sshd policy as well:

```text
Match User dawvc
    AuthenticationMethods publickey
    PasswordAuthentication no
    KbdInteractiveAuthentication no
    DisableForwarding yes
    PermitTTY no
    ForceCommand /usr/local/bin/dawvc serve --config /etc/dawvc/serve.json
```

Adapt executable paths and forced-command quoting to the host OS/OpenSSH version.
Account/key, known-host and repository permissions must be restrictive; do not
reuse privileged/root accounts. Disable unrestricted shells and forwarding using
SSH forced-command policy rather than relying on the DAWVC client alone. Enforce
connection, disk and concurrency quotas at the SSH/OS layer. Preserve operator
access for diagnosing policy/configuration problems; server errors deliberately
omit local paths and raw exception messages.

## Client workflow

Provision the SSH key/agent and verify the host fingerprint through a trusted
independent channel. Add only the verified key to the client's known_hosts. DAWVC
never automatically accepts an unknown/changed key and will not prompt for a
password or unlock a key interactively; unlock it in the agent beforehand.
SSH user config can select IdentityFile/IdentitiesOnly and ProxyJump. Its local
configuration is trusted; DAWVC does not store key files or passwords in remotes.

```powershell
dawvc remote add origin dawvc://alice@studio-server/projects/song
dawvc push --dry-run
dawvc push
dawvc clone dawvc://alice@studio-server/projects/song MySong
dawvc fetch
dawvc pull
```

A different SSH port is explicit, e.g. `dawvc://alice@nas:2222/projects/song`.
The receiver applies the same verification, quarantine, fast-forward and ref-lock
rules as filesystem Push. Uncommitted local work is not uploaded; Push does not
update either workspace or local upstream/tracking refs. Fetch refreshes tracking.

See [Remote protocol](Remote_Protocol.md), [Recovery Guide](Recovery_Guide.md) and
[Known limitations](Known_Limitations.md) for quotas, disconnect behavior and the
scope of tested host platforms. Opening an SSH port does not bypass NAT/firewalls;
operators choose VPN, routing and exposure policy for their deployment.

## Repeating the Linux OpenSSH smoke test

After a Release build, run `python3 scripts/test_ssh_loopback.py` as an unprivileged
Linux user with `ssh`, `/usr/sbin/sshd` and `ssh-keygen` installed. Set
`DAWVC_DOTNET` if the dotnet executable is outside PATH. The script creates
temporary host/client keys, a localhost SSH daemon on a free port, isolated
known_hosts/configuration and test repositories, then cleans them up. It verifies
real push/clone/fetch/pull, byte identity and rejection of unknown/changed keys.
No production SSH configuration or account state is changed. The source checkout
must have secure ancestor ownership/modes, as required by OpenSSH StrictModes.


## Host workspace recovery

A new handshake against an ordinary host working repository with pending workspace
publication refuses synchronization until its operator inspects/replays recovery
locally. Use `dawvc recover` and the Recovery Guide on that host, not a remote
arbitrary-path recovery command. Bare storage has no workspace transaction. Never
remove live ref/ownership locks to bypass refusal. Workspace crash recovery does
not make whole Push or SSH connection failure transactional; inspect remote tips
after a lost publish reply as before.

WP-SYNC-08 receivers also own the destination's operation handle for the complete
incoming push session. A competing upgraded writer/receiver fails fast as a remote
error (exit 8). Local busy commands use exit 11. Do not remove operation/creation/
ref/recovery files to break a live owner; OS handles release on process termination.
Upgrade all writers together and qualify actual NAS/SMB locking on both clients.

WP-SYNC-09 integrates offline `remote track` upstream settings and fresh Fetch/Pull
previews with the same SSH transport. Preview reads current refs plus bounded
missing commit/snapshot metadata in memory; it neither downloads blob bodies nor
publishes anything. The host can still log authentication/requests, and its refs
can change before actual execution. No new wire, daemon or authorization policy.
All four sync commands forward cancellation; Linux SIGINT verifies child exit.
Windows console signals and actual deployment/storage behavior need native tests.
## Choosing a different upstream

After `remote add backup dawvc://alice@nas/projects/song`, use
`remote track backup published` to make the active local branch push to and pull
from backup/published. Add `--local-branch name` to configure another existing local
branch. The host is not contacted while storing this local preference. The same
rules apply to filesystem remotes. Explicit `pull backup published` always updates
the active local branch; a matching explicit remote retains its upstream mapping.

## Read-only sync previews

`fetch --dry-run` and `pull --dry-run` use read-only host operations to pin current
advertised tips and verify missing metadata in bounded memory. They do not open a
push session, initialize repository directories/indexes or change local tracking
refs. SSH authentication and operator audit logs still occur. No blob payloads
are downloaded or remote refs reserved; a later real operation validates again.
Existing host permissions, keys, quotas and protocol version remain unchanged.
