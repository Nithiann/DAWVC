# DAWVC — MVP Requirements Specification

> **Document Version:** 1.0  
> **Status:** Approved baseline for implementation  
> **Product Version:** MVP v0.1  
> **Normative Architecture:** `DAWVC_Technical_Design.md` v1.1  
> **Primary Implementation:** C# / .NET 10  
> **Target Platform:** Windows 11 x64  
> **Initial DAW Adapter:** FL Studio 2026.x  
> **Release Format:** Public technical preview

---

# 1. Purpose of this Document

This document specifies the required behavior of DAWVC MVP v0.1. The Technical Design describes the architecture and internal models; this document establishes what the MVP functionally and non-functionally delivers and how it is objectively accepted.

The keywords **MUST**, **MUST NOT**, **SHOULD**, and **MAY** are normative:

- **MUST / MUST NOT:** Hard MVP requirement;
- **SHOULD:** Desired behavior; any deviation requires explicit technical justification;
- **MAY:** Optional behavior that does not block MVP acceptance.

All requirements possess a stable identifier. The Implementation Plan and test suites link directly to these identifiers.

---

# 2. Product Goal

DAWVC is a local version control and dependency management system tailored for DAW projects. MVP v0.1 must safely register, inspect, version, restore, and reproducibly prepare a single FL Studio project across different Windows workstations.

The MVP proves five core propositions:

1. A native `.flp` project file can be versioned immutably and byte-identically.
2. Project assets can be identified by content hash rather than fragile local file paths.
3. Local directory layouts and host plugin installations do not leak into shared identities.
4. Workspace checkout never publishes a partial or unverified project artifact.
5. FL Studio-specific inspection remains isolated behind a DAW-independent adapter contract.

---

# 3. Approved Product Decisions

The following architectural and product choices are binding for MVP v0.1:

| ID | Decision |
|---|---|
| DEC-MVP-001 | MVP v0.1 is a public technical preview and portfolio-grade open-source release. |
| DEC-MVP-002 | One repository represents one logical musical project with exactly one primary `.flp` per snapshot. |
| DEC-MVP-003 | Windows 11 x64 and FL Studio 2026.x are officially supported and tested. Other FLP versions may be tracked opaquely. |
| DEC-MVP-004 | DAWVC utilizes hybrid staging: the primary `.flp` and tracked assets are staged automatically; new assets require `dawvc add`. |
| DEC-MVP-005 | Audio samples, recordings, and plugin identities are detected best-effort. Third-party plugin content is best-effort or user-assisted. |
| DEC-MVP-006 | DAWVC materializes assets and bindings, but never mutates FL Studio settings or rewrites `.flp` files. Manual search path configuration or relinking may be required. |
| DEC-MVP-007 | Missing mandatory `Bundle` dependencies block a normal commit; `--allow-incomplete` permits an explicit override. `ReferenceOnly` requirements do not block commits. |
| DEC-MVP-008 | The v0.1 command suite comprises `init`, `scan`, `status`, `add`, `commit`, `log`, `checkout`, `branch`, `switch`, `doctor`, and `fsck`. |
| DEC-MVP-009 | Checkout aborts when uncommitted changes exist. `--force` creates a recovery backup first; `--restore-to` restores cleanly without touching the active workspace. |
| DEC-MVP-010 | Objects utilize a versioned binary envelope. Canonical JSON manifests and blobs are stored uncompressed in v0.1. |
| DEC-MVP-011 | Distribution is delivered as a self-contained Windows x64 executable and ZIP archive. |
| DEC-MVP-012 | Performance baseline targets 5,000 tracked assets and 100 GB bundled content, with streaming I/O and peak memory under 512 MB. |
| DEC-MVP-013 | Development plan assumes a single engineer using ideal working days without artificial calendar deadlines. |
| DEC-MVP-014 | Source code is published on GitHub under the MIT license; test fixtures contain strictly custom or royalty-free public domain content. |

---

# 4. Scope

## 4.1 In Scope

MVP v0.1 includes:

- One local repository per logical musical project;
- Exactly one primary FL Studio `.flp` per snapshot;
- DAW-independent core domain model;
- `.flp` treated as a `SingleFileArtifact`;
- Immutable content-addressed object store leveraging BLAKE3;
- Hybrid staging workflow;
- Commits, log history, branching, and branch switching;
- Read-only FLP detection, inspection, and format validation;
- Opaque fallback for unknown or newer FLP versions;
- Discovery of audio samples, recordings, and plugin requirements;
- Asset bundling governed by portability policies;
- Local machine-specific dependency bindings;
- Staged and verified workspace checkout;
- Automated recovery copies during forced checkout;
- Project and environment diagnostics via `dawvc doctor`;
- Repository and artifact integrity checks via `dawvc fsck`;
- Self-contained Windows x64 binary distribution;
- Automated unit, fixture, integration, and end-to-end tests.

## 4.2 Out of Scope

MVP v0.1 explicitly excludes:

- Remote repositories, `clone`, `fetch`, `pull`, or `push`;
- Central server, user accounts, authentication, or access control;
- Desktop GUI or web dashboard;
- Project locking between concurrent users;
- Tag commands;
- Automated semantic merging of native DAW project files;
- Semantic project diffing;
- Native FLP rewriting or binary path patching;
- Automated modification of FL Studio configuration or Extra Search Folders;
- Automated installation of plugins or proprietary library content;
- Full internal asset discovery managed inside closed third-party plugin engines;
- FL Studio zipped project archives (`.zip`) as primary artifacts;
- Functional support for Ableton Live, Logic Pro, Studio One, REAPER, or other DAWs;
- Cross-DAW collaboration snapshots;
- Object compression, delta compression, or content-defined chunking (CDC);
- Telemetry, automatic updates, or hosted crash reporting.

---

# 5. Users and System Actors

## 5.1 Primary User

Music producers and audio engineers working locally via the command-line interface with standard knowledge of terminals, file systems, and FL Studio projects.

## 5.2 External Actors

| Actor | Role in MVP |
|---|---|
| Windows Filesystem | Storage of active workspace, object store, staging cache, and recovery backups. |
| FL Studio | Launches the restored native project; never spawned, driven, or modified by DAWVC. |
| FL Studio Adapter | Inspects, parses, and validates `.flp` binaries in a strictly read-only manner. |
| Host Plugin Filesystem | Scanned to verify local plugin installations and versions; binaries are never executed. |
| User | Confirms policy overrides, specifies bindings, and configures FL Studio search folders as guided. |

---

# 6. Glossary

| Term | Definition |
|---|---|
| Repository | Version control boundary for one logical musical project. |
| Workspace | Active working directory containing live files, local bindings, and caches. |
| Primary Artifact | The active `.flp` project file represented by a snapshot. |
| Blob | Immutable content object addressed cryptographically by its BLAKE3 hash. |
| Snapshot | Immutable representation of project artifacts, dependencies, and environment requirements. |
| Bundle Dependency | Asset whose binary content is packaged into repository object storage. |
| ReferenceOnly Dependency | Requirement recorded in project metadata but not bundled (e.g. plugin binaries). |
| Binding | Machine-specific mapping linking a shared dependency ID to a local filesystem path. |
| Opaque Artifact | Artifact stored byte-identically without semantic interpretation or format decoding. |
| Recovery Copy | Complete backup of uncommitted local modifications created before a forced checkout. |
| Required Dependency | Dependency strictly required to reconstruct the project environment. |
| Optional Dependency | Supplementary dependency whose absence does not block project playback. |

---

# 7. Global Invariants

- **INV-001:** Original native artifact bytes represent primary ground truth.
- **INV-002:** DAWVC MUST NOT rewrite or binary-patch `.flp` project files in v0.1.
- **INV-003:** Dependency identity MUST NOT rely upon absolute local filesystem paths.
- **INV-004:** Commits, snapshots, manifests, and blobs are strictly immutable.
- **INV-005:** Branch references MUST point only to fully written and verified commits.
- **INV-006:** Checkout MUST NOT publish a partial or unverified artifact to the workspace.
- **INV-007:** Shared metadata MUST NOT contain credentials or machine-local path bindings.
- **INV-008:** Plugin binaries and commercial sound libraries MUST NOT be bundled by default.
- **INV-009:** Adapter inspection errors MUST NOT be classified as repository object corruption.
- **INV-010:** Unknown or future FLP formats MUST remain byte-identically versionable.
- **INV-011:** The core engine MUST NOT depend upon FL Studio-specific implementation code.
- **INV-012:** Every destructive workspace operation MUST provide an automated recovery path.

---

# 8. Functional Requirements — Repository & Configuration

| ID | Priority | Requirement |
|---|---|---|
| FR-REP-001 | Must | `dawvc init` MUST initialize a new repository in the current or specified directory. |
| FR-REP-002 | Must | Initialization MUST create `.dawvc/` and a versioned `dawvc.yaml` manifest. |
| FR-REP-003 | Must | Initialization MUST abort if parent or target directories introduce repository ambiguity, unless overridden. |
| FR-REP-004 | Must | A repository MUST contain exactly one unique logical repository ID. |
| FR-REP-005 | Must | The configuration MUST designate exactly one primary artifact locator. |
| FR-REP-006 | Must | Initialization MUST establish `main` as the default branch reference. |
| FR-REP-007 | Must | Re-running `init` inside an existing valid repository MUST be idempotent and preserve history. |
| FR-REP-008 | Must | Repositories with an unrecognized newer schema version MUST be rejected read-only without silent modification. |
| FR-REP-009 | Should | `init` SHOULD automatically suggest the discovered `.flp` if exactly one candidate exists. |
| FR-REP-010 | Must | If zero or multiple `.flp` files exist, the user MUST explicitly designate the primary artifact. |

## 8.1 Configuration Schema v1

`dawvc.yaml` MUST support the following canonical fields:

```yaml
schemaVersion: 1
repositoryId: "<uuid>"
projectName: "Apotheosis"
primaryArtifact: "Apotheosis.flp"
defaultBranch: "main"
policies:
  newDependencies: require-add
  missingBundledDependencies: block
  unknownProjectFormat: allow-opaque
  invalidProjectArtifact: require-confirmation
```

- **FR-CFG-001:** Paths in `dawvc.yaml` MUST be relative to the repository root.
- **FR-CFG-002:** Absolute local filesystem paths in shared configuration are strictly forbidden.
- **FR-CFG-003:** Unrecognized configuration keys MUST be preserved when rewriting the manifest.
- **FR-CFG-004:** Missing or corrupted mandatory configuration keys MUST yield typed configuration errors.

---

# 9. Functional Requirements — Scan & Project Detection

| ID | Priority | Requirement |
|---|---|---|
| FR-SCAN-001 | Must | `dawvc scan` MUST inspect primary artifacts, working tree, and dependencies without modifying files. |
| FR-SCAN-002 | Must | Scan MUST identify FLP formats via file extensions and magic headers where available. |
| FR-SCAN-003 | Must | Scan MUST report detection status, confidence score, adapter version, and findings. |
| FR-SCAN-004 | Must | Unrecognized FLP versions MUST be marked `Unsupported` and tracked opaquely. |
| FR-SCAN-005 | Must | Low detection confidence MUST report `Unknown`; DAWVC MUST NOT parse speculatively. |
| FR-SCAN-006 | Must | Violations of known binary format invariants MUST report `Invalid`. |
| FR-SCAN-007 | Must | Parser exceptions MUST be mapped to typed adapter errors without crashing with unhandled stack traces. |
| FR-SCAN-008 | Must | Scan MUST distinguish newly discovered, modified, deleted, and unresolved dependencies. |
| FR-SCAN-009 | Must | Identical file bytes MUST yield identical content-addressed identities across scans. |
| FR-SCAN-010 | Must | Scan MUST NOT load or execute plugin binaries. |
| FR-SCAN-011 | Should | Unchanged files SHOULD be identified via local index metadata without recomputing full BLAKE3 hashes. |
| FR-SCAN-012 | Must | Cancellation requests (Ctrl+C) MUST cleanly terminate scanning without modifying repository state. |

---

# 10. Functional Requirements — Staging & Status

| ID | Priority | Requirement |
|---|---|---|
| FR-STG-001 | Must | DAWVC MUST maintain a local staging index (`.dawvc/index.db`) excluded from shared state. |
| FR-STG-002 | Must | The primary `.flp` and tracked assets MUST automatically stage their active disk state upon commit. |
| FR-STG-003 | Must | Newly discovered external assets MUST NOT be committed silently without explicit staging. |
| FR-STG-004 | Must | `dawvc add <path>` MUST register and stage newly discovered assets. |
| FR-STG-005 | Must | `dawvc add --all` MUST stage all bundlable assets while skipping `Forbidden` and `ReferenceOnly` assets. |
| FR-STG-006 | Must | `UserChoice` assets MUST prompt for confirmation or require an explicit non-interactive flag. |
| FR-STG-007 | Must | `dawvc status` MUST categorize changes into staged, modified, added, removed, unresolved, and policy-blocked. |
| FR-STG-008 | Must | Status output MUST display staged assets and untracked discoveries in separate sections. |
| FR-STG-009 | Must | Status MUST report whether the upcoming commit will be fully reproducible, incomplete, or opaque. |
| FR-STG-010 | Should | File renames SHOULD be identified via content identity and presented as renames rather than deletion/addition. |

---

# 11. Functional Requirements — Content-Addressed Object Store

| ID | Priority | Requirement |
|---|---|---|
| FR-OBJ-001 | Must | Every blob MUST be identified by the BLAKE3 hash computed over its raw payload bytes. |
| FR-OBJ-002 | Must | Identical bytes MUST resolve to the identical blob identity within the repository. |
| FR-OBJ-003 | Must | Blobs MUST be streamed during hashing and disk writes without full in-memory buffering. |
| FR-OBJ-004 | Must | Objects MUST be written to temporary storage, validated, and published atomically. |
| FR-OBJ-005 | Must | An existing object with a given identity MUST NEVER be overwritten with differing bytes. |
| FR-OBJ-006 | Must | Objects MUST contain a 56-byte binary envelope storing magic bytes, version, type, lengths, and payload hash. |
| FR-OBJ-007 | Must | Compression mode is `None` in v0.1; readers MUST reject unknown compression modes safely. |
| FR-OBJ-008 | Must | Metadata objects MUST use canonical UTF-8 JSON with explicit `schemaVersion`. |
| FR-OBJ-009 | Must | Timestamps, file attributes, and local file IDs MUST NOT factor into content identity. |
| FR-OBJ-010 | Must | Process termination during object writes MUST leave at most orphaned temporary files and never corrupt history. |

## 11.1 Canonical Object Envelope Layout

