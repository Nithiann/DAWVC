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

- **Supported Transports:** DAWVC v0.2 supports remote collaboration via the filesystem transport (`FileSystemRemoteTransport`), covering local paths, external drives, and network UNC shares (`\\server\share\repo`). The SSH extension adds `dawvc://` through OpenSSH; HTTPS and standard Git hosting remain unsupported. See [Self-hosting SSH](Self_Hosting_SSH.md).
- **Fast-Forward Only Synchronization:** In v0.2, synchronization operations (`dawvc pull` and `dawvc push`) enforce fast-forward integration (`--ff-only`). If histories diverge (e.g. concurrent commits made on different machines), the pull is rejected with exit code `9` (`NonFastForward`) to prevent automated binary corruption. WP-SYNC-05 adds explicit whole-snapshot Merge to join diverged history; three-way structural merging of DAW projects remains deferred.
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
- If a handled error is encountered, temporary file-level backups are restored in reverse order, returning the workspace to its exact pre-checkout state.
- **Crash Recovery Scope:** WP-SYNC-07 adds explicit persistent recovery for interrupted in-place workspace publication. Physical power-cut/controller/NAS guarantees still require deployment qualification; see the current boundaries below and Recovery Guide.


## Filesystem Push boundaries (23 November 2026)

- The original filesystem Push baseline supports filesystem/file:// and UNC
  repositories with matching identity. The SSH extension below adds authenticated
  network hosting. WP-SYNC-04 adds source-derived bare provisioning; force-push remains unsupported.
- Non-bare checked-out destination branches are refused. Do not concurrently use
  destination worktrees while pushing to inactive branches.
- Push and cooperating local branch/HEAD writers share a destination-wide
  `ref-write.lock`; the file persists after handles close. Never delete it while
  writers are active. Older clients and external writers that ignore it are not
  protected by the conditional-update contract.
- Symbolic links/junctions in writable metadata paths are refused. Filesystem/SMB
  exclusive-open and atomic-rename semantics must be reliable. Native Windows/UNC
  validation is distinct from local Linux validation.
- Complete reachable closure is read/verified on both sides. Metadata objects are
  limited to 2 MiB each; traversal is bounded to 50,000 commits and 1,000,000
  distinct objects. This favors integrity over incremental-read performance.
- Push leaves local upstream and tracking refs unchanged. No workspace is modified.
- Cancellation/crashes can leave unreachable verified objects. Quarantine cleanup
  on disposal is best-effort; cleanup failure or process termination can leave
  `incoming/push-*` directories. There is no automatic GC/reset command.

The historical handled-rollback guarantee in section 7 is conditional: failed
compensation is surfaced as `IncompleteRollbackException` with retained backups.
It is not an unconditional guarantee that every failed mutation leaves the original
state. See the amended [Recovery Guide](Recovery_Guide.md).

## SSH extension boundaries

`dawvc://` works through an installed OpenSSH client and a compatible DAWVC
receiver on the selected PC/NAS/server. These external programs are not bundled.
The host needs SSH running and network reachability; no DAWVC daemon is required.
An SMB-only NAS or ordinary GitHub/GitLab repository is not a DAWVC SSH endpoint.

Unknown or changed host keys and unavailable public-key authentication fail
noninteractively. There is no automatic host-key enrollment, password storage,
DAWVC identity-provider service or unrestricted-path server access. Operator
forced-command/OS policies are necessary for restricted SSH accounts/keys.

SSH aliases and quotas are specified in Remote_Protocol.md. Objects are capped at
8 GiB, metadata at 2 MiB, frames at 1 MiB; client calls at five minutes, host sessions
at ten minutes by default. There is no resumable transfer, quota negotiation,
compression or tenant-wide connection/disk enforcement. These limits constrain
large libraries. Unknown schemes are rejected instead of treated as local paths.
Filesystem Push's no-force, active-worktree, cooperating-writer and recovery
boundaries apply unchanged. A lost publish reply does not imply rollback.

Platform evidence is recorded in WP-SSH-01 in the implementation plan. Real Windows
OpenSSH, specific NAS deployments and actual UNC/SMB storage need independent
validation; a successful Linux loopback test does not establish those guarantees.


## Bare provisioning boundaries (WP-SYNC-04)

`init --bare --from` prepares an empty receiver with source identity;
`clone --bare` copies verified history/all fetched branches without a workspace.
Both reject existing nonempty destinations and links/junctions in destination
ancestry. Bare cloning rejects force. No bare mirror update, source-local
bindings/index copy, random-ID receiver adoption or SSH remote-create operation.
Bare roots are hosting endpoints; use normal Clone to obtain a working copy.

