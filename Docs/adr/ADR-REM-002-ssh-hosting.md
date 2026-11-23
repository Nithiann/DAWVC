# ADR-REM-002: Vendor-independent hosting with dawvc:// over SSH

Date: 23 November 2026. Status: Proposed for PR review (direction authorized by user).
Requirements: FR-SSH-001..009, FR-PSH-001..010.

Use a versioned stdio request/response protocol between the installed OpenSSH
client and a temporary `dawvc serve` process. OpenSSH performs authentication,
server identity verification and encryption. Keep the same Application use cases
and optional push capability for local folders, NAS and servers.

`dawvc://` always means this protocol over SSH, not a new insecure TCP service.
Repository path components are logical allowlisted aliases. Operators bind
absolute repository roots and read/write permission to an SSH account/key using
host-owned configuration and a forced command. There is no client-selected
server executable or config path. Disable forwarding and interactive login in
the documented SSH policy. The filesystem receiver enforces the same closure,
fast-forward, active-worktree and ref-lock checks.

The previously described HTTPS/OIDC/PostgreSQL/S3 architecture is an optional
future hosted product, not a requirement for basic synchronization. Standard
GitHub/GitLab endpoints speak Git and cannot serve this protocol directly.
A custom background daemon adds deployment burden and is unnecessary here.
SFTP alone cannot enforce receiver-side closure validation or conditional refs.

Consequences: hosts need OpenSSH and DAWVC installed; clients need OpenSSH in PATH.
Users provision trusted host keys and their SSH keys outside DAWVC. Configuration
files, keys and repository directories must have restrictive OS permissions.
Locks protect cooperating writers, not hostile local filesystem races. Process
loss after ref publication can lose the acknowledgement; inspect refs before
retrying. No authentication server, tenant management or Git interoperability.
