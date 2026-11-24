# Disaster Recovery & Workspace Safety Guide

**Target Audience:** Audio engineers and developers managing repositories  
**Requirement Mapping:** `NFR-INT-004`, `NFR-INT-005`, `R-010`, `FR-CHK-003`, `FR-CHK-005`

---

## 1. Workspace Safety Model

In music production, losing uncommitted audio takes, recorded vocal recordings, or project edits is catastrophic. DAWVC enforces strict safety rules to guarantee that uncommitted work is never silently overwritten.

```text
┌─────────────────────────────────────────────────────────────┐
│                    Zero-Data-Loss Rule                      │
│   DAWVC will NEVER overwrite or delete uncommitted local    │
│   modifications without an explicit --force flag AND an     │
│   automatic pre-install recovery archive.                   │
└─────────────────────────────────────────────────────────────┘
```

---

## 2. Dirty Workspace Protection

When you attempt to switch branches or checkout a commit while your workspace has unsaved modifications:

```powershell
dawvc switch experimental-arrangement
```

If local files have been modified since the last commit, DAWVC halts immediately with **exit code 4 (Dirty Working Tree)**:

```text
Error: Workspace contains uncommitted modifications to tracked assets:
  - Project.flp (modified)
  - bass_recording.wav (modified)

Action:
  1. Commit your changes: dawvc commit -m "WIP: save before switching"
  2. Or restore to a clean folder: dawvc checkout <target> --restore-to <new-dir>
  3. Or override with force: dawvc switch experimental-arrangement --force
```

---

## 3. Forced Checkout & Automatic Recovery Copies

When you explicitly supply `--force`, DAWVC does **not** simply overwrite your files. Instead, it performs the following atomic protocol:

1. **Pre-Flight Inspection**: Inspects all files currently in the workspace that differ from the target snapshot.
2. **Deterministic Recovery Archive**: Copies the dirty files into an immutable recovery directory:
   ```text
   .dawvc/recovery/recovery_YYYYMMDD_HHmmss_<guid>/
   ```
3. **Atomic Candidate Staging**: Prepares the incoming files in a temporary staging folder (`.dawvc/staging/`) on the same volume.
4. **Candidate Verification**: Verifies the BLAKE3 hashes of all candidate files.
5. **Atomic Swap**: Replaces the workspace files atomically.

---

## 4. Restoring From a Recovery Backup

If you used `--force` and later realize you needed an audio recording or project variation that was in the uncommitted workspace:

### Step 1: Locate Recovery Directories
Open PowerShell and list the recovery snapshots:

```powershell
Get-ChildItem -Path .dawvc\recovery
```

**Output:**
```text
Mode     LastWriteTime       Length Name
----     -------------       ------ ----
d----    2026-09-14 20:15           recovery_20260914_201530_a9f1b2c3
```

### Step 2: Inspect Recovered Files
The recovery directory preserves the exact files and folder hierarchy as they existed before the forced checkout:

```powershell
Get-ChildItem -Path .dawvc\recovery\recovery_20260914_201530_a9f1b2c3 -Recurse
```

### Step 3: Copy Needed Files Back
Simply copy the desired `.flp` or `.wav` back to your project directory, or open it directly in your DAW to export what you need.

---

## 5. Repository Corruption Recovery (`dawvc fsck`)

After a system crash or power outage, inspect `dawvc recover` first and follow the
durable workspace procedure below. Repository graph verification is a separate
check and does not replace recovery:

```powershell
dawvc fsck --artifacts
```

- **Health domain**: Checks internal BLAKE3 hashes of all stored commit objects, snapshots, envelopes, and bundled blobs.
- **Atomic guarantee**: DAWVC uses atomic file renaming on Windows NTFS. Incomplete object writes remain orphaned in temporary files and never corrupt existing reachable commits or branch pointers.

## Interrupted operations and incomplete rollback

Await cancelled operations until compensation has finished. Cancellation does not
guarantee an unchanged repository: commit, fetch, pull and clone can leave durable
effects. Failed forced recovery-copy creation can leave an incomplete copy.

`IncompleteRollbackException` distinguishes failed compensation from cancellation.
Preserve its `RecoveryPath` before further mutations. A journal directory contains
original files that could not be restored and survives disposal; a repository-root
path identifies failed ref/index compensation rather than a complete metadata backup.
Inspect nested technical causes locally for other retained paths. Preserve the
workspace and `.dawvc`, then inspect refs and run `doctor`/`fsck` as appropriate.
Redact machine paths and exception details before sharing diagnostics.

## Corrupt staging index

