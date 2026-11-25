# DAWVC — DAW Version Control & Dependency Management

[![CI](https://github.com/Nithiann/DAWVC/actions/workflows/ci.yml/badge.svg)](https://github.com/Nithiann/DAWVC/actions/workflows/ci.yml)
[![Release](https://img.shields.io/badge/Release-v0.2.0--preview.1-green.svg)](https://github.com/Nithiann/DAWVC/releases)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)
[![.NET](https://img.shields.io/badge/.NET-10.0-purple.svg)](https://dotnet.microsoft.com/)
[![Platform](https://img.shields.io/badge/Platform-Windows%2011%20x64-blue.svg)]()

> **DAW-agnostic version control, asset deduplication, and dependency portability for music production.**

---

## 1. What is DAWVC?

**DAWVC** (Digital Audio Workstation Version Control) is a dedicated version control and dependency management system tailored for music producers and audio engineers.

Traditional version control systems like Git store files, but lack domain awareness for audio and DAW projects:

- They do not know which samples, recordings, wavetables, or third-party plugins a project relies upon.
- They depend on local, absolute file paths that break across different workstations (`D:\Samples\Kick.wav` vs. `C:\Audio\Kick.wav`).
- They provide no insight into plugin versions, environment portability, or system reproducibility.

DAWVC solves this by semantically binding project files, audio assets, and plugin requirements to **content identity** rather than fragile local file paths.

```text
┌─────────────────────────────────────────────────────────────┐
│                       Core Principle:                       │
│        "A dependency is identified by its content           │
│         or logical identity, NEVER by its local             │
│         filesystem path."                                   │
└─────────────────────────────────────────────────────────────┘
```

> [!IMPORTANT]
> **Important Disclaimers (`NFR-REL-007`, `NFR-REL-008`):**
>
> 1. **Proprietary Plugins & Libraries**: DAWVC does **not** bundle commercial plugin binaries (`.dll`, `.vst3`), license keys, or commercial sample packs. It records plugin metadata and verifies their presence via `dawvc doctor`.
> 2. **Native Project Bytes Are Sacred**: DAWVC **never** modifies or rewrites native `.flp` files. When collaborating across machines with different folder layouts, configure FL Studio's *Extra Search Folders* to point to your DAWVC project or sample root. See [FL Studio Search Paths & Relinking Guide](Docs/FLStudio_Search_Paths_Relinking.md).

---

## 2. Core Concepts & Philosophy

1. **DAW-Agnostic Core**:
   The core engine (`Domain`, `Application`, `Infrastructure`) has zero knowledge of specific DAW formats. All DAW-specific inspection (such as FL Studio `.flp` parsing) lives inside isolated, interchangeable adapters.
2. **Native Bytes are Sacred**:
   Native project files are never mutated in-place or written partially. Source bytes remain the primary source of truth. Inspection metadata is derived, rebuildable, and non-destructive.
3. **Content-Addressed Storage (BLAKE3)**:
   All project assets and blobs are addressed via fast, cryptographically secure BLAKE3 hashes. Identical audio files across projects, branches, or folders are automatically deduplicated.
4. **Strict Separation of Shared vs. Local State**:
   - *Shared (versioned)*: Commits, snapshots, dependency graphs, project artifacts, bundled assets, plugin requirements, and portability policies.
   - *Local (non-versioned)*: Absolute file paths, machine-specific plugin installations, local library directories, caches, and credentials.
5. **Safe Checkout & Integrity-First**:
   A checkout never silently overwrites local modifications. Forced checkouts (`--force`) always create a deterministic recovery copy first. Candidate workspace installs are always staged and atomic.

---

## 3. Quick Start (5-Minute Walkthrough)

Detailed tutorial: **[Quick Start Guide](Docs/QuickStart.md)** | Setup instructions: **[Installation Guide](Docs/Installation.md)**

```powershell
# 1. Initialize repository inside an FL Studio project folder
cd C:\Music\My_Track
dawvc init

# 2. Inspect project dependencies and referenced samples
dawvc scan

# 3. Check workspace state
dawvc status

# 4. Stage external samples for bundling into version control
dawvc add samples\vocals.wav

# 5. Record your first immutable snapshot
dawvc commit -m "feat: initial arrangement and vocal stems"

# 6. Check environment health on another workstation
dawvc doctor
```

---

## 4. MVP v0.1 Scope & Feature Set

The initial release (**MVP v0.1**) focuses on a rock-solid, local workflow for **FL Studio** on **Windows 11 x64**:

- [x] **Repository Management**: Local repositories per musical project (`dawvc init`, `dawvc.yaml`).
- [x] **Hybrid Staging**: Automatic tracking for the primary project file and tracked assets; explicit staging for newly discovered dependencies (`dawvc add`).
- [x] **Immutable History**: Commits, snapshot creation, log visualization (`dawvc commit`, `dawvc log`).
- [x] **Branching**: Creating, switching, and inspecting local branches (`dawvc branch`, `dawvc switch`).
- [x] **Read-Only FL Studio Adapter**: Non-destructive inspection and validation of `.flp` projects (FL Studio 2026.x baseline) with safe *opaque fallback* for future versions.
- [x] **Safe Checkout**: Staged verification and atomic restore into active workspaces or clean target folders (`dawvc checkout --restore-to <dir>`).
- [x] **Environment Diagnostics**: Project and system environment validation (`dawvc doctor`).
- [x] **Repository Integrity**: Object graph, envelope, and hash verification (`dawvc fsck`).

---

## 5. CLI Overview

For options, exit codes, and JSON schemas, see the complete **[CLI Command Reference](Docs/CLI_Reference.md)**.

| Command | Description |
| --- | --- |
| `dawvc init` | Initializes a new DAWVC repository in the current or target directory. |
| `dawvc scan` | Inspects the project for referenced samples, recordings, and plugins. |
| `dawvc status` | Shows workspace state, modified tracked assets, and uncommitted discoveries. |
| `dawvc add <path>` | Adds an external asset or sample to tracked repository dependencies. |
| `dawvc commit -m <msg>` | Records a new immutable project snapshot into repository history. |
| `dawvc log` | Displays commit history with snapshot metadata and author info. |
| `dawvc branch [name]` | Lists existing branches or creates a new branch pointer. |
| `dawvc switch <branch>` | Safely switches the workspace to a different branch. |
| `dawvc checkout <commit>` | Restores a specific snapshot (supports `--restore-to` and `--force`). |
| `dawvc remote track <remote> <branch>` | Sets the active local branch’s upstream without contacting the host. |
| `dawvc push [remote] [branch]` | Sends committed history to a filesystem/UNC or dawvc:// SSH remote, fast-forward only; supports `--dry-run`. |
| `dawvc bind <id> <path>` | Links an external asset to a logical dependency ID. |
| `dawvc doctor` | Verifies environment health, checking local presence and compatible versions (`installed >= required`) for plugins, samples, and dependencies. |
| `dawvc fsck` | Validates internal integrity across the object store and refs. |

---

## 6. Architecture & Solution Structure

The solution adheres to Clean Architecture principles with strictly enforced dependency boundaries:

```text
                  ┌──────────────────────┐
                  │      DawVcs.Cli      │ (Presentation / Host)
                  └──────────┬───────────┘
                             │
                             ▼
                  ┌──────────────────────┐
                  │  DawVcs.Application  │ (Use Cases, Ports, Orchestration)
                  └──────┬────────┬──────┘
                         │        │
           ┌─────────────┘        └─────────────┐
           ▼                                    ▼
┌──────────────────────┐             ┌─────────────────────────────┐
│    DawVcs.Domain     │             │ DawVcs.Adapters.Abstractions│
└──────────────────────┘             └──────────────┬──────────────┘
           ▲                                        ▲
           │                                        │
┌──────────┴───────────┐             ┌──────────────┴──────────────┐
│ DawVcs.Infrastructure│             │   DawVcs.Adapters.FLStudio  │
└──────────────────────┘             └─────────────────────────────┘
```

---

## 7. Development, Building & Packaging

### Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) (version `10.0.301` or later)
- Windows 11 x64 (recommended for FL Studio adapter validation) or Linux/macOS for core domain development.
- Git & PowerShell

### Build

```powershell
# Restore and build all projects in Release configuration (TreatWarningsAsErrors enabled)
dotnet build -c Release
```

### Test

```powershell
# Run unit, architecture, infrastructure, adapter, performance, and smoke tests
dotnet test -c Release --logger "console;verbosity=normal"
```

### Package Distribution

```powershell
# Produces self-contained win-x64 ZIP and SHA256SUMS.txt
pwsh -File ./scripts/package.ps1
```

---

## 8. Documentation & Specifications

### Guides & References

- **[Installation & Setup Guide](Docs/Installation.md)** — Self-contained setup, verification, and uninstall.
- **[Quick Start Guide](Docs/QuickStart.md)** — Step-by-step tutorial on project versioning.
- **[CLI Command Reference](Docs/CLI_Reference.md)** — Full command arguments, flags, and exit code reference.
- **[FL Studio Search Paths & Relinking](Docs/FLStudio_Search_Paths_Relinking.md)** — Resolving audio assets across systems without modifying project files.
- **[Disaster Recovery & Workspace Safety](Docs/Recovery_Guide.md)** — Uncommitted change guards, forced checkout backups, and `fsck`.
- **[Known Limitations & Scope](Docs/Known_Limitations.md)** — v0.1 boundaries, plugin bundling policy, and privacy redactions.

### Architecture & Security

- **[Technical Design](Docs/Technical%20Design.md)** — Normative architecture v1.1, BLAKE3 models, and envelopes.
- **[MVP Requirements Specification](Docs/MVP%20Requirements.md)** — Normative requirements and acceptance criteria.
- **[Implementation Plan](Docs/Implementation%20plan.md)** — Work packages (WP-00 through WP-10) and release gate criteria.
- **[Security Threat Model & Dependency Audit](Docs/SecurityThreatModelReview.md)** — Vulnerability analysis and mitigations.
- **[Software Bill of Materials (SBOM)](Docs/SBOM.md)** — NuGet runtime inventory and license validation.
- **[Architecture Decision Records](Docs/adr/README.md)** — Formally documented architectural decisions.

---

## 9. License

This project is licensed under the **[MIT License](LICENSE)**.

Copyright (c) 2026 DAWVC Contributors.

## Engine integration

The CLI and independent clients can share the framework-independent engine through `DawVcs.Bootstrap`. See [Engine integration](Docs/Engine_Integration.md) for ownership, progress, cancellation, typed failures and paged queries. No desktop framework is required.

## Self-hosted synchronization

Use `dawvc://[user@]host[:port]/repository-alias` for clone, fetch, pull and push
over SSH, alongside existing filesystem remotes. Host on your own PC, compatible
NAS or server with OpenSSH and DAWVC installed. SSH starts a temporary receiver;
no permanent DAWVC daemon is required. Strict host-key verification, public-key
authentication and host-owned repository policies apply. See
[SSH hosting](Docs/Self_Hosting_SSH.md) and [Protocol v1](Docs/Remote_Protocol.md).
This is DAWVC synchronization, not compatibility with GitHub/GitLab Git endpoints.


## Bare hosting and first publication

Create an empty receiver with the existing project's identity:
`dawvc init --bare --from <source> --dir <hosting-path>`.
Or seed all branches/history without working files:
`dawvc clone <source> <hosting-path> --bare`.
Both require absent/empty local destinations; source may be a path or dawvc:// URL.
Then configure filesystem origin or the host's SSH alias and Push normally. See
[SSH hosting](Docs/Self_Hosting_SSH.md) for provisioning and authorization details.


## Explicit divergence resolution

After `dawvc fetch origin`, choose one complete committed project snapshot with
`dawvc merge origin/main --take local -m "Keep local project version"` or
`--take incoming`. A two-parent commit preserves both histories; normal Push/Pull
can then resume. This does not combine FLP contents or mix assets automatically.
Commit your tracked/staged changes first. See [collaboration workflow](Docs/Collaborator_Workflow.md).


## Preview acceptance evidence

CI exercises the extracted Windows release executable with a synthetic collaboration
workflow and uploads a scoped JSON result. Linux DLL checks are recorded separately.
See [Preview acceptance](Docs/Preview_Acceptance.md) for the runner, mounted-share
option and pending real Windows/FL Studio/two-PC/SSH/NAS qualification procedures.


## Interrupted workspace publication

`dawvc recover` inspects persistent publication recovery data;
`dawvc recover --rollback` explicitly restores a Prepared workspace or verifies/
finalizes a Committed publication. Pending envelopes block new cooperating
mutations. Unexpected edits/corrupt backups are preserved for inspection, without
force overwrite. See [Recovery Guide](Docs/Recovery_Guide.md) for ownership,
staging replay, private backups, cleanup and power-loss qualification boundaries.

WP-SYNC-08 adds cooperative operation-wide writer ownership across CLI processes
and incoming SSH/filesystem push sessions. Busy local operations return exit 11;
busy receivers return remote exit 8. Await the owning operation; never delete live
lock files. Status/Doctor participate, and Pull/Push retain local ownership during
network waits. Older/raw clients, DAW saves and external restores still require
exclusive coordination. See [Recovery Guide](Docs/Recovery_Guide.md) and
[ADR-OPS-001](Docs/adr/ADR-OPS-001-repository-operation-locking.md).

The WP-SYNC-09 candidate integrates offline `remote track`, shared Push/Pull upstream
defaults, fresh read-only Fetch/Pull previews and cancellation onto the recovery/
locking stack. Dry-runs create no local lock/index/quarantine; real operations keep
writer ownership. See [CLI Reference](Docs/CLI_Reference.md) and the current
[implementation plan](Docs/Implementation%20plan.md) for review/qualification status.
Fetch/Pull support fresh `--dry-run` metadata previews without updating workspace,
objects, staging or refs. Fetch also shows planned tracking changes. Blob contents
and dirty workspace conflicts are not checked; see the CLI reference for limits.
