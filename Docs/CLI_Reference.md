# DAWVC CLI Command Reference

**Executable:** `dawvc.exe`  
**Version:** `0.1.0-preview.3`  
**Syntax:** `dawvc <command> [options] [arguments]`

---

## Standard Exit Codes

DAWVC uses deterministic process exit codes across all commands:

| Exit Code | Name | Meaning |
|:---:|---|---|
| `0` | `Success` | Command completed successfully with no errors or blocking warnings. |
| `1` | `GeneralError` | Unhandled error, invalid user input, or missing required parameter. |
| `2` | `RepositoryNotFound` | Command run outside a DAWVC repository (no `.dawvc/` found in directory tree). |
| `3` | `CorruptRepository` | Internal repository integrity failure detected during validation. |
| `4` | `DirtyWorkingTree` | Action prevented because workspace contains unsaved changes to tracked assets. |
| `5` | `MissingDependencies` | Incomplete commit blocked due to missing required `Bundle` assets. |
| `6` | `DiagnosticFailure` | `doctor` or `fsck` found one or more unresolved warnings or errors. |
| `7` | `InvalidArguments` | CLI syntax error or mutually incompatible flags passed. |
| `8` | `RemoteError` | Remote transport failure, unreachable share/directory, repository ID mismatch, or schema error. |
| `9` | `NonFastForward` | Push or pull rejected because the requested update is not a fast-forward (including a remote-ahead Push). |
| `130` | `Cancelled` | Push was cooperatively cancelled; inspect the destination ref after an interrupted process. |

---

## Commands

### 1. `dawvc init`
Initializes a new DAWVC repository in the current or specified directory.

```powershell
dawvc init [--dir <path>] [--name <string>] [--primary <path>]
```

| Option | Type | Description |
|---|---|---|
| `--dir` | Path | Target directory to initialize (default: current working directory). |
| `--name` | String | Custom logical project name (default: directory name). |
| `--primary` | Path | Explicit relative path to primary DAW project file (e.g. `Project.flp`). |

---

### 2. `dawvc clone`
Clones a remote repository (directory or UNC share) into a new local directory, checks out the default branch, and materializes all project files and bundled sample assets.

```powershell
dawvc clone <url> [<dir>]
```

| Argument/Option | Type | Description |
|---|---|---|
| `<url>` | Argument | **Required.** Remote repository path (filesystem path or `\\server\share\path`). |
| `<dir>` | Argument | Target directory for the new repository (defaults to project name). |

#### Post-Clone Verification:
`dawvc clone` automatically configures `origin` pointing to `<url>`, checks out the default branch (`main`), materializes all bundled audio samples into `./Samples/`, and runs a silent `doctor` check, reporting immediate reproducibility readiness.

---

### 3. `dawvc status`
Displays the current branch, tracked primary file status, and uncommitted or modified dependencies.

```powershell
dawvc status [--dir <path>] [--json]
```

| Option | Type | Description |
|---|---|---|
| `--dir` | Path | Target repository directory. |
| `--json` | Flag | Output machine-readable JSON status payload. |

---

### 4. `dawvc scan`
Scans the primary project file and workspace for referenced audio assets and plugins.

```powershell
dawvc scan [--dir <path>] [--json]
```

| Option | Type | Description |
|---|---|---|
| `--dir` | Path | Target repository directory (default: current repository). |
| `--json` | Flag | Emits scan results as canonical JSON. |

---

### 5. `dawvc add`
Stages discovered external assets or sample files for bundling into repository storage.

```powershell
dawvc add <path> [--dir <path>]
```

| Argument/Option | Type | Description |
|---|---|---|
| `<path>` | Argument | File or directory path to stage (relative or absolute). |
| `--dir` | Path | Target repository directory. |

---

### 6. `dawvc commit`
Records an immutable project snapshot into repository history.

```powershell
dawvc commit -m <message> [--allow-incomplete] [--consolidate] [--dir <path>]
```