Publication is a same-volume directory rename after preparation. Failures before
publication do not expose a partial repository. Existing empty placeholders are
deleted immediately before rename; a failed rename may leave that placeholder
absent. Parent directories created during preparation may remain. Cleanup is
best-effort; crashes/permissions can leave `.dawvc-provision-*` sibling directories.
There is no provisioning recovery journal, secure erase or protection from a
hostile writer replacing parent directories between checks. Restrict parent
permissions and do not manipulate staging/destination paths concurrently.


## Explicit snapshot Merge boundaries (WP-SYNC-05)

`merge --take local|incoming` chooses an entire immutable snapshot, not individual
tracks, plugins or files. Both original commits remain parents of a new commit.
Only diverged histories with a common ancestor are accepted. No automatic Fetch,
force, dry-run, merge state/abort, semantic FLP merge or ref rewriting. Log remains
first-parent and displays both merge parent IDs for explicit restoration.

Both tip snapshots/blobs and commit histories are verified; earlier snapshots/
blobs need Fsck for exhaustive auditing. Graph/metadata limits are 50k commits,
1m parent edges, 2 MiB/object and 64 MiB cumulative metadata payload. Selected
incomplete snapshots retain their incomplete flag; choosing a version does not
make missing external plugins/libraries reproducible.

Expected branch/tip checks catch changes before checkout publication, but there
is an operation-wide cooperating writer lease in WP-SYNC-08. Older/raw clients
and DAW saves must still be kept exclusive. Handled publication failures use existing
compensation; incomplete rollback can retain recovery data. Hard crashes and
post-publication dependency-resolution failure/cancellation need inspection.
An unreachable immutable merge object can remain after failure; no GC is added.

Merge-specific mutation guards also reject reserved `.dawvc`/`.git`/`dawvc.yaml`
artifact roots and link/junction ancestry in affected workspace/metadata paths.
Checks repeat after the publication progress callback; trusted local writers
must still avoid concurrent path replacement. Snapshot container shape and
aggregate hash must match their entries. The new merge object must itself fit
the metadata/graph limits (including its two parent edges).


## Preview acceptance and deployment qualification

See [Preview acceptance](Preview_Acceptance.md) for automated packaged-CLI evidence
and the separate real Windows/FL Studio/two-PC/OpenSSH/NAS checklist. Synthetic
single-host success does not certify native playback or deployment-specific
locking/rename behavior. Those release gates remain pending until measured on
actual supported setups; no GUI or additional DAW support is implied.


## Durable workspace recovery boundaries (WP-SYNC-07)

Persistent local manifests/backups now cover in-place Checkout/Switch and shared
fast-forward publication (Pull, explicit Merge, initial Clone checkout). Prepared
recovery restores pre-publication bytes/HEAD/ref/staged intent; Committed recovery
verifies and finalizes the completed version. Unexpected edits/corruption retain
recovery data and refuse; there is no forced overwrite or implicit repair.

Backup capacity is additional to candidate and legacy handled-journal storage.
Metadata is limited to schema 1, 16 MiB, 10k files and 10k staged entries. Recovery
is logical/content-based, not a complete filesystem/database-image backup. Private
orphan preparation, terminal cleanup, staging and recovery copies can remain;
cleanup is not secure erasure. No GC/reset command is introduced.

The ownership lock prevents simultaneous publication/recovery, and existing ref
locks protect recovery metadata writes. WP-SYNC-08 adds serialization for upgraded stock writers. Older
clients/raw backend calls and external DAW/file edits are not globally serialized.
Do not mutate/save concurrently. External restore, Commit, Push and bare storage
provisioning are not newly crash-journaled. Process-kill tests do not certify true
power loss, directory-rename ordering or specific NAS/SMB behavior. Those native
field gates remain open; see Preview_Acceptance.md and Recovery_Guide.md.

Recovery backup directories require owner-only permissions (Unix 0700 or a Windows
protected owner DACL). Non-ACL filesystems or NAS identity/ACL mappings may reject
preparation before workspace mutation. Journaled workspaces require storage with
qualified permissions and atomic rename semantics; bare remote storage is a
separate scope. Restore does not preserve original file ACLs/modes.


## Cooperative operation ownership (WP-SYNC-08)

Stock local writers, Status/Doctor and incoming push sessions now share an
operation-wide OS handle lease; independent processes/callbacks fail fast rather
than wait. This supersedes earlier statements that stock operations lack a global
writer lease. Local busy is exit 11; incoming busy is remote exit 8. Network waits
hold local ownership, and self-push to the same physical repository is refused.
Creation uses a persistent hashed sibling lock and needs parent write permission.

Older binaries, raw context/index calls, custom overrides/providers, DAW/editor
saves and external restore destinations remain outside this cooperative protocol.
External restores require exclusive separate targets. Read-only inspection is not
an atomic view. No new crash journal for Commit/Push/bare provisioning, automatic
lock stealing/cleanup, GC, distributed locking service or NAS qualification is
added. Persistent empty coordination files are normal. Native FL Studio/two-PC,
physical-power and actual storage permission/locking/rename tests remain open.
