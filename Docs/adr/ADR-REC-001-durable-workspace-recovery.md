# ADR-REC-001: Durable workspace publication recovery

Date: 24 November 2026. Status: Proposed for PR review.
Requirements: FR-REC-001..010; WP-SYNC-07.

The existing PublicationJournal compensates handled errors in memory. A killed
process loses its action list and cannot restore files, HEAD/ref and staged intent
as a coherent previous state. Preserve a write-ahead recovery envelope before
in-place publication; do not silently guess after restart.

Prepare under `.dawvc/transaction-preparation/<id>`: copy and flush original file
bytes, record before/after BLAKE3 identities, prior/final HEAD and affected branch
tip, repository ID and logical staged entries (path/blob/hash/size/role). Flush
verified candidate data and a bounded versioned JSON manifest, then atomically
rename the prepared directory to `.dawvc/transactions/<id>` before file mutation.
No absolute workspace/backup paths are stored. Preparation never touches user
files; unpublished leftovers are not active transactions.

In-place Checkout, Switch and the shared fast-forward pipeline used by Pull,
initial Clone checkout and explicit Merge use this envelope around the existing
journal. Once files/ref/index finish, flush a Committed decision before discarding
backups. Existing handled compensation is retained; only a confirmed rollback
allows discarding its envelope. Compensation failure retains both journals.
SQLite staging uses synchronous=FULL; index recovery saves logical staged intent,
not database bytes, timestamps, cache hints, ACLs or arbitrary SQLite tables.

`recover` inspects; `recover --rollback` explicitly acts. Prepared means roll back
to the saved state; Committed means verify the final state and finalize metadata,
never revert an acknowledged version. Preflight all affected files, metadata and
backup hashes before recovery writes. Unknown edits/refs/index entries, malformed
paths/versions, missing backups and multiple transactions fail closed. Expected
original staging subsets allow recovery to resume after clearing/repopulating the
index was itself interrupted. Atomic per-file restore and repeated identity checks
avoid truncation and overwrite of ordinary detected concurrent edits. They are
not a sandbox against a hostile same-user writer changing paths between checks.

Finalize by atomically moving terminal envelopes to `.dawvc/transaction-cleanup`
BEFORE recursive deletion. A crash during cleanup leaves terminal garbage, not a
half-deleted active manifest. Private backups can remain after denied cleanup;
there is no secure erasure, automatic orphan GC or automatic recovery on open.

An exclusive persistent workspace-recovery.lock is owned through preparation/
publication or explicit recovery. Recovery also holds the existing ref-write.lock.
New cooperating mutation entry points and filesystem/SSH handshakes refuse active
pending envelopes. Raw backend APIs, older clients, external tools and already-
started writers are not globally serialized. The next WP's operation-wide local
lock is separate. Do not mutate/save the same project concurrently.

Reject links/junctions and reserved/noncanonical mutation paths. Validate manifest
version/identity/shape, 10k file and staging-entry limits, 16 MiB JSON, and derived
numeric backup paths before acting. Metadata/backups are private local recovery
data, never sync objects or CI artifacts. The test-only crash host uses internal
callbacks and actual self-process Kill; no product CLI/env kill switch is added.

Process termination recovery is automated evidence. Flush(true), atomic rename
and FULL SQLite durability improve persistence, but directory-entry ordering,
controllers, power-loss protection and NAS/SMB semantics need actual qualification.
No universal power-cut guarantee or cross-files/ref/index atomicity claim. External
restores, Commit, Push and bare provisioning are not newly journaled; post-decision
binding/probe failures do not undo committed publication. No native FLP writing,
GUI, remote wire version or shared object schema changes.

Recovery envelopes are owner-only directories: Unix 0700, Windows protected DACL
with inheritable full control for the current user. Restoration copies stay
inside that private envelope before rename, rather than exposing old musical
bytes in public workspace temporary files. These controls do not exclude OS
administrators/privileged backup tools or certify a NAS ACL implementation.