```text
Magic (4 bytes: 'DWVC')
EnvelopeVersion (uint16)
ObjectType (uint16: Blob=1, Tree=2, Snapshot=3, Commit=4)
CompressionMode (uint8: 0=None)
Flags (uint8)
Reserved (6 bytes padding)
PayloadLength (uint64)
PayloadHash (32 bytes BLAKE3)
Payload (N bytes raw stream)
```

Detailed byte offsets are codified in `ADR-OBJ-001`.

---

# 12. Functional Requirements — Dependencies & Portability

| ID | Priority | Requirement |
|---|---|---|
| FR-DEP-001 | Must | The dependency graph MUST model assets, plugins, plugin content, and environment requirements distinctly. |
| FR-DEP-002 | Must | Every dependency MUST define identity, requirement level, source locator, provenance, and portability policy. |
| FR-DEP-003 | Must | Audio samples and recordings MUST be addressed via cryptographic content hash where possible. |
| FR-DEP-004 | Must | Plugin identity MUST include vendor, product name, format (VST3/Native), and recorded version requirement. |
| FR-DEP-005 | Must | Local plugin installation directories MUST NOT form part of plugin identity. |
| FR-DEP-006 | Must | Plugin binaries MUST default to `ReferenceOnly` portability mode. |
| FR-DEP-007 | Must | Commercial sound libraries MUST default to `ReferenceOnly` or `UserChoice`. |
| FR-DEP-008 | Must | Project recordings and custom samples MAY be designated as `Bundle`. |
| FR-DEP-009 | Must | Third-party plugin preset content MUST remain `Unknown` or user-assisted when discovery is inconclusive. |
| FR-DEP-010 | Must | Inferred dependencies MUST track inspection provenance and confidence scores. |
| FR-DEP-011 | Must | New `Bundle` dependencies MUST require explicit acceptance via `dawvc add`. |
| FR-DEP-012 | Must | Missing required `Bundle` dependencies MUST block a normal commit. |
| FR-DEP-013 | Must | `--allow-incomplete` MUST permit an intentional incomplete commit, tagging the snapshot as incomplete. |
| FR-DEP-014 | Must | Missing `ReferenceOnly` requirements MUST NOT block commits, but MUST be highlighted in `dawvc doctor`. |
| FR-DEP-015 | Must | The CLI MUST display policy decisions and prospective payload byte volume prior to bundling. |

---

# 13. Functional Requirements — Commits & Project History

| ID | Priority | Requirement |
|---|---|---|
| FR-COM-001 | Must | `dawvc commit -m <msg>` MUST construct an immutable snapshot and commit object. |
| FR-COM-002 | Must | A commit MUST capture the active `.flp`, tracked assets, dependency graph, and environment metadata. |
| FR-COM-003 | Must | Commits with zero substantive changes MUST be rejected unless empty commits are explicitly permitted. |
| FR-COM-004 | Must | Commit messages MUST contain at least one non-whitespace character. |
| FR-COM-005 | Must | Commit IDs MUST be derived deterministically from canonical commit contents. |
| FR-COM-006 | Must | Branch references MUST be updated atomically only after all objects are safely written and verified. |
| FR-COM-007 | Must | Opaque artifacts MUST be committable without semantic manifests. |
| FR-COM-008 | Must | `Invalid` or `Suspicious` project artifacts MUST require explicit confirmation or `--allow-invalid-artifact`. |
| FR-COM-009 | Must | Non-interactive execution requiring an unsupplied override MUST terminate with a non-zero exit code. |
| FR-COM-010 | Must | `dawvc log` MUST output commit ID, parent hashes, author, timestamp, and message. |
| FR-COM-011 | Must | Repository history MUST remain navigable even if future adapters interpret metadata differently. |

---

# 14. Functional Requirements — Branches & Branch Switching

| ID | Priority | Requirement |
|---|---|---|
| FR-BRA-001 | Must | A newly initialized repository MUST include a default branch named `main`. |
| FR-BRA-002 | Must | `dawvc branch <name>` MUST create a new branch pointer referencing current HEAD. |
| FR-BRA-003 | Must | Branch names MUST be validated against empty strings, path traversal, and ref collisions. |
| FR-BRA-004 | Must | `dawvc switch <name>` MUST check out the target branch snapshot safely. |
| FR-BRA-005 | Must | Switch MUST enforce identical dirty workspace guards as checkout. |
| FR-BRA-006 | Must | MVP v0.1 MUST NOT attempt automated semantic project merging. |
| FR-BRA-007 | Must | Diverging branches remain distinct lines of history; merging is deferred post-MVP. |

---

# 15. Functional Requirements — Local Bindings & Multi-Machine Portability

