# Security Threat Model Review & Dependency Audit (WP-09)

**Document Version:** 1.0  
**Date:** September 14, 2026  
**Status:** Approved (WP-09 / IMP-0911, IMP-0912)  
**Requirements:** `NFR-SEC-001` through `NFR-SEC-009`, `NFR-INT-001` through `NFR-INT-008`, `NFR-PERF-001` through `NFR-PERF-008`.

---

## 1. Objective and Scope

This document details the formal security and resilience review of DAWVC for the v0.1 MVP release. It documents the audit of external NuGet dependencies (`IMP-0911`) and evaluates the implemented mitigations against the threat model (`IMP-0912`).

---

## 2. NuGet Dependency & License Audit (IMP-0911)

All runtime dependencies in `src/` were analyzed for licensing terms and potential security risks.

| Package | Version | Publisher | License | Role / Application | Risk Assessment |
| :--- | :--- | :--- | :--- | :--- | :--- |
| `Blake3` | 0.4.2 | Kexik / BLAKE3 Team | **MIT / Apache-2.0** | Cryptographic content hashing | Very Low. Official wrapper to SIMD/AVX-512 BLAKE3 C-core. |
| `Microsoft.Data.Sqlite` | 10.0.0 | Microsoft | **MIT** | Staging index and caching | Very Low. Local SQLite storage in `.dawvc/index.db`. |
| `SQLitePCLRaw.bundle_e_sqlite3` | 2.1.10 | Eric Sink | **Apache-2.0** | Native SQLite engine | Very Low. Trusted embedded SQLite binary. |
| `Dapper` | 2.1.66 | Stack Overflow | **Apache-2.0** | Micro-ORM for SQLite index | Very Low. Parameterized queries, no dynamic SQL injection risk. |
| `YamlDotNet` | 16.3.0 | Antoine Aubry | **MIT** | Repository configuration parser | Low. Strictly typed deserialization without type-instantiation injection. |
| `Microsoft.Extensions.FileSystemGlobbing` | 10.0.0 | Microsoft | **MIT** | File filtering and ignore patterns | Very Low. Managed path matching. |
| `Microsoft.Extensions.DependencyInjection` | 10.0.0 | Microsoft | **MIT** | Inversion of Control in CLI | Very Low. Standard .NET container. |
| `Spectre.Console` | 0.50.0 | Patrik Svensson | **MIT** | Terminal interface rendering | Very Low. No external I/O. |
| `System.CommandLine` | 2.0.0-beta4.* | Microsoft | **MIT** | CLI parsing and options | Very Low. Safe argument parsing. |

**License Audit Conclusion:**
1. **100% Permissive:** All dependencies are licensed under MIT or Apache-2.0.
2. **No Copyleft / GPL:** No GPL, AGPL, or other viral/restrictive licenses exist. DAWVC can be distributed commercially and privately without legal restrictions.

---

## 3. Threat Model Checklist & Validated Mitigations (IMP-0912)

### 3.1 Untrusted Project Data & Parser Boundaries (`NFR-SEC-001`, `NFR-SEC-002`)
- **Threat:** Adversarially crafted `.flp` files (heap spraying, infinite loops, buffer overflows, oversized declared payload lengths).
- **Mitigation:**
  - `FlpBinaryReader` reads with strict bounds: maximum 2 MB (`MaxMetadataScanBytes`) for header/metadata inspection.
  - Event payload limit: maximum 10 MB per event (`MaxEventPayloadBytes`).
  - LEB128 shift guard: maximum 28-bit shift to prevent integer overflows.
  - Stream boundary validation: truncated streams are classified as `Suspicious` or `Invalid` and never cause unhandled exceptions.
  - Fuzz-tested via `FlpFuzzTests` against random and malformed byte streams.

