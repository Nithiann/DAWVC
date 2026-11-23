# ADR-REM-001: Verified filesystem push with conditional ref publication

Date: 23 November 2026. Status: Proposed for PR review.
Requirements: FR-PSH-001..010, INV-004/005/007.

## Decision

Add a writable transport capability and disposable push session without changing
the existing read-only IRemoteTransport contract. Application plans a complete
reachable closure, proves fast-forward ancestry locally and validates remote
identity/schema. Infrastructure receives and verifies envelopes in quarantine,
promotes immutable objects and publishes the expected ref atomically.

A destination-wide ref-writer lock serializes push sessions and cooperating local
branch/HEAD writers. It is an open persistent lock file with exclusive sharing,
not a stale-marker lock requiring deletion after crashes. A second expected-tip
and HEAD check immediately precedes publication. Non-bare checked-out targets
are refused. Symlink/reparse-point paths in writable repository metadata are
rejected rather than followed.

## Alternatives and consequences

Copying files and then overwriting a ref has no concurrency protection. Reusing
fetch alone cannot safely publish the receiving branch. Force pushes and remote
workspace updates need separate policy and are excluded.

Complete closure validation costs reads even when ancestors already exist.
Cancellation or failure before publication can leave unreachable verified objects;
quarantine is cleaned on handled termination. Power loss can leave quarantine.
After publication a cancellation cannot imply that the remote did not advance.
No local tracking ref is changed, avoiding a second publication with ambiguous
partial-success reporting. Existing/third-party writers that ignore this lock and
hostile concurrent filesystem changes are outside the cooperating-writer model.
Remote worktrees must not be used concurrently during push. SMB/filesystem
atomic-rename and file-sharing semantics must be supported by the share.