| ID | Priority | Requirement |
|---|---|---|
| FR-BND-001 | Must | Bindings MUST be persisted locally and strictly excluded from shared commits. |
| FR-BND-002 | Must | Bindings MUST store dependency ID, local path locator, resolution method, status, and verified hash. |
| FR-BND-003 | Must | An asset binding MUST ONLY be marked `Verified` after verifying file bytes against expected hash. |
| FR-BND-004 | Must | Resolver pipeline MUST follow deterministic precedence: repository asset, verified binding, relative path, original path, library mapping, asset index, hash discovery, user selection, unresolved. |
| FR-BND-005 | Must | Files matching the expected name but differing in content hash MUST be tagged `Mismatch`. |
| FR-BND-006 | Must | Users MUST be able to manually bind dependencies to local filesystem paths (`dawvc bind`). |
| FR-BND-007 | Must | An identical asset located at a different path MUST be recognized via content hash. |
| FR-BND-008 | Must | Original host paths MAY only be retained as diagnostic locators, never as dependency identities. |
| FR-BND-009 | Must | Local path bindings MUST NOT leak into shared manifests or commit snapshots. |

---

# 16. Functional Requirements — Safe Checkout & Disaster Recovery

| ID | Priority | Requirement |
|---|---|---|
| FR-CHK-001 | Must | `dawvc checkout <ref>` MUST verify all reachable repository objects prior to workspace materialization. |
| FR-CHK-002 | Must | Checkout MUST verify object hashes, artifact trees, and aggregate hashes. |
| FR-CHK-003 | Must | Checkout MUST abort if any required repository objects are missing or corrupt. |
| FR-CHK-004 | Must | Checkout MUST stage the entire project snapshot into an isolated directory on the same filesystem volume. |
| FR-CHK-005 | Must | The staged candidate MUST be re-verified byte-identically prior to workspace replacement. |
| FR-CHK-006 | Must | ONLY a fully verified candidate directory MAY be installed atomically into the workspace. |
| FR-CHK-007 | Must | Failures prior to publication MUST leave the existing workspace unmodified. |
| FR-CHK-008 | Must | Checkouts targeting a dirty workspace MUST abort by default with exit code 7. |
| FR-CHK-009 | Must | `--restore-to <dir>` MUST materialize snapshots cleanly into an external target directory. |
| FR-CHK-010 | Must | `--force` MUST create a complete recovery copy of local files prior to workspace mutation. |
| FR-CHK-011 | Must | Recovery copy locations MUST be printed to stdout and never deleted by the triggering operation. |
| FR-CHK-012 | Must | Traversal sequences (`..`), absolute paths, duplicate normalized paths, and symlinks MUST be rejected prior to writes. |
| FR-CHK-013 | Must | DAWVC MUST NOT modify or binary-patch the native `.flp` during checkout. |
| FR-CHK-014 | Must | Bundled assets MUST materialize into a managed project asset root. |
| FR-CHK-015 | Must | Checkout MUST emit a summary report displaying artifact health, unresolved bindings, and manual FL Studio steps. |

---

# 17. Functional Requirements — FL Studio Integration

| ID | Priority | Requirement |
|---|---|---|
| FR-FLP-001 | Must | The v0.1 adapter MUST identify FL Studio 2026.x `.flp` files as `SingleFileArtifact`. |
| FR-FLP-002 | Must | The adapter MUST operate read-only and never open write handles to project files. |
| FR-FLP-003 | Must | The adapter MUST extract project format and version metadata where reliably available. |
| FR-FLP-004 | Must | The adapter MUST extract sample and recording references best-effort. |
| FR-FLP-005 | Must | The adapter MUST extract plugin identities best-effort without initializing binaries. |
| FR-FLP-006 | Must | Indeterminate plugin presets MUST be recorded as `Unknown` or user-assisted. |
| FR-FLP-007 | Must | Extractions MUST document source locator, confidence score, timestamp, and adapter version. |
| FR-FLP-008 | Must | Unknown or newer FLP formats MUST degrade safely to opaque snapshotting and byte-exact restoration. |
| FR-FLP-009 | Must | Capabilities `NativeWrite`, `NativeRoundTripValidation`, and `NativeMerge` MUST be disabled in v0.1. |
| FR-FLP-010 | Must | The adapter MUST NOT spawn or drive FL Studio for scanning or validation. |
| FR-FLP-011 | Must | The adapter MUST NOT load or execute third-party plugin binaries. |
| FR-FLP-012 | Must | Feasibility spikes MUST establish supported fixture matrices and parser boundaries before implementation. |

## 17.1 Asset Relinking Boundaries

FL Studio resolves missing samples via configured Browser *Extra Search Folders*. DAWVC v0.1 does not mutate user DAW preferences automatically (see [FL Studio Search Paths & Relinking Guide](FLStudio_Search_Paths_Relinking.md)).

- **FR-FLP-013:** `dawvc doctor` MUST report the local managed asset root for users to register in FL Studio.
- **FR-FLP-014:** `dawvc doctor` MUST explicitly notify users when manual relinking is expected.
- **FR-FLP-015:** MVP acceptance requires all bundled bytes to be present and verified; automated path binding inside FL Studio is outside v0.1 scope.

---

# 18. Functional Requirements — Diagnostics (`dawvc doctor`)

