# DAWVC Remote Protocol v1

Status: implemented on the SSH feature branch; subject to PR review. This is a
DAWVC protocol, not Git or SFTP. Both clients and transient hosts run DAWVC.

## Address and transport

`dawvc://[user@]host[:port]/repository-alias` uses OpenSSH, default port 22.
Aliases contain literal ASCII name segments (`A-Z a-z 0-9 _ -`, with interior
periods allowed), at most 64 characters per segment and 256 overall. They are
case-sensitive allowlist keys, not absolute paths. Dot segments, URI escapes,
empty components, passwords, query strings, fragments and backslashes are refused.
DAWVC interprets these addresses in the CLI; this change does not register an OS/browser URL handler.
Use SSH config/agent for identities and jump-host configuration. There is no
implicit HTTPS/SSH scheme fallback. Filesystem/file:// remain separate transports.

The client executes OpenSSH using an argument list and the constant remote
command `dawvc serve`. All repository selection travels over stdin. The URL never
supplies a command, config path or remote shell argument. OpenSSH verifies known
host keys strictly, authenticates noninteractively using public keys/agent and
disables password/keyboard-interactive login, forwarding and local commands.
Host keys must be independently verified and provisioned outside DAWVC.

## Frames and state

The authenticated SSH channel carries stdout/stdin binary frames. A control frame
is a four-byte signed positive big-endian length, followed by UTF-8 JSON, maximum
1,048,576 bytes and JSON depth 16. Property names are case-sensitive PascalCase;
unknown fields fail closed. Every request/response carries `Version: 1`.
No banners, progress messages or terminal escape sequences belong on stdout.

The first request is `{"Version":1,"Operation":"hello","Repository":"projects/song"}`.
The host binds the connection to that alias and replies with `Configuration`, the
versioned repository YAML (not host policy). Subsequent requests must omit
Repository. Configuration is capped at 262,144 UTF-16 code units at the client and remains
subject to the 1 MiB frame byte limit. Unknown versions,
operations and state transitions terminate the session with a safe error code.

| Operation | Request fields | Response / body |
|---|---|---|
| `refs` | none | `Refs`: Name/Commit strings, maximum 4096 |
| `exists` | Hash (64 hex) | Exists boolean |
| `get` | Hash | Exists; when true Length followed by exactly Length raw envelope bytes |
| `validate` | Branch, nullable ExpectedTip | No error means current ref/active-HEAD checks pass; requires write permission |
| `begin` | Branch, nullable ExpectedTip | Acquires the receiver's ref lock and opens a push session |
| `put` | Hash, Type, Length (payload bytes) | Existing verified object: Ready=false. Missing: Ready=true, then client sends exactly Length payload bytes, then host replies Received=true after verification |
| `publish` | Tip | Validate complete closure and expected ancestry, promote objects, atomically publish ref; dispose receiving session and acknowledge |

Only one request/body is outstanding on a connection. A bounded body stream never
consumes the next frame. Closing an incompletely read download faults the client
connection. A put payload has no envelope header on wire: the host constructs it
from the declared identity/type/length and verifies the resulting envelope and
BLAKE3 against the expected hash. Downloads preserve the original full envelope. Shared incoming verification also
rejects any trailing bytes beyond its declared payload, for filesystem and SSH.

Errors contain only `Error`: `incompatible`, `non-fast-forward`, `integrity`,
`validation`, `denied`, `timeout` or `remote`. No raw host exception messages,
policy paths or private filesystem locators are returned. The client maps these
to domain-typed failures. SSH stderr is drained without copying raw host output
into product diagnostics; inspect SSH configuration locally when login fails.

## Limits, ownership and failure

The client bounds each command/object read to five minutes, including connection
setup; OpenSSH also has a 15-second connect timeout and keepalives. Host policy
bounds the entire session (default 600 seconds, configurable 5–3600), request
count (default 100,000, maximum 2,000,000), individual payload size (default and
maximum 8 GiB) and cumulative binary transfer (default 32 GiB). Metadata remains
limited to 2 MiB per object. The existing push closure limits still apply.
Limits are deliberate and configurable downwards; this version has no resumable
transfer or dynamic limit negotiation. Very large libraries may require a later
protocol extension. Administrators also enforce connection/storage quotas in SSH
and the operating system; per-process limits do not bound aggregate clients.

Authentication and authorization happen before filesystem access. The configured
SSH account/key determines the host policy; aliases grant read access and
AllowPush explicitly grants write access. The host refuses links/reparse points
in repository metadata paths. OS permissions and trusted administrators remain
part of the boundary; protection against a hostile local actor replacing paths
between checks is not claimed.

The host owns transport/push-session resources and disposes them on EOF, handled
error or deadline. Client cancellation kills the SSH process; the receiving host
observes disconnect/EOF and releases its session. Abrupt host termination releases
OS locks but may leave quarantine. Acknowledgement loss after ref publication is
ambiguous: fetch/inspect refs before retrying. Push-session disposal closes that
SSH connection. No implicit tracking-ref/upstream changes or destructive repair.


## Bare provisioning compatibility (WP-SYNC-04)

Bare init/clone adds no wire operation or version change. A local operator uses
`init --bare --from <source>` to read source configuration via hello, or
`clone --bare` to read configuration/refs/verified objects via the existing read
operations. The target is a local filesystem directory. Provisioning never asks
SSH serve to create an arbitrary remote path; expose the resulting host path
through an explicitly configured alias/push policy. First Push keeps the same
repository identity and fast-forward/closure/conditional-publication checks.


## Explicit Merge compatibility (WP-SYNC-05)

Explicit local Merge uses the existing multi-parent commit payload and a reused
snapshot ID. Object traversal/closure validation and fast-forward checks already
follow all parents; no hello, ref, object, publication operation or wire version
changes. Fetch precedes local Merge; publication remains ordinary Push. Receivers
need the existing compatible DAWVC protocol, not a native FLP merge service.
