# DAWVC CLI Command Reference

**Executable:** `dawvc.exe`  
**Version:** `0.1.0-preview.1`  
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

### 2. `dawvc scan`
Scans the primary project file and workspace for referenced audio assets and plugins.

```powershell
dawvc scan [--dir <path>] [--json]
```

| Option | Type | Description |
|---|---|---|
| `--dir` | Path | Target repository directory (default: current repository). |
| `--json` | Flag | Emits scan results as canonical JSON. |

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

### 4. `dawvc add`
Stages discovered external assets or sample files for bundling into repository storage.

```powershell
dawvc add <path> [--dir <path>]
```

| Argument/Option | Type | Description |
|---|---|---|
| `<path>` | Argument | File or directory path to stage (relative or absolute). |
| `--dir` | Path | Target repository directory. |

---

### 5. `dawvc commit`
Records an immutable project snapshot into repository history.

```powershell
dawvc commit -m <message> [--allow-incomplete] [--dir <path>]
```

| Option | Type | Description |
|---|---|---|
| `-m`, `--message` | String | **Required.** Descriptive commit message. |
| `--allow-incomplete` | Flag | Permits committing even if required bundle dependencies are missing locally (exit code 5 override). |
| `--dir` | Path | Target repository directory. |

---

### 6. `dawvc log`
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

### 7. `dawvc branch`
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

### 8. `dawvc switch`
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

### 9. `dawvc checkout`
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

### 10. `dawvc bind`
Binds an external file path to a logical asset identity or updates local path bindings.

```powershell
dawvc bind <asset-id> <local-path> [--dir <path>]
```

---

### 11. `dawvc doctor`
Inspects workspace and host system health, checking for missing audio assets or unregistered plugins.

```powershell
dawvc doctor [--json] [--dir <path>]
```

| Option | Type | Description |
|---|---|---|
| `--json` | Flag | Emits doctor diagnostic report as structured JSON. |
| `--dir` | Path | Target repository directory. |

*Exits with code `6` if missing dependencies or unregistered plugins are detected.*

---

### 12. `dawvc fsck`
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