SQLite initialization errors CORRUPT/NOTADB now throw
`StagingIndexCorruptException` instead of deleting/rebuilding `index.db`. This
intentionally changes CLI failure behavior: staged user intent must be preserved.
Stop operations and preserve `.dawvc/index.db` and any `index.db-wal`/`index.db-shm`
files before recovery. Use an offline copy for SQLite recovery tooling.
Before raw-file inspection or offline copying, await active operations and dispose
their repository contexts/indexes. Even a failed initialization can retain an idle
pooled native connection until its owner is disposed; Windows can otherwise deny
access to `index.db`. Do not bypass this lifetime requirement with retries or
process-wide connection-pool clearing.
`doctor`/`fsck` do not reconstruct staged intent. There is no new automatic reset
command. See [ADR-009](adr/ADR-009-sqlite-local-index.md).

## Failed or interrupted Push

Push does not alter worktrees. Before final atomic ref publication, transfer,
verification or cancellation failures leave the destination branch unchanged.
Verified objects promoted before a later failure may remain unreachable. Once
publication succeeds, cancellation cannot undo it. Inspect the advertised branch
(or fetch) to determine whether the new tip was published after process loss.

On handled termination, disposal attempts to remove its `incoming/push-*` session.
Power loss, access restrictions or cleanup failures can leave session directories.
Preserve evidence first; only remove an identified abandoned session when no writer
is using it. Never remove `ref-write.lock` to break a live writer's lock: it is a
persistent file whose open handle supplies synchronization, and is harmless when
closed. No automatic garbage collection or destructive index reset is introduced.

## SSH disconnection or lost acknowledgement

On handled EOF, protocol error or host deadline, the transient receiver disposes
its push session and releases the ref lock. Killing the host can leave abandoned
quarantine even though OS locks are released. Cancellation stops the client's SSH
process; the host handles the disconnected channel or session deadline.

A broken connection after atomic publication can lose the success reply. Fetch
and compare the remote tip before retrying; do not assume an error means the
branch was unchanged. Strict host-key failure requires independent verification
of the host identity. Never disable verification or delete known_hosts entries
merely to bypass an unexpected key change. Preserve evidence and fix the host
policy/key permissions locally for denied access; protocol errors omit raw paths.


## Interrupted bare provisioning

Bare init/clone prepares in `.dawvc-provision-<id>` under the destination parent.
It publishes the complete bare layout by directory rename and never recursively
replaces an existing destination. Before publication, errors/cancellation leave
no partial repository at the target. An empty target placeholder can disappear
if the final rename fails after removing it; existing user data is not overwritten.

After a hard crash or cleanup permission failure, staging can remain. First await
or stop the owning process and check that nobody is provisioning that target.
Inspect the destination using its dawvc.yaml, HEAD, objects/ and refs/heads/;
absence of .dawvc is expected for bare storage. Use normal Clone to obtain and
check a working copy; do not point workspace commands at a bare root. Remove
abandoned staging only after confirming ownership/inactivity, then retry into an
absent/empty destination. Do not merge staging directories into live storage.
An interrupted SSH connection/source read does not provision a target on the SSH
host; configuration and destination creation happen on the initiating machine.


## Explicit snapshot Merge recovery

Merge has no persistent conflicted state: there is no `merge --abort`/`--continue`.
It requires a committed workspace and chooses one complete pre-existing snapshot.
Before publication, dirty/collision/integrity/state-change failures do not
intentionally move the active branch; an immutable unreachable merge object can
remain. Checkout's existing journal compensates handled publication failures.
If compensation fails, preserve recovery data reported by IncompleteRollbackException.

After cancellation, a hard process stop or an error during post-publication
binding resolution, inspect `dawvc log` and `dawvc status` before retrying. A merge
may already be committed. Never delete object storage to undo it. Both parent
IDs are printed by log; inspect/restore either old project in a separate directory:
`dawvc checkout <full-parent-id> --restore-to <empty-directory>`. Restore outputs
are ordinary files for review; the active branch is unaffected. Do not use Git's
`HEAD^` syntax: it is not implemented. Keep source refs or note full IDs when
reviewing alternate versions, and commit subsequent manual edits normally.


## Durable workspace recovery (WP-SYNC-07)

In-place Checkout/Switch and the fast-forward publication used by Pull, explicit
Merge and initial Clone checkout now save persistent recovery data. After a killed
process, stop/await all operations and do not save/edit the same project in a DAW.
Inspect first:

```powershell
dawvc recover --dir D:\Music\Song
# Only after inspection and after the owning operation has stopped:
dawvc recover --rollback --dir D:\Music\Song
```