| Option | Type | Description |
|---|---|---|
| `-m`, `--message` | String | **Required.** Descriptive commit message. |
| `--allow-incomplete` | Flag | Permits committing even if required bundle dependencies are missing locally (exit code 5 override). |
| `--consolidate` | Flag | Automatically copies bundled external audio samples into the local repository `./Samples/` folder. |
| `--dir` | Path | Target repository directory. |

#### Auto-Bundling & Portability
When committing, DAWVC automatically analyzes project dependencies:
- **Audio Assets (`.wav`, `.mp3`, `.flac`, etc.)**: Automatically classified as `Bundle`. External samples located on central drives or DAW search folders are read and stored as content-addressed blobs in `.dawvc/objects/`, mapped to snapshot relative paths under `Samples/<filename>`.
- **Zero-Config Checkout**: When a collaborator clones or checks out the commit, DAWVC materializes all bundled samples directly into their workspace `./Samples/` folder without any missing file dialogues.
- **Consolidation**: By default, DAWVC leaves the author's local file references unchanged on disk. Passing `--consolidate` physically copies external samples into the author's local `./Samples/` folder as well.
- **Third-Party Instrument Libraries**: Multi-gigabyte proprietary libraries (e.g. Kontakt, Omnisphere) remain `ReferenceOnly` or `UserChoice` and are never bundled into the repository.

---

### 7. `dawvc log`
Displays commit history starting from HEAD.

```powershell
dawvc log [-n <limit>] [--json] [--dir <path>]
```

| Option | Type | Description |
|---|---|---|
| `-n`, `--limit` | Integer | Maximum number of commit entries to display (default: all). |
| `--json` | Flag | Output commit log as JSON array. |
| `--dir` | Path | Target repository directory. |

---

### 8. `dawvc branch`
Lists existing branches or creates a new branch pointer at current HEAD.

```powershell
# List branches:
dawvc branch [--dir <path>]

# Create branch:
dawvc branch <branch-name> [--dir <path>]
```

| Argument/Option | Type | Description |
|---|---|---|
| `<branch-name>` | Argument | Optional. Name of new branch to create. |
| `--dir` | Path | Target repository directory. |

---

### 9. `dawvc switch`
Safely switches workspace to a target branch.

```powershell
dawvc switch <branch-name> [--force] [--dir <path>]
```

| Option | Type | Description |
|---|---|---|
| `<branch-name>` | Argument | **Required.** Name of branch to switch to. |
| `--force` | Flag | Overrides dirty tree guard by creating a recovery backup in `.dawvc/recovery/` before installing files. |
| `--dir` | Path | Target repository directory. |

---

### 10. `dawvc checkout`
Restores a specific commit snapshot or exports it to an external folder.

```powershell
dawvc checkout <commit-ref> [--restore-to <dir>] [--force] [--dir <path>]
```

| Option | Type | Description |
|---|---|---|
| `<commit-ref>` | Argument | **Required.** Commit hash (full or short) or branch name. |
| `--restore-to` | Path | Exports candidate files cleanly to target directory without modifying current workspace. |
| `--force` | Flag | Overrides uncommitted modifications in current workspace with automated recovery copy. |
| `--dir` | Path | Target repository directory. |

---

### 11. `dawvc remote`
Manages configured remote repositories (local folders, external drives, or UNC network shares).

```powershell
# List remotes:
dawvc remote list [--dir <path>]

# Add remote:
dawvc remote add <name> <url> [--dir <path>]

# Remove remote:
dawvc remote rm <name> [--dir <path>]
```

| Subcommand/Option | Description |
|---|---|
| `list` | Lists all configured remotes with their URLs. |
| `add <name> <url>` | Registers a new remote repository location (e.g. `origin` `\\NAS\Projects\Song`). |
| `rm <name>` | Removes an existing remote configuration. |
| `--dir` | Path to target repository directory. |

---

### 12. `dawvc fetch`
Downloads commits, snapshots, and missing blob dependencies from a remote repository into the local quarantine (`.dawvc/incoming/`), verifies envelope integrity and BLAKE3 hashes, promotes them to `.dawvc/objects/`, and updates remote-tracking references (`.dawvc/refs/remotes/<remote>/<branch>`). Never touches the workspace or HEAD.

