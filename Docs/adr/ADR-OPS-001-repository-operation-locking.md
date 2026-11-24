# ADR-OPS-001: operation-wide repository writer ownership

Status: Proposed; WP-SYNC-08. Date: 24 November 2026.

## Context

Ref compare-and-swap, SQLite transactions and durable workspace recovery protect
individual boundaries. They do not prevent another Add/Commit/Fetch from changing
staged intent or refs while a long-running Pull/Merge plans and publishes files.
Independent clients and transient SSH receivers need the same exclusion domain.

## Decision

Stock Application mutation entry points acquire a fail-fast OS handle lease on
`.dawvc/operation.lock` before opening mutable contexts, and hold it until their
local work and owned resources finish. This includes cache-writing Status and
binding-writing Doctor. Pull/Push retain local ownership during network waits.
Incoming filesystem/SSH push sessions own the destination's `operation.lock`
(bare root or working `.dawvc`) through quarantine, publication and disposal.

Explicit internal request capabilities allow only awaited nested use cases to
join the same root lease. They are reference-counted, never publicly serialized
and not ambient `AsyncLocal` ownership: independent callbacks/Task.Run/public
calls fail busy. The in-process registry supplements the OS lock. Lock order is
operation, workspace publication/recovery, ref writer. There is no waiting,
timeout, stale-PID stealing or lock-file deletion. Opposing pushes cannot deadlock;
one/both can fail and callers retry after observing the remote refs. Self-push to
the same physical repository is refused while its local lease is held.

Init/Clone and bare provisioning first own a persistent hashed sibling creation
lock, shared for the canonical target path. All creation paths use this sidecar,
even for an existing empty target. Working provisioning then owns the metadata
lock; bare staging is private, acquires its own working lease, releases it before
renaming and atomically publishes a complete bare directory. Existing initialized
mutations need only the metadata lease. Creating/provisioning requires a writable
parent directory. Failed working creation can retain a lock-only scaffold; a new
Clone accepts that scaffold, never arbitrary existing files without --force.

Normal paths are canonical full paths and links/junctions are refused. Empty
persistent lock files are normal; nonempty unknown contents fail closed. OS
sharing/lock contention maps to RepositoryBusyException, appended failure category
RepositoryBusy and local CLI exit 11. A busy incoming host retains the existing
remote error/exit 8 contract. Cancellation and every exception dispose ownership;
the OS releases handles after process termination, leaving files in place.

## Alternatives

A ref-only lock leaves staging/workspace races. Per-command locks miss nested
read/plan/publish spans. Waiting can deadlock across repositories. An ambient
reentrancy flag permits unrelated callbacks/child tasks to inherit authority.
Deleting stale lock files can split active ownership across different file inodes.

## Limits and consequences

This is cooperative ownership, not a filesystem permission boundary. Older
clients, raw IRepositoryContext/index/backend methods, overridden custom use
cases, external restore targets and DAW/editor saves do not participate. Custom
non-filesystem contexts without filesystem repository markers retain their
provider's coordination policy. Read-only Log/Scan/Fsck/view/inspection operations
remain outside the lease; they offer no atomic multi-file view and opening a
backend may still create housekeeping metadata. External restores require their
own exclusive destination and must not target an active working repository.

Cross-process local filesystem tests include busy CLI refusal, exact non-lock
byte preservation, independent working copies, callback isolation, creation
races, exception/cancellation release and forced process termination. Existing
nested workflows and SSH smoke remain acceptance coverage. Windows CI validates
the same cases. Actual NAS/SMB lock/rename qualification, physical power loss and
native FL Studio/two-PC acceptance remain separate open gates. Operation ownership
does not newly journal Commit/Push/provisioning or replace expected-state checks.

Requirements: FR-OPS-001..009. No shared object/schema/wire/runtime dependency change.
