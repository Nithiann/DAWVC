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

If a system crash or power outage occurs during a disk write, verify the integrity of the repository:

```powershell
dawvc fsck --artifacts
```

- **Health domain**: Checks internal BLAKE3 hashes of all stored commit objects, snapshots, envelopes, and bundled blobs.
- **Atomic guarantee**: DAWVC uses atomic file renaming on Windows NTFS. Incomplete object writes remain orphaned in temporary files and never corrupt existing reachable commits or branch pointers.
