# ADR-009: SQLite + Dapper for Local Workspace Metadata & Staging Index

## Status
Accepted

## Date
2026-09-13

## Context
DAWVC needs to track working-tree state, staging decisions, and filesystem metadata efficiently:
1. **Hybrid staging (`DEC-MVP-004`, `FR-STG-001`):** DAWVC must record explicitly staged assets (`dawvc add`) without persisting them as shared project state.
2. **Warm status performance (`FR-SCAN-011`, `NFR-PERF-004`):** Scanning a directory with hundreds of sample files must avoid recomputing full BLAKE3 hashes on every invocation when files have not changed. A cache of `(path, file_length, last_modified_utc, blake3_hash)` allows sub-second response times.
3. **Robustness & concurrency (`R-006`):** The local storage must handle concurrent processes, process crashes, and unexpected process termination cleanly on Windows.
4. **Rebuildability (`R-006`):** The local index is strictly a local cache. All ground truth lives in the working directory and the content-addressed object store (`.dawvc/objects/`). If the database file is lost or corrupt, it can be transparently recreated.

## Decision
We select **SQLite** via `Microsoft.Data.Sqlite` combined with **Dapper** micro-ORM to store local workspace metadata in `.dawvc/index.db`.

### Database Schema
1. **`staging_entries`**:
   - `path TEXT PRIMARY KEY` (normalized POSIX relative path)
   - `blob_id TEXT NOT NULL`
   - `hash TEXT NOT NULL`
   - `size INTEGER NOT NULL`
   - `role INTEGER NOT NULL`
   - `staged_at INTEGER NOT NULL`

2. **`filesystem_cache`**:
   - `path TEXT PRIMARY KEY`
   - `file_length INTEGER NOT NULL`
   - `last_modified_utc INTEGER NOT NULL`
   - `blake3_hash TEXT NOT NULL`
   - `checked_at INTEGER NOT NULL`

### Key Invariants
- **WAL Mode:** Opened with `PRAGMA journal_mode=WAL;` and `PRAGMA synchronous=NORMAL;` for optimal Windows concurrent performance and crash resistance.
- **Local Only:** `.dawvc/index.db` is never copied, exported, or treated as shared repository state.
- **Auto-Recovery:** If the database file fails to open or is detected as corrupted, it is automatically discarded and rebuilt from working tree state and HEAD snapshot.

## Consequences
- **Positive:** Standardized SQL transactional semantics for staging operations; sub-second warm `status` performance.
- **Positive:** Zero dependency leakage into `DawVcs.Domain` (domain only interacts with the `IStagingIndex` port).
- **Negative:** Adds `Microsoft.Data.Sqlite` and `Dapper` to `DawVcs.Infrastructure`.
