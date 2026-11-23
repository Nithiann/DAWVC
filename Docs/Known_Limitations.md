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
- **Fast-Forward Only Synchronization:** In v0.2, synchronization operations (`dawvc pull` and `dawvc push`) enforce fast-forward integration (`--ff-only`). If histories diverge (e.g. concurrent commits made on different machines), the pull is rejected with exit code `9` (`NonFastForward`) to prevent automated binary corruption. Three-way structural merging of DAW projects is planned for future releases.
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
- **Crash Recovery Scope:** Unhandled terminations (e.g. `SIGKILL`, abrupt power outage, or OS panic) mid-publication cannot be rolled back until persistent on-disk transaction manifests (`.dawvc/transactions/<id>/`) are implemented in a future release.


## Filesystem Push boundaries (23 November 2026)

- The original filesystem Push baseline supports filesystem/file:// and UNC
  repositories with matching identity. The SSH extension below adds authenticated
  network hosting. Neither extension adds `init --bare` or force-push.
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

## Upstream workflow and deferred preview work

`remote track` configures existing local branches (including active unborn HEAD)
against configured remotes; the remote branch may be created later. It does not
probe permissions, host availability or repository identity. Those checks happen
when transferring. Clone's automatic origin/upstream setup remains available.
No automatic post-Push tracking update or `push --set-upstream` is introduced.

Pull now resolves upstream before Fetch and explicit source branches integrate
into the active local branch. However, fresh `pull --dry-run` remains incomplete:
Fetch preview discards incoming metadata before Pull checks cached tracking refs
and ancestry. New changes can be missed or an absent tracking branch can fail.
An explicit Fetch can populate that cache, but writes objects/tracking itself.
Fetch preview can also create temporary quarantine metadata/prune stale incoming
directories. Fully fresh preview/no-write semantics are WP-SYNC-03. Do not treat
this WP as a fix for those existing preview limitations. Bare-host provisioning
remains WP-SYNC-04. Windows native Ctrl+C and NAS/UNC require separate validation.