| ID | Priority | Requirement |
|---|---|---|
| FR-DOC-001 | Must | `dawvc doctor` MUST evaluate artifact health, dependency health, and environment health independently. |
| FR-DOC-002 | Must | Doctor MUST compare installed FL Studio versions with recorded project requirements when discoverable. |
| FR-DOC-003 | Must | Doctor MUST verify that installed plugins satisfy minimum version compatibility (`installed >= required`). |
| FR-DOC-004 | Must | Doctor MUST report asset bindings as `Verified`, `Unresolved`, or `Mismatch`. |
| FR-DOC-005 | Must | Doctor MUST clearly distinguish `Bundle`, `ReferenceOnly`, `UserChoice`, `Forbidden`, and `Unknown` modes. |
| FR-DOC-006 | Must | Doctor MUST separate blocking errors from non-blocking diagnostic warnings. |
| FR-DOC-007 | Must | Doctor MUST provide actionable remediation instructions for identified mismatches or missing assets. |
| FR-DOC-008 | Must | Doctor MUST NOT certify reproducibility when `ReferenceOnly` or `Unknown` dependencies are unresolved. |
| FR-DOC-009 | Must | Doctor MUST emit machine-readable JSON reports when run with `--json`. |
| FR-DOC-010 | Must | Process exit code MUST reflect whether blocking diagnostic errors were detected (code 6). |

---

# 19. Functional Requirements — Integrity Verification (`dawvc fsck`)

| ID | Priority | Requirement |
|---|---|---|
| FR-FSC-001 | Must | `dawvc fsck` MUST inspect all reachable repository objects and references. |
| FR-FSC-002 | Must | `fsck` MUST verify object hashes, commit parent links, snapshot refs, manifests, and artifact trees. |
| FR-FSC-003 | Must | `fsck --artifacts` MUST perform deep validation of physical workspace files against stored hashes. |
| FR-FSC-004 | Must | Repository corruption and invalid native project bytes MUST be reported in separate health domains. |
| FR-FSC-005 | Must | Adapter parsing failures MUST NOT be misclassified as object store hash corruption. |
| FR-FSC-006 | Must | `fsck` MUST support machine-readable JSON output via `--json`. |
| FR-FSC-007 | Must | `fsck` MUST NOT perform automated destructive repairs in v0.1. |
| FR-FSC-008 | Must | Unreachable objects MAY be reported as warnings without being treated as repository corruption. |

---

# 20. CLI Architecture & Error Handling

## 20.1 General CLI Requirements

- **FR-CLI-001:** Every command MUST support `--help`.
- **FR-CLI-002:** The root CLI executable MUST support `--version`.
- **FR-CLI-003:** Interactive prompts MUST ONLY be displayed when stdin is attached to an interactive terminal.
- **FR-CLI-004:** Every interactive prompt MUST provide a non-interactive flag or deterministic failure path.
- **FR-CLI-005:** Terminal output MUST be human-readable by default; diagnostic commands MUST support `--json`.
- **FR-CLI-006:** Credentials, passwords, and sensitive directories MUST be redacted in logs and error output.
- **FR-CLI-007:** `--verbose` MUST display resolution provenance without dumping raw unhandled stack traces.
- **FR-CLI-008:** `--no-color` MUST disable ANSI terminal styling.
- **FR-CLI-009:** Cancellation via Ctrl+C MUST terminate gracefully, cleaning up staging files.

## 20.2 Exit Code Mapping

| Exit Code | Category |
|---:|---|
| 0 | Success; no blocking issues. |
| 1 | General unexpected application error. |
| 2 | Invalid CLI arguments or repository not found. |
| 3 | Corrupt repository integrity. |
| 4 | Dirty working tree preventing operation. |
| 5 | Missing required bundle dependencies. |
| 6 | Diagnostic failure (`doctor` or `fsck` errors). |
| 7 | Invalid arguments or syntax error. |
| 8 | Filesystem or atomic install failure. |
| 9 | Operation cancelled by user. |

- **FR-ERR-001:** Typed domain errors MUST deterministically map to standard exit codes.
- **FR-ERR-002:** Known errors MUST print a brief summary, root cause, and remediation guidance.
- **FR-ERR-003:** Unexpected errors MUST log a unique correlation ID.
- **FR-ERR-004:** JSON output MUST use stable string error codes independent of localized text.

---

# 21. Non-Functional Requirements — Integrity & Reliability

| ID | Requirement |
|---|---|
| NFR-INT-001 | Checked-out `.flp` files MUST be cryptographically byte-identical to committed payloads. |
| NFR-INT-002 | Directory/package artifacts MUST be representable deterministically by the core model. |
| NFR-INT-003 | Branch reference updates MUST occur atomically. |
| NFR-INT-004 | Workspace checkout installations MUST be staged and atomic or demonstrably rollback-safe. |
| NFR-INT-005 | Fault injection immediately prior to publication MUST NOT corrupt previously reachable state. |
| NFR-INT-006 | Repeated commits of identical canonical state MUST generate identical snapshot identities. |
| NFR-INT-007 | Repository objects MUST be verified for length and payload hash upon read during integrity scans. |
| NFR-INT-008 | Opaque versioning MUST remain functional when no DAW adapter is registered. |

