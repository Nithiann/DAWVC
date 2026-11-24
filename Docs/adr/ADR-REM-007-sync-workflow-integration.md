# ADR-REM-007: integrate upstream and preview workflows with recovery/ownership

Date: 24 November 2026. Status: Proposed. WP-SYNC-09.
Requirements: FR-SYN-001..007, FR-PRV-001..007, FR-INT-001..005.

## Context and decision

WP-SYNC-02/03 existed on separate feature branches; the bare/merge/recovery/locking
stack started from develop and explicitly lacked them. Field qualification of the
complete CLI requires a coherent candidate with all intended synchronization
behavior. Port the two source implementations (`d7ccf20`, `be589ac`) onto
WP-SYNC-08 in a new stacked branch, without approving or merging their PRs.

Resolve overlap explicitly: preserve Bare Clone/Init flags, recovery and Busy
handlers, internal nested capability forwarding, both-parent ancestry and all
existing functionality. New upstream configuration is a writer and receives
operation/pending-recovery guards. Keep actual synchronization under ownership;
read-only dry-runs skip writer acquisition so an inspection cannot create a lock
file. Side-effect-free backend construction and lazy index ownership are retained.
A preview is advisory, not a local multi-file snapshot or reservation; actual
execution re-fetches. Pending local recovery still blocks sync previews.

## Alternatives and consequences

Leaving the branches separate cannot validate their interaction; rebasing older
sources wholesale would lose later behavior/documentation. Manual integration
plus cross-feature tests makes the resulting candidate reviewable. A shared reader
lock that creates a missing file would violate the no-write preview contract.
Read-only previews therefore remain outside writer serialization and may observe
concurrent changes, just like other read-only queries.

Existing API positional parameters, object formats and wire frames stay unchanged.
Track accepts future remote branches but validates local/configured remote names.
CLI cancellation is forwarded throughout; Linux SIGINT cleanup is automated,
Windows native Ctrl+C and actual FL Studio/two-PC/NAS/power tests remain open gates.
Merged-parent integration tests combine both snapshot choices, mapped local branch
names, fresh previews and real publication over filesystem and transient SSH.
