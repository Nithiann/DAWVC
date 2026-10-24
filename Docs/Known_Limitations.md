# Known Limitations & Scope Boundaries (v0.1 Technical Preview)

**Release:** DAWVC v0.1.0-preview.3  
**Requirement Mapping:** `NFR-REL-007`, `NFR-REL-008`, `NFR-SEC-001..009`, Section 25 & 29

---

## 1. Proprietary Plugin Binaries & Sample Libraries

> [!IMPORTANT]
> **DAWVC does NOT bundle commercial plugin binaries (`.dll`, `.vst3`, `.aax`), license keys, or commercial sample packs.**

- **Plugin Tracking & Version Verification**: DAWVC inspects and records plugin requirements (plugin name, format, vendor, version, and architecture) inside the immutable project snapshot. It does **not** package plugin binaries. Collaborators must have their own licensed copies of third-party plugins installed on their systems. When moving across workstations, `dawvc doctor` validates that installed plugins are equal to or newer than the project's recorded version (`installedVersion >= requiredVersion`), flagging older versions as mismatches because DAW projects are generally backward compatible but not forward compatible.
- **Sample Bundling Policy**: Only project-specific recordings, stems, and explicitly staged samples (`dawvc add <path>`) are bundled into repository storage. Large commercial sample libraries (e.g. Kontakt libraries, multi-gigabyte orchestral packages) should remain on local storage drives and are referenced logically by content hash.

---

## 2. Read-Only FL Studio Project Inspection

> [!NOTE]
> **DAWVC never modifies, re-writes, or binary-patches FL Studio `.flp` files.**

- The FL Studio adapter is strictly **read-only**.
- It parses headers, event streams, and channel definitions to discover referenced samples and plugins.
- Because DAWVC does not alter the paths hardcoded inside `.flp` files, opening a restored project on a machine with a different folder structure may require setting up FL Studio *Extra Search Folders* (see [FL Studio Search Paths & Relinking Guide](FLStudio_Search_Paths_Relinking.md)).

---

## 3. Single Primary Project File per Repository

- In v0.1, each DAWVC repository tracks **one primary project file** (e.g., `TrackName.flp`) specified in `dawvc.yaml`.
- Sub-arrangements or alternative mixes should be managed using branches (`dawvc branch <name>` and `dawvc switch <name>`).
- Repositories containing multiple disparate `.flp` files in the root folder are out of scope for v0.1.

---

## 4. Remote Synchronization & Transports (v0.2 Baseline)

- **Supported Transports:** DAWVC v0.2 supports remote collaboration via the filesystem transport (`FileSystemRemoteTransport`), covering local paths, external drives, and network UNC shares (`\\server\share\repo`). Cloud transports (SSH, HTTPS) are scheduled for subsequent iterations.
- **Fast-Forward Only Synchronization:** In v0.2, synchronization operations (`dawvc pull`) enforce fast-forward integration (`--ff-only`). If histories diverge (e.g. concurrent commits made on different machines), the pull is rejected with exit code `9` (`NonFastForward`) to prevent automated binary corruption. Three-way structural merging of DAW projects is planned for future releases.
- **Quarantine Ingestion:** All remote payloads pass through incoming staging (`.dawvc/incoming/<sessionId>/`) with envelope inspection, BLAKE3 verification, and strict path traversal validation (`NFR-SEC-001..003`) before promotion to `.dawvc/objects/`.

---

## 5. Supported DAW Versions & Formats

- **Supported Baseline:** Image-Line FL Studio 2026.x (64-bit).
- **Graceful Fallback:** If an unversioned, newer, or legacy `.flp` format is encountered, DAWVC automatically falls back to **opaque tracking**: the file is versioned and restored byte-identically, while dependency inspection is skipped with a warning.
- **Other DAWs:** Ableton Live, Logic Pro, Studio One, and Reaper are planned for subsequent adapter releases. The core architecture is completely DAW-independent.

---

## 6. Privacy & Diagnostic Redaction

- Local absolute filesystem paths (such as `C:\Users\<username>\...`) and URL credentials are automatically redacted in diagnostic logs and error outputs (`NFR-SEC-007`).
- Inspection metadata contains no user passwords or machine hardware IDs.

---

## 7. Automatic Rollback on Handled Publication Failure

- In v0.1, the `PublicationJournal` provides automatic, in-memory rollback of file operations when an exception occurs during staging, candidate installation, reference updates, or index clearing.
- Temporary file-level backups are restored in reverse order on handled publication failure. **Audit clarification (24 October 2026): rollback is best-effort, not proof that the exact previous state was restored.** Restoration errors are currently suppressed, and backup cleanup may still run after a failed restoration. Incomplete recovery does not yet have a dedicated typed outcome.
- **Crash Recovery Scope:** Unhandled terminations (e.g. `SIGKILL`, abrupt power outage, or OS panic) mid-publication cannot be rolled back until persistent on-disk transaction manifests (`.dawvc/transactions/<id>/`) are implemented in a future release.

## 8. Operation Audit for Desktop Preparation (24 October 2026)

These are audited backend limitations, not delivered GUI features. See the [operation inventory](GUI_Operation_Inventory.md) and [GUI task list](GUI_Implementation_Tasks.md) for implementation status.

- **Cancellation is not uniformly preserved:** dependency-search fallbacks, publication exception wrapping, and optional post-transfer doctor summaries can still mask or reclassify cancellation. Doctor/fsck now exclude cancellation from their own diagnostic catches and check entry/completion; scan/status have targeted entry/loop/completion checks. Commit and transfers check entry cancellation, but underlying synchronous work and post-publication semantics are not yet uniformly cancellable.
- **Progress is integrated, not a transaction guarantee:** optional per-call progress is available for scan/status, commit, checkout/restore and fast-forward, doctor/fsck and fetch/clone/pull without changing existing CLI calls. Nested operations filter child completion. Known totals count processed work rather than healthy results; repeated stage instances may restart counters. Observer callbacks must not throw or block. Throttling, UI dispatching, typed timeout outcomes and complete cancellation/rollback guarantees remain follow-up work. A scan timeout is cooperative and adapter-only, not hard process isolation.
- **Metadata writes during inspection:** constructing a repository context can create metadata directories. Status warms the hash cache and may initialize SQLite. Corruption recovery currently deletes/recreates the entire local index, which can also discard staged entries; this must be addressed before accepting a viewer that promises no staging changes.
- **Dry-run is not write-free:** fetch/pull dry-run creates quarantine sessions, downloads commit/snapshot metadata and prunes stale incoming sessions. It does not download blobs, promote objects or update tracking refs. Pull's classification can still use stale local tracking refs rather than the prospective remote tip.
- **Partial durable results:** fetch can retain promoted objects and partially updated tracking refs; clone can leave a partially initialized target. Commit can publish its ref before index clearing fails. Checkout/restore can finish file publication before dependency resolution fails or is cancelled. A failed/cancelled task therefore does not universally mean 'nothing changed'.
- **Long-lived adapter caches:** the shared FL Studio adapter has no invalidation for search paths, file indexes or positive/negative sample resolutions. Lifetime/refresh policy is still required before desktop rescans can be advertised as fresh.