---

# 22. Non-Functional Requirements — Performance & Scale

| ID | Requirement |
|---|---|
| NFR-PERF-001 | MVP MUST support repositories tracking up to 5,000 assets. |
| NFR-PERF-002 | MVP MUST process up to 100 GB bundled content using streaming I/O without buffering entire files in memory. |
| NFR-PERF-003 | Peak memory utilization SHOULD remain comfortably below 512 MB on reference fixtures. |
| NFR-PERF-004 | Unchanged warm `status` SHOULD complete within 2 seconds on local SSDs. |
| NFR-PERF-005 | Initial hashing MUST stream data and be bound strictly by disk throughput. |
| NFR-PERF-006 | Hashing MUST utilize bounded concurrency to keep the system responsive. |
| NFR-PERF-007 | Unchanged assets SHOULD be accepted via cached metadata without full re-hashing. |
| NFR-PERF-008 | CLI startup without full repository scans SHOULD complete within 500 ms. |

---

# 23. Non-Functional Requirements — Security & Privacy

| ID | Requirement |
|---|---|
| NFR-SEC-001 | Native project data and manifests MUST be handled as untrusted adversarial input. |
| NFR-SEC-002 | Parsers MUST enforce byte bounds checks, timeouts, and resource limits. |
| NFR-SEC-003 | Path traversal (`..`) and symlink escapes MUST be rejected before materialization. |
| NFR-SEC-004 | DAWVC MUST NOT execute, load, or install third-party plugin binaries. |
| NFR-SEC-005 | DAWVC MUST operate 100% offline without requiring network connectivity in v0.1. |
| NFR-SEC-006 | MVP MUST NOT transmit telemetry or analytics. |
| NFR-SEC-007 | Logs MUST NOT contain credentials and SHOULD redact user profile directories. |
| NFR-SEC-008 | Temporary and recovery files MUST only be created in project-bound folders. |
| NFR-SEC-009 | Archive extraction routines MUST NOT be exposed in `.flp` workflows unless explicitly secured. |

---

# 24. Non-Functional Requirements — Compatibility & Maintainability

| ID | Requirement |
|---|---|
| NFR-CMP-001 | Public technical preview officially supports Windows 11 x64. |
| NFR-CMP-002 | FL Studio adapter is validated against FL Studio 2026.x fixtures. |
| NFR-CMP-003 | Older, newer, or unsupported FLP versions SHOULD remain opaquely versionable. |
| NFR-CMP-004 | Persisted JSON objects MUST include a `schemaVersion` starting from v1. |
| NFR-CMP-005 | Unrecognized future fields SHOULD be preserved during round-trip deserialization. |
| NFR-MNT-001 | `DawVcs.Domain` MUST NOT reference Infrastructure, CLI, or Adapter projects. |
| NFR-MNT-002 | CLI and future GUI clients MUST invoke identical Application use cases. |
| NFR-MNT-003 | Every adapter capability MUST be independently testable and explicitly declared. |
| NFR-MNT-004 | Public JSON schemas and diagnostic reports require golden compatibility tests. |
| NFR-MNT-005 | Architectural decisions MUST be codified in Architecture Decision Records (ADRs). |

---

# 25. Packaging, Licensing & Distribution

| ID | Requirement |
|---|---|
| NFR-REL-001 | Release MUST be distributed as a self-contained `win-x64` executable and ZIP archive. |
| NFR-REL-002 | Users MUST NOT be required to install a separate .NET runtime. |
| NFR-REL-003 | The repository MUST include an MIT license file. |
| NFR-REL-004 | The repository MUST provide a comprehensive README with setup, quick start, and recovery guides. |
| NFR-REL-005 | The release MUST publish cryptographic SHA-256 checksums for distribution artifacts. |
| NFR-REL-006 | Test fixtures MUST contain only custom or royalty-free public domain audio. |
| NFR-REL-007 | Documentation MUST explicitly state that plugin binaries and sound libraries are not bundled. |
| NFR-REL-008 | Documentation MUST explicitly state that manual FL Studio search path setup may be required. |

---

# 26. Acceptance Criteria

## AC-001 — Walking Skeleton
```gherkin
Given a valid repository tracking one primary .flp project
When the user executes init, commit, and log
And checks out the commit into an empty target directory
Then log displays the recorded commit
And the restored .flp is byte-identical to the original file
And the source project file was never modified by DAWVC
```

## AC-002 — Content Deduplication
```gherkin
Given two local paths referencing identical audio sample bytes
When both are registered as project dependencies
Then both point to the same content-addressed blob identity
And the audio bytes are stored only once in the object store
```

## AC-003 — Modified Content with Identical Name
```gherkin
Given a tracked asset named kick.wav
When the audio bytes change while the filename remains identical
Then a new content-addressed identity is generated
And the old local binding is not reused as verified
```

