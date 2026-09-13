# DAWVC — DAW Version Control & Dependency Management

[![CI](https://github.com/dawvc/dawvc/actions/workflows/ci.yml/badge.svg)](https://github.com/dawvc/dawvc/actions/workflows/ci.yml)
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

## 3. MVP v0.1 Scope & Feature Set

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

## 4. CLI Overview

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
| `dawvc doctor` | Verifies whether all required samples, plugins, and dependencies exist locally. |
| `dawvc fsck` | Validates internal integrity across the object store and refs. |

---

## 5. Architecture & Solution Structure

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

### Directory Structure

```text
src/
  DawVcs.Domain/                  # Pure domain entities, value objects, and invariants (0 external deps)
  DawVcs.Application/             # Use cases, interfaces, and command orchestration
  DawVcs.Infrastructure/          # Object store, BLAKE3 hasher, atomic filesystem I/O, and envelope serialization
  DawVcs.Adapters.Abstractions/   # Contracts for DAW inspection and capability detection
  DawVcs.Adapters.FLStudio/       # Read-only FL Studio (.flp) bounded parser and metadata extractor
  DawVcs.Cli/                     # CLI entry point (System.CommandLine + Spectre.Console)

tests/
  DawVcs.Domain.Tests/            # Unit tests & Architectural boundary enforcement tests
  DawVcs.Application.Tests/       # Use case tests using test doubles
  DawVcs.Infrastructure.Tests/   # Object store, envelope corruption, and atomic I/O fault-injection tests
  DawVcs.Adapters.FLStudio.Tests/ # FLP fixture parsing and regression tests
  DawVcs.IntegrationTests/        # Cross-component integration tests
  DawVcs.EndToEndTests/           # CLI acceptance tests (AC-001 through AC-014)

docs/
  Technical Design.md            # Normative system architecture v1.1
  MVP Requirements.md            # Requirements specification baseline v1.0
  Implementation plan.md         # Milestone phases and work packages (WP-00 through WP-10)
  adr/                           # Architecture Decision Records
```

---

## 6. Development & Building

### Prerequisites
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) (version `10.0.301` or later)
- Windows 11 x64 (recommended for FL Studio adapter validation) or Linux/macOS for core domain development.
- Git

### Build
```powershell
# Restore and build all projects in Release configuration
dotnet build -c Release
```

> **Note:** The build is configured with `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>` and strict code analysis rules.

### Test
```powershell
# Run unit, architecture, infrastructure, and adapter tests
dotnet test -c Release --logger "console;verbosity=normal"
```

### Code Formatting
```powershell
# Verify code formatting against .editorconfig rules
dotnet format --verify-no-changes
```

---

## 7. Documentation & Specifications

- [Technical Design](Docs/Technical%20Design.md) — Comprehensive technical architecture, object models, envelope specs, and error taxonomies.
- [MVP Requirements Specification](Docs/MVP%20Requirements.md) — Normative functional and non-functional requirements including acceptance criteria.
- [Implementation Plan](Docs/Implementation%20plan.md) — Milestone roadmaps, risk spikes, and Definition of Done.
- [Architecture Decision Records](Docs/adr/README.md) — Formally documented architectural choices.

---

## 8. License

This project is licensed under the **[MIT License](LICENSE)**.

Copyright (c) 2026 DAWVC Contributors.
