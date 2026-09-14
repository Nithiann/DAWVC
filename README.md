# DAWVC — DAW Version Control & Dependency Management

[![CI](https://github.com/dawvc/dawvc/actions/workflows/ci.yml/badge.svg)](https://github.com/dawvc/dawvc/actions/workflows/ci.yml)
[![Release](https://img.shields.io/badge/Release-v0.1.0--preview.1-green.svg)](https://github.com/dawvc/dawvc/releases)
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
|---|---|
| `dawvc init` | Initializes a new DAWVC repository in the current or target directory. |
| `dawvc scan` | Inspects the project for referenced samples, recordings, and plugins. |
| `dawvc status` | Shows workspace state, modified tracked assets, and uncommitted discoveries. |
| `dawvc add <path>` | Adds an external asset or sample to tracked repository dependencies. |
| `dawvc commit -m <msg>` | Records a new immutable project snapshot into repository history. |
| `dawvc log` | Displays commit history with snapshot metadata and author info. |
| `dawvc branch [name]` | Lists existing branches or creates a new branch pointer. |
| `dawvc switch <branch>` | Safely switches the workspace to a different branch. |
| `dawvc checkout <commit>` | Restores a specific snapshot (supports `--restore-to` and `--force`). |
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

