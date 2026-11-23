# ADR-REM-004: Bounded in-memory sync preview

Date: 23 November 2026. Status: Proposed for PR review.
Requirements: FR-PRV-001..007; WP-SYNC-03.

Fetch dry-run previously staged metadata in local quarantine, pruned incoming
sessions and discarded metadata before Pull could inspect ancestry. Pull then
used stale/missing tracking refs. Reading through ordinary repository contexts
also created directories and disposed an unused SQLite index/pool.

Use a dedicated in-memory metadata planner before any quarantine creation.
Pin selected advertised tips, return planned ref updates, and retain bounded
verified parent links so Pull can traverse an overlay of remote metadata and
existing local commits. Preview results expose immutable snapshots; no streams
or paths outlive the call. Limit metadata/graph sizes and reject invalid shapes,
identities/types/lengths/schemas and unsafe snapshot paths. Blob bodies stay
unread; counts are declared missing-transfer estimates, not fsck guarantees.

Make loose-object-store/context construction side-effect free and staging-index
creation lazy. Blob writes create their temporary directories; explicit Init
creates its layout. Preview never accesses the staging index, so disposal does
not clear a pool owned by an unused index. This is lifetime tightening, not a new
persisted schema or security/wire protocol.

A disk-backed temporary quarantine outside the repository would retain a temp
lifetime/cleanup burden; caching in the actual object store would violate dry-run.
In-memory bounded metadata gives a clear no-repository-write contract at the cost
of rejecting oversized previews. Custom context/transport implementations remain
responsible for read-only behavior of their read methods. Filesystem access times
and host SSH authentication/audit logs are not repository content mutations.

The snapshot does not reserve refs, verify/download blobs or plan dirty workspace
conflicts. Actual execution re-fetches. No global mutation lock or forced recovery.