```powershell
dawvc fetch [<remote>] [<branch>] [--dry-run] [--dir <path>]
```

| Argument/Option | Type | Description |
|---|---|---|
| `<remote>` | Argument | Name of remote to fetch from (default: `origin`). |
| `<branch>` | Argument | Optional remote branch to fetch (default: all tracking branches). |
| `--dry-run` | Flag | Simulates the fetch operation and displays incoming commits and transfer sizes without writing. |
| `--dir` | Path | Target repository directory. |

*Exits with code `8` (`RemoteError`) if remote handshake fails or repository IDs do not match.*

---

### 13. `dawvc pull`
Fetches changes from the remote repository and safely integrates them into the current branch using fast-forward materialization.

```powershell
dawvc pull [<remote>] [<branch>] [--force] [--ff-only] [--dry-run] [--dir <path>]
```

| Argument/Option | Type | Description |
|---|---|---|
| `<remote>` | Argument | Remote repository name (default: `origin`). |
| `<branch>` | Argument | Remote branch to pull (default: upstream tracking branch or current branch name). |
| `--force` | Flag | Bypasses dirty workspace check by creating a timestamped recovery copy in `.dawvc/recovery/` before updating files. |
| `--ff-only` | Flag | Explicitly enforces that only fast-forward updates are permitted (default behavior in v0.2). |
| `--dry-run` | Flag | Previews pull actions without modifying workspace or advancing branch pointers. |
| `--dir` | Path | Target repository directory. |

#### Behavior & Safety Guarantees:
- **Fast-Forward Only**: In v0.2, if local history and remote history have diverged, the pull is safely rejected with exit code `9` (`NonFastForward`) without touching any files.
- **Dirty Workspace Guard**: If uncommitted edits exist in tracked assets, pull is rejected with exit code `4` (`DirtyWorkingTree`). Use `--force` to create an automatic recovery snapshot and proceed.
- **Zero Binary Mutations**: Audio files and `.flp` project files are restored cleanly and byte-identically. Bundled samples are materialized to `./Samples/` with automatic path deduplication.

---

### 14. `dawvc doctor`
Inspects workspace and host system health, verifying that all referenced audio assets exist and that required plugins are installed with compatible versions.

```powershell
dawvc doctor [--json] [--dir <path>]
```

| Option | Type | Description |
|---|---|---|
| `--json` | Flag | Emits doctor diagnostic report as structured JSON. |
| `--dir` | Path | Target repository directory. |

#### Diagnostic Checks Performed:
1. **Asset Dependencies**: Checks that all tracked bundled samples, project recordings, and external sound libraries are present on disk.
2. **Plugin Dependencies & Version Compatibility**:
   - Checks that required VST3/native plugins are installed in standard host directories (e.g. `C:\Program Files\Common Files\VST3`).
   - **Version Compatibility Rule (`installed >= required`)**: Audio plugins must be backward compatible with older projects. If the plugin installed on the current machine is an older version than recorded in the project snapshot, `dawvc doctor` flags a `Mismatch` status and recommends updating the plugin.
   - If the plugin version cannot be extracted or no requirement was recorded, presence verification succeeds.
3. **Primary Project File**: Validates that the active DAW project file (e.g. `.flp`) exists and is accessible.

#### Terminal Output Columns:
- **Dependency**: Name or logical ID of the asset / plugin.
- **Type**: `Plugin` or `Asset`.
- **Required**: Project required version (or `—` for assets / unversioned plugins).
- **Installed**: Currently detected version on this machine.
- **Status**: `Verified` (green), `Missing` (red), or `Mismatch` (red/yellow).
- **Details**: Local filesystem path or diagnostic notes.

#### Structured JSON Schema:
When run with `--json`, each item in the `dependencies` array includes:
```json
{
  "name": "Serum",
  "kind": "Plugin",
  "status": "Mismatch",
  "isHealthy": false,
  "expectedVersion": "1.3.6",
  "detectedVersion": "1.2.0",
  "resolvedPath": "C:\\Program Files\\Common Files\\VST3\\Serum.vst3",
  "notes": "Installed version 1.2.0 is older than project version 1.3.6. Plugins must be the same or higher version."
}
```

