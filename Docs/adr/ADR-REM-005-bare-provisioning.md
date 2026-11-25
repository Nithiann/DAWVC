# ADR-REM-005: Source-derived bare repository provisioning

Date: 23 November 2026. Status: Proposed for PR review.
Requirements: FR-BARE-001..008; WP-SYNC-04.

The transports already understand bare roots but operators previously had to
assemble their layout manually. Independently initializing a receiver generates
a different repository identity and therefore cannot seed an existing project.

Expose a shared BareRepositoryUseCase plus `init --bare --from <source>` and
`clone <source> [directory] --bare`. Init reads the source handshake/configuration
and creates an empty receiver with exactly that identity and shared policies.
Clone uses existing Fetch verification/quarantine and copies every fetched
tracking tip to a local branch, without checkout, index use or Doctor. HEAD keeps
the configured default branch, including an unborn default. Neither operation
copies source staging, bindings, credentials or workspace files. Bare Clone
records origin, but is not a mirror/update command and does not configure an
upstream or support ordinary working-tree commands against the bare root.

Build a normal temporary repository under the destination parent so Fetch can
reuse existing context/store paths. Dispose its contexts, move only its metadata
and shared configuration into a bare layout, then publish by same-volume directory
rename. Destination must be absent/empty; force and reinitialization are rejected.
Reject links/junctions in destination ancestry; recheck immediately before publish.
This is cooperative local administrative provisioning, not a sandbox against a
hostile writer changing parent paths concurrently. Parent directories require
operator-controlled permissions and reliable exclusive-open/rename semantics.

Remote creation over SSH is deliberately not a protocol operation: an operator
runs provisioning on the host (or mounted storage), then explicitly assigns its
alias and push policy. SSH URLs may supply the source. No daemon, schema, wire
version, new dependency or GUI integration is introduced.

A random-ID empty remote with first-push identity adoption was rejected because
it would weaken existing identity checks and require a new handshake/write policy.
Cloning a working tree and copying its entire directory was rejected because it
could expose machine bindings/index data and unnecessarily materialize assets.

Handled failures/cancellation before publication do not expose partial target
repositories. An existing empty placeholder is deleted immediately before rename;
if rename fails it may be absent, but existing user files are never overwritten.
Created parent directories can remain. Crash/denied cleanup may leave isolated
`.dawvc-provision-*` siblings; there is no persistent provisioning recovery journal
or secure-erase guarantee. Cancellation after publication need not undo success.