### 3.2 Path Traversal & Symlink Escape (`NFR-SEC-003`, `FR-CHK-012`)
- **Threat:** A project contains crafted traversal paths (`../../Windows/System32` or absolute paths like `C:\Windows`) intended to overwrite arbitrary files upon checkout.
- **Mitigation:**
  - `PathSecurityGuard.ValidateAll` inspects all paths prior to materialization.
  - Paths containing `..`, root indicators (`/`, `\`, `C:`), or unnormalized segments are immediately rejected with `PathSecurityException`.
  - Materialization is strictly restricted within the designated target directory.

### 3.3 No Plugin Execution (`NFR-SEC-004`)
- **Threat:** Execution of suspicious or malicious VST/CLAP/AU binaries during scanning or inspection.
- **Mitigation:**
  - DAWVC under no circumstances loads or executes plugin binaries (`.dll`, `.vst3`, `.exe`).
  - Plugin identification is performed purely declaratively via string parsing of project metadata and binary file header inspection.

### 3.4 Air-Gapped Privacy & Zero Telemetry (`NFR-SEC-005`, `NFR-SEC-006`)
- **Threat:** Exfiltration of project metadata or user identity to remote servers.
- **Mitigation:**
  - DAWVC contains zero network clients and zero telemetry subsystems.
  - All operations (hashing, indexing, branching, diagnostics) operate 100% offline.

### 3.5 Log Privacy & Path Redaction (`NFR-SEC-007`, `IMP-0908`)
- **Threat:** Leakage of personally identifiable information (PII) or user directories through logs and diagnostic output.
- **Mitigation:**
  - `PathRedactor` redacts user-specific profile paths (`C:\Users\Username\...` → `C:\Users\<user>\...`).
  - URL credentials (`user:password@host`) are automatically sanitized to `://<redacted>@`.

### 3.6 Controlled Temporary Files (`NFR-SEC-008`)
- **Threat:** Temporary files in public directories (`C:\Temp`) susceptible to race conditions (TOCTOU) or symlink redirection attacks.
- **Mitigation:**
  - All temporary files are created exclusively inside the project-bound directory (`.dawvc/objects/.tmp/` or the target directory for atomic file moves).
  - Unique GUID-based filenames prevent collisions and predictability.

---

## 4. Resilience & Crash Recovery (`NFR-INT-003..005`, `IMP-0909`)

- **Atomic Writes:**
  - Objects are written streaming to `.tmp` and published atomically via `File.Move(..., overwrite: false)`.
  - Ref updates (`HEAD`, `refs/heads/*`) utilize `AtomicFileWriter` (`File.Move(..., overwrite: true)`).
- **Fault Injection Validation (`FaultInjectionAcTests`):**
  - Simulated I/O errors or process kills immediately prior to publication do not corrupt previously reachable repository state.
  - `dawvc fsck` confirms that the object store and commit DAG remain 100% intact following aborted operations.
- **SQLite Crash Recovery:**
  - `SqliteStagingIndex` configures SQLite in WAL mode (`PRAGMA journal_mode=WAL; synchronous=NORMAL;`).
  - Upon database corruption, automatic reconstruction recreates the local index without data loss.

---

## 5. Performance & Concurrency Baseline (`NFR-PERF-001..008`)

- **5,000 Assets Benchmark (`NFR-PERF-001`, `NFR-PERF-003`, `NFR-PERF-004`):**
  - Reference repository containing 5,000 synthetic audio assets commits successfully.
  - Peak memory usage remains comfortably below 512 MB.
  - Warm status check for an unchanged workspace completes in under 2 seconds.
- **100 GB Streaming I/O (`NFR-PERF-002`):**
  - Objects are streamed in 64 KB buffers without buffering entire files in memory.
  - Memory consumption remains low and constant (< 50 MB) regardless of asset size.
- **Bounded Concurrency (`NFR-PERF-006`):**
  - Hashing and filesystem scanning leverage `ConcurrencyLimiter` bounded to `Math.Clamp(ProcessorCount, 1, 8)`.


## SSH synchronization extension (23 November 2026)

The historical local-only assessment does not cover network hosting. The SSH
extension establishes the following explicit boundaries (FR-SSH-001..009):

- OpenSSH authenticates the server (strict known_hosts) and user (public key/agent),
  encrypts the channel and denies interactive/password fallback. No custom crypto.
- Argument-list execution and a constant remote command prevent URL-to-shell
  interpolation. Protocol aliases map to operator-owned repository allowlists.
- Forced commands/restricted keys and OS permissions are required to prevent
  bypass via an unrestricted SSH shell. Read access is allowlisted; writes need
  AllowPush. No centralized tenant/user authorization is claimed.
- Bounded frames, metadata, object bodies, transfer totals, requests and deadlines
  limit individual sessions. Operators enforce aggregate connection/disk quotas.
- Host filesystem reads and writes reject symlinks/reparse points in metadata.
  Integrity/quarantine/fast-forward/conditional-ref checks still run at receipt.
- stdout is protocol-only; error codes omit raw host exceptions/paths. SSH stderr
  is drained without exporting arbitrary server output. URLs cannot persist
  passwords through remote add. Keys remain in OpenSSH/agent storage.
- EOF/deadlines dispose sessions; abrupt loss after publication can lose the
  acknowledgement. Cooperating ref locks do not protect against hostile local
  actors or older writers ignoring the lock. No automatic destructive recovery.

See ADR-REM-002, Remote_Protocol.md and Self_Hosting_SSH.md for the implemented
contract. Linux protocol/OpenSSH validation is not a full production security
audit or certification of every NAS/Windows/OpenSSH deployment.


## Bare provisioning review — WP-SYNC-04

Provisioning inherits an explicit compatible source identity and shared config;
there is no first-writer identity adoption. Source reads use existing filesystem
or strict-key SSH transports. History ingestion reuses Fetch envelope/hash/path
validation; no source index, binding store or native project execution occurs.
SSH exposes no arbitrary-path repository-creation command. Host path/alias and
AllowPush policies remain administrative settings.

Local destinations reject URLs, existing files/nonempty dirs and link/junction
ancestry, and are rechecked before publication. A same-parent staged directory is
renamed only after validation and resource disposal. Concurrent destination data
is not recursively removed. These path checks do not sandbox malicious parent
replacement; restrict parent ACLs/permissions to trusted operators. Cleanup may
leave staging after a crash/permissions failure and is not secure erasure. Existing
empty placeholders may be absent if final publication fails. Remote sources can
consume staging space up to existing transfer limits; provision adequate disk.


## Explicit snapshot Merge review — WP-SYNC-05

Merge performs no network calls, native FLP editing or plugin execution. Input
references use the existing local resolver; fetched objects must already have
passed transport verification. Merge additionally validates bounded commit
metadata/ancestry and both tip snapshots/blobs, with existing path traversal and
collision guards. Unknown schemas/type mismatches and missing/corrupt objects
fail before workspace publication. Whole-snapshot selection avoids silently
combining incompatible FLP/asset sets. Both parent histories stay reachable.

No force or implicit history rewrite. Dirty/staged work and selected untracked
collisions are refused. Expected branch/tip checks reduce callback-observable
state-change windows; a hostile/concurrent local writer is not sandboxed and
there is no global workspace lock. Existing journal/rollback/crash boundaries
apply. Earlier snapshots/blobs require Fsck; an unreachable merge object may
remain on failure. Selected incomplete snapshots remain incomplete.

Merge-specific mutation guards also reject reserved `.dawvc`/`.git`/`dawvc.yaml`
artifact roots and link/junction ancestry in affected workspace/metadata paths.
Checks repeat after the publication progress callback; trusted local writers
must still avoid concurrent path replacement. Snapshot container shape and
aggregate hash must match their entries. The new merge object must itself fit
the metadata/graph limits (including its two parent edges).


## Durable recovery review — WP-SYNC-07

Recovery data is local/private: actual musical bytes and relative manifest names
must not enter shared objects, arbitrary-path server operations or CI uploads.
Read-only inspection exposes counts/IDs, not backup contents. Explicit rollback
validates repository/schema, bounded JSON/list sizes, canonical paths/reserved
metadata roots, link ancestry, derived backup names and BLAKE3 before/after proofs.
All initial targets and backups are preflighted; copies and targets are rechecked
before atomic replacement. Unknown edits, bad manifests/backups and multiple
transactions preserve evidence and refuse; no force or automatic repair.

Exclusive ownership prevents recovering a live publisher; existing ref-write
ownership prevents cooperating ref writers during replay. Older clients, raw
backend APIs, malicious same-user path replacement and already-started writers
are outside global serialization. Do not run/save concurrently. Terminal envelopes
leave the active namespace before deletion, so interrupted cleanup is not mistaken
for a damaged active manifest. Private orphan data/locks require ownership-aware
operator handling; secure erasure is not claimed. Power-cycle/controller/NAS
qualification is separate from actual process-kill regression evidence.

Recovery envelopes are owner-only directories: Unix 0700, Windows protected DACL
with inheritable full control for the current user. Restoration copies stay
inside that private envelope before rename, rather than exposing old musical
bytes in public workspace temporary files. These controls do not exclude OS
administrators/privileged backup tools or certify a NAS ACL implementation.

### WP-SYNC-09: preview/upstream integration

Track validates names and configured/local branch identity, updates settings
atomically offline and honors operation/recovery guards. Fresh previews verify
missing metadata envelopes, identities/types/schema and paths with fixed graph/
byte budgets; they do not create quarantine or clear unused SQLite pools. Preview
blob availability/integrity and dirty work remain unverified. Do not treat a
preview as a reserved or atomic view during other writers. Existing host-key,
allowlist, push policy and protocol limits remain; credentials/paths are not added
to the preview DTO. Custom providers retain the read-only contract obligation.