*Exits with code `0` if all dependencies are verified and healthy. Exits with code `6` (`DiagnosticFailure`) if any dependencies are missing, unregistered, or version-incompatible.*

---

### 15. `dawvc bind`
Binds an external file path to a logical asset identity or updates local path bindings.

```powershell
dawvc bind <asset-id> <local-path> [--dir <path>]
```

---

### 16. `dawvc fsck`
Validates repository database, commit DAG, envelope formats, and BLAKE3 object hashes.

```powershell
dawvc fsck [--artifacts] [--json] [--dir <path>]
```

| Option | Type | Description |
|---|---|---|
| `--artifacts` | Flag | Deep-verifies SHA-256 / BLAKE3 hashes for all physical files in the workspace. |
| `--json` | Flag | Emits fsck verification report as JSON. |
| `--dir` | Path | Target repository directory. |

*Exits with code `6` if repository or artifact corruption is detected.*


## `dawvc push` — filesystem collaboration extension

```powershell
dawvc push [<remote>] [<branch>] [--dry-run] [--dir <path>]
```

Pushes a committed local branch to an initialized remote with the same repository
ID. Supports local paths, external drives, `file://` URLs, UNC shares and `dawvc://` over SSH. Without
arguments it uses the active branch's upstream remote/branch, falling back to
`origin` and the active branch name. An explicit remote uses the active branch
name; an explicit branch selects that local branch and the same destination name.
No refspec syntax, force-push or branch deletion is supported.

`--dry-run` verifies the complete closure and reports missing object count/payload
bytes without writing the destination or local tracking refs. Ordinary push sends
missing objects through verified destination quarantine and atomically creates or
fast-forwards the branch. Existing objects are verified and never overwritten.
A checked-out non-bare destination branch is refused; select an inactive branch
or use an existing bare repository. Push never updates either workspace.

Fetch and integrate changes before retrying a rejected non-fast-forward push.
Push does not automatically set upstream or update local remote-tracking refs;
run `fetch` when you want refreshed tracking information. Dirty local work is not
included: only the selected committed history is sent.

Exit codes: 0 success/preview; 3 corrupt or unsafe objects; 7 invalid push arguments;
8 remote rejection, unavailable/unsupported transport or concurrent writer;
9 non-fast-forward; 130 cooperative cancellation. Parser syntax failures use the
parser's nonzero code. Cancelled/failed operations can leave unreachable verified
objects; inspect the destination ref before retrying after an interrupted process.
See [Recovery Guide](Recovery_Guide.md) for cleanup boundaries.

## SSH remotes and `dawvc serve`

All four sync operations use the selected transport:

```powershell
dawvc remote add origin dawvc://alice@host/projects/song
dawvc push --dry-run
dawvc push
dawvc clone dawvc://alice@host/projects/song MySong
dawvc fetch
dawvc pull
```

The scheme is `dawvc://` (one colon), always SSH; port defaults to 22 and may be
explicit (`host:2222`). The URL path is an allowlisted logical repository alias.
Passwords, queries, fragments, URI-escaped aliases and traversal are refused.
OpenSSH uses strict known_hosts/public-key authentication; preconfigure verified
host keys and an unlocked agent/IdentityFile. Login failures are not interactive.

`dawvc serve [--config <absolute-host-policy-path>]` is an operator-facing stdio
receiver started by SSH. Its default policy is `~/.config/dawvc/serve.json`. It
produces binary protocol on stdout and is not an interactive CLI session or
network listener. The remote client always requests the constant `dawvc serve`
command; SSH forced-command policy can select the host config. Missing/invalid
host policy exits 8 with a safe stderr message. The policy path is never accepted
from protocol requests. `remote add` validates configured transport addresses
before storing them, without initiating an SSH connection.

See [SSH hosting](Self_Hosting_SSH.md) for setup/permissions and
[Remote Protocol](Remote_Protocol.md) for streaming, deadlines and error mapping.
