# Known Limitations & Scope Boundaries (v0.1 Technical Preview)

**Release:** DAWVC v0.1.0-preview.2  
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

## 4. Local-Only Version Control (No Remotes in v0.1)

- Version 0.1 is designed for single-machine local version control and manual export/import (`dawvc checkout --restore-to <dir>`).
- Remote commands (`dawvc push`, `dawvc pull`, `dawvc clone`) and cloud repository negotiation are part of the post-MVP roadmap (v0.2+).

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
- If a handled error is encountered, temporary file-level backups are restored in reverse order, returning the workspace to its exact pre-checkout state.
- **Crash Recovery Scope:** Unhandled terminations (e.g. `SIGKILL`, abrupt power outage, or OS panic) mid-publication cannot be rolled back until persistent on-disk transaction manifests (`.dawvc/transactions/<id>/`) are implemented in a future release.