## AC-004 — Explicit Staging Required for New Dependencies
```gherkin
Given a scan that discovers a new bundlable audio sample
When the user runs commit directly
Then the sample is not silently committed
And commit reports that explicit staging is required
When the user executes dawvc add and commits again
Then the sample is safely bundled into the snapshot
```

## AC-005 — Missing Required Bundle Dependency
```gherkin
Given a missing required Bundle dependency
When the user executes a normal commit
Then the command aborts with exit code 5
When the user commits with --allow-incomplete
Then an incomplete snapshot is recorded
And dawvc doctor highlights the missing dependency as a blocking issue
```

## AC-006 — Missing ReferenceOnly Plugin
```gherkin
Given a project referencing an uninstalled commercial plugin
When the user executes commit
Then the plugin binary is not bundled
And the commit succeeds
And dawvc doctor reports the plugin as a missing ReferenceOnly requirement
```

## AC-007 — Unknown FLP Version Fallback
```gherkin
Given an unrecognized or future FL Studio project version
When the user runs scan and commit
Then semantic parsing is bypassed
And the project artifact is committed opaquely
And can subsequently be restored byte-identically
```

## AC-008 — Invalid FLP Project
```gherkin
Given a truncated FLP file violating core binary header invariants
When the user runs commit
Then DAWVC requires confirmation or --allow-invalid-artifact
And repository integrity remains separate from native artifact health
```

## AC-009 — Dirty Workspace Checkout Guard
```gherkin
Given a workspace with uncommitted modifications
When the user runs checkout
Then checkout aborts with exit code 7
And local workspace bytes remain completely untouched
When the user runs checkout with --restore-to <dir>
Then the snapshot is cleanly restored into the designated folder
```

## AC-010 — Forced Checkout with Recovery Copy
```gherkin
Given a workspace with uncommitted modifications
When the user executes checkout --force
Then a complete recovery backup of local files is generated first
And the recovery backup directory path is printed to stdout
And only then is the verified snapshot installed
```

## AC-011 — Process Interruption During Checkout
```gherkin
Given an active valid workspace
When the checkout process is terminated mid-staging
Then the active workspace remains byte-identically untouched
And the incomplete candidate is discarded
```

## AC-012 — Repository Integrity vs. Adapter Failure
```gherkin
Given a valid stored blob that the adapter cannot parse
When dawvc fsck is executed
Then repository integrity reports the blob as intact
And artifact health reports the adapter parsing issue separately
And zero ObjectHashMismatch errors are reported
```

## AC-013 — Cross-Machine Portability
```gherkin
Given a repository authored on Machine A
And Machine B features a different local folder structure
When Machine B runs checkout and doctor
Then all bundled audio samples are materialized
And identical local assets are discovered by hash
And doctor presents the managed asset root with FL Studio instructions
```

## AC-014 — Performance Baseline
```gherkin
Given a reference repository containing 5,000 tracked assets on a local SSD
And a warm valid local index
When dawvc status is executed
Then the command completes within 2 seconds
And memory usage remains below 512 MB
```

---

# 27. MVP Release Gate

MVP v0.1 may be released as a public technical preview when:

- All Must requirements are implemented and verified by automated tests;
- Acceptance criteria AC-001 through AC-013 pass reproducibly;
- AC-014 performance metrics are measured and documented;
- All FL Studio 2026.x test fixtures pass;
- Unknown and invalid fixtures degrade safely;
- Fault injection validates that aborted operations never publish corrupt repository state;
- Distribution package runs self-contained on a clean Windows 11 VM without .NET runtime pre-installed;
- README, MIT license, security disclosures, and known limitations are published;
- Cryptographic SHA-256 checksums are verified;
- Zero copyrighted or non-distributable assets reside in fixtures or releases.

---

# 28. Traceability to Technical Design

| Requirement Group | Technical Design Reference Sections |
|---|---|
| Repository & Config | Sections 5, 9, 10, 22–26 |
| Artifacts & Object Store | Sections 11, 23–26, 42–46 |
| Dependencies & Bindings | Sections 12–14, 21, 29–30 |
| Adapter & FLP Inspection | Sections 17–18, 53–54, 60 |
| Checkout & Recovery | Sections 27, 45–46 |
| Branching | Sections 31–33 |
| Security & Licensing | Sections 48–50 |
| Errors & Diagnostics | Sections 51–52 |
| Testing & Verification | Sections 53–56, 64 |

---

# 29. Deferred Requirements

The following capabilities are formally deferred to post-MVP releases:

- Remote server, synchronization protocol, and authentication;
- Multi-user project locking;
- Desktop GUI client (Avalonia UI);
- Semantic project diffing and merging;
- Native project writing via adapters;
- Secondary DAW adapters (Ableton Live, Logic Pro, Studio One, REAPER);
- Directory, package, and archive artifacts in primary workflows;
- Collaboration snapshots and cross-DAW project exchange;
- Object compression, CDC chunking, and partial checkout;
- Automated telemetry and crash reporting.
