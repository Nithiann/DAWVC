# ADR-IO-001: Windows Atomic File Replace & Staging Primitives

- **Status:** Accepted
- **Date:** 2026-09-13
- **Authors:** DAWVC Contributors
- **Related Requirements:** FR-OBJ-004, FR-OBJ-010, NFR-INT-004, NFR-INT-005
- **Work Package:** WP-01 (Spike D) / WP-02 / WP-07

---

## Context & Problem Statement

When persisting immutable objects (`.dawvc/objects/`), updating references (`HEAD`, branches), and materializing project files (`checkout`), a crash, disk error, or forced process termination must never leave a partially written or corrupted file at the target destination (`FR-OBJ-010`).
On Windows NTFS, file updates must therefore be executed atomically via a *safe-write staging* mechanism.

## Considered Options

1. **Direct In-Place Write (`FileStream` directly targeting the destination path):**
   - If the process crashes mid-write, a corrupted file remains.
   - Violates integrity invariants `INV-008` and `FR-OBJ-004`.
2. **Writing to `%TEMP%` and Subsequently Moving to Repository:**
   - If `%TEMP%` resides on a different volume than the project (e.g. C: vs D:), `File.Move` performs a copy-and-delete operation rather than an atomic directory entry swap.
3. **Same-Directory Temporary File with Flush to Disk & Atomic Replace (`AtomicFileWriter`):**
   - Temporary file is created in the exact same directory as the target destination (`.tmp_<name>_<guid>`), guaranteeing that source and target reside on the same filesystem/NTFS volume.
   - Upon completing the payload, `FileStream.Flush(flushToDisk: true)` is called to ensure physical persistence on disk.
   - Next, `File.Move(tempPath, destinationPath, overwrite: true)` is invoked, utilizing the Windows kernel primitive `MoveFileExW` with the flag `MOVEFILE_REPLACE_EXISTING`.

## Decision Outcome

We choose Option 3: **`AtomicFileWriter` with same-directory staging and `Flush(flushToDisk: true)`**.

### Execution Protocol
1. Determine the absolute directory of the destination path and ensure the directory exists;
2. Create a unique hidden staging file in that directory: `.tmp_<filename>_<guid:N>`;
3. Stream the entire content or binary envelope into this staging file;
4. Invoke `Flush(flushToDisk: true)` before closing the handle;
5. Atomically replace/publish the file via `File.Move(temp, target, overwrite: true)`;
6. Upon any exception during steps 2 through 4, the staging file is immediately removed via a cleanup block. In the event of a sudden power loss or process kill, at most an unindexed `.tmp_*` orphaned file remains, which can be safely cleaned up by `dawvc doctor` or `fsck`.

## Consequences

### Positive Consequences
- **Crash Resilience:** The target file is guaranteed to retain its previous valid content until new data is completely flushed and atomically moved into place.
- **Volume Invariant:** Co-locating the staging file in the target directory eliminates cross-volume copy penalties.
- **Robustness:** Validated under fault injection (abrupt cancellation during write).

### Negative Consequences or Risks
- Requires write permissions in the target directory to create temporary staging files (which is inherently required for the destination file itself).

## Verification & Validation Evidence

The test suite in `tests/DawVcs.Infrastructure.Tests/FileSystem/AtomicFileWriterTests.cs` validates:
- Proper creation of new files;
- Atomic replacement of existing files;
- Preservation of original file contents when an `IOException` or crash is simulated mid-write;
- Clean removal of staging files upon failure.