Inspection reports transaction ID, Prepared/Committed state and saved file/staging
counts without opening the index. Prepared restores original file bytes, HEAD/
affected branch and staged intent, including dirty pre-publication files protected
by force checkout. Committed verifies the published files/ref/empty index and
cleans recovery metadata; it never silently reverts the completed version. Neither
mode fetches, writes FLP internals or offers force/discard. Exit 10 means pending,
conflicting or unreadable recovery state and requires inspection. A live ownership
lock refuses recovery: await the operation; do not delete its lock files.

Backups and the manifest are under `.dawvc/transactions/<id>`. Keep them private:
backups contain actual project/audio bytes, and manifests contain relative names
and staged object IDs. They are not shared objects or diagnostic upload material.
Unknown versions, foreign repository IDs, invalid paths, bad backups, unexpected
file/ref/index edits and multiple active envelopes are refused before recovery
writes. Preserve all evidence. A later edit detected during recovery can stop it
after some other files were restored; the envelope remains for retry. No lock
against arbitrary concurrent filesystem/DAW writers is claimed.

If recovery conflicts, first copy your unexpected new work to a separate safe
location. Inspect the manifest/backups and known parent snapshots. External
`checkout <full-id> --restore-to <separate-directory>` remains available for review;
while pending it must be outside the source workspace and its ancestry, and cannot
write into another pending workspace. Bring only deliberately reviewed affected
paths/refs back to an expected state before retrying. Do not delete a transaction,
edit its hashes/state, erase objects or run force-like repairs to bypass checks.
For unknown metadata, missing backups or changed refs, manual expert inspection is
required; automatic recovery cannot safely guess your intended version.

A stopped recovery can be run again. Files restore via flushed temporary copies
and atomic rename; refs accept only expected old/new tips; a partial original
staging subset is recognized and rebuilt. A corrupt SQLite index is not erased or
recreated to make recovery pass. Existing handled journal compensation failures
still surface IncompleteRollbackException and retain recovery data.

Terminal envelopes first move to `.dawvc/transaction-cleanup/<id>`, then deletion
is best-effort. Crashes/permissions can leave terminal data there. Unpublished
preparation can remain under `.dawvc/transaction-preparation/<id>` without user
file changes. Legacy `.journal_backup_*`, `.staging_checkout_*` and interrupted
private incomplete restore copies may remain. Only after confirming ownership/inactivity and
successful state inspection may an operator archive/remove identified leftovers.
Do not feed terminal/preparation directories back into active recovery or merge
copies into live storage. No secure erasure or automatic GC is provided.

Recovery preserves contents and logical staged entries, not filesystem/SQL bytes,
cache hints, timestamps, permissions or ACLs. A created Switch branch can remain
following a crash; both histories are retained. A rolled-back initial Clone can
remain initialized with fetched objects/tracking settings: use normal Pull to
finish materializing it after inspection. No atomic whole-Clone/Commit/Push/bare
provisioning transaction is introduced. Post-commit dependency/binding errors may
still leave a completed publication; inspect log/status before retrying.

Actual process-kill tests are separate from physical power-cycle evidence.
Flush-to-disk, atomic rename and SQLite synchronous=FULL do not certify directory
ordering, hardware/controller caches or NAS/SMB behavior. Real power-loss and
storage qualification remain open deployment gates. Operation-wide serialization
of all local writers is the next independent implementation scope.


## Busy repository (WP-SYNC-08)

A cooperating local mutation, cache-writing Status or binding-writing Doctor owns
`.dawvc/operation.lock` for its full operation. A second writer returns exit 11
immediately; an incoming filesystem/SSH push to a busy receiver returns remote
exit 8. Await/cancel the owning operation and await its completion before retrying.
The lock covers network waits; a stalled remote can keep the local repository busy.
No automatic waiting, forced takeover, PID expiry or lock-file removal is offered.

The order is operation → workspace publication/recovery → ref writer. Explicit
`recover --rollback` participates in the operation lock; read-only `recover`
inspection does not. After a killed process the OS releases its handles: inspect
pending recovery first, then roll back/finalize it. Busy alone does not imply a
recovery envelope exists. Empty operation/creation/ref/recovery files persist and
are not evidence of a live process. Never delete them to bypass a live owner.

Init/Clone/bare provisioning coordinate through a persistent hashed sibling
`.dawvc-create-<sha256>.lock`. They need parent write permission. Failed normal
creation can leave an empty `.dawvc/operation.lock` scaffold; Clone accepts that
scaffold, but does not silently replace other existing destination data.

Only upgraded stock use cases/receivers participate. Older binaries, direct backend
calls and DAW/editor writes must still be kept exclusive. External restoration
requires an exclusive separate target and must not write into an active working
repository. Read-only views can observe changes and are not multi-file snapshots.
NAS/SMB lock semantics and real power-loss durability still require field testing.
