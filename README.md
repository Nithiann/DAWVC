# DAWVC — DAW Version Control & Dependency Management

[![CI](https://github.com/Nithiann/DAWVC/actions/workflows/ci.yml/badge.svg?branch=main)](https://github.com/Nithiann/DAWVC/actions/workflows/ci.yml)
[![Release](https://img.shields.io/badge/Release-v0.3.0--preview.1-green.svg)](https://github.com/Nithiann/DAWVC/releases)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)
[![.NET](https://img.shields.io/badge/.NET-10.0-purple.svg)](https://dotnet.microsoft.com/)
[![Platform](https://img.shields.io/badge/Preview-Windows%20x64-blue.svg)](Docs/Installation.md)

> Version control, project history, dependency inspection, and self-hosted collaboration for music production.

DAWVC provides a command-line interface and a reusable engine. The current preview focuses on **FL Studio projects on Windows x64**. The core architecture supports separate DAW adapters; additional DAWs are outside this preview.

A graphical client is a separate product and is not included in this repository's release package.

## What is DAWVC?

DAWVC records versions of DAW projects together with explicitly bundled audio assets and discovered dependency information.

It helps you:

- Commit and restore project versions.
- Explore alternative arrangements using branches.
- Track recordings, samples, and other project assets.
- Inspect recorded plugin and asset requirements.
- Share committed history through your own storage or SSH host.
- Preserve both histories when collaborators make independent changes.
- Inspect and recover interrupted workspace updates.

DAWVC uses its own repository format and synchronization protocol. Standard GitHub and GitLab Git endpoints cannot act as DAWVC remotes.

## Core principles

### Preserve native project bytes

The FL Studio adapter inspects `.flp` files without rewriting their internal format. Project versions are stored and restored byte-for-byte.

Restoring a version replaces workspace files with the selected stored version. It does not rewrite embedded sample paths or convert plugin state.

### Identify stored content by hash

Objects use BLAKE3 content hashes. Identical stored content is reused within a repository across commits and branches.

Independent repositories have independent object stores.

### Separate shared history from local configuration

Shared history contains commits, snapshots, bundled assets, and recorded dependency information.

Local state includes staging data, caches, and bindings to resources installed on a particular machine. Native FLP files may still contain their original paths, so moving a project can require relinking in FL Studio.

### Protect existing work

Workspace updates check for conflicting local changes. Interrupted in-place workspace publications retain recovery information.

Repository locks coordinate participating DAWVC writers. They do not lock FL Studio itself: finish saves and recordings before performing repository operations.

## Features in v0.3.0-preview.1

| Area | Functionality |
| --- | --- |
| Repository management | Initialize working repositories and clone existing projects. |
| Staging | Explicitly add new project assets; the primary project file is included when committing. |
| History | Immutable commits and snapshots with first-parent history inspection. |
| Branching | Create, inspect, switch, and delete local branches. |
| Restoration | Restore committed versions into the workspace or a separate directory. |
| Synchronization | Fetch, Pull, and fast-forward-only Push. |
| Hosting | Filesystem paths, mounted network shares, and `dawvc://` over SSH. |
| Bare repositories | Provision hosting repositories without a working tree. |
| Upstreams | Configure default Push/Pull targets with `remote track`. |
| Previews | Inspect current remote changes with Fetch/Pull dry-run without updating the local repository. |
| Merge | Join diverged histories by explicitly choosing one complete project snapshot. |
| Recovery | Inspect and explicitly recover interrupted workspace publications. |
| Diagnostics | Inspect dependencies and environment health with `doctor`. |
| Integrity | Verify stored objects and repository history with `fsck`. |

## Installation

Download the Windows x64 ZIP and `SHA256SUMS.txt` from [GitHub Releases](https://github.com/Nithiann/DAWVC/releases).

The Windows distribution is self-contained and does not require a separately installed .NET runtime.

1. Verify the ZIP against its entry in `SHA256SUMS.txt`.
2. Extract the package into a permanent directory.
3. Add that directory to your user `PATH`.
4. Verify the installation:

```powershell
dawvc --version
dawvc --help
```

SSH synchronization additionally requires the OpenSSH client, `ssh`, in `PATH`.

See the [Installation Guide](Docs/Installation.md) for detailed instructions.

## Quick start

The following example assumes an existing, saved `Song.flp` and a project-owned sample at `Samples\Kick.wav`.

```powershell
cd C:\Music\MySong

# Initialize the project repository.
dawvc init --name "My Song" --primary Song.flp

# Inspect the project and workspace.
dawvc scan
dawvc status

# Explicitly include a sample in repository history.
dawvc add Samples\Kick.wav

# Record the first version.
dawvc commit -m "Initial arrangement"

# Inspect history, dependencies, and stored integrity.
dawvc log
dawvc doctor
dawvc fsck
```

Use paths that exist in your own project. Files passed to `add` should be inside the working repository.

The primary project file is included by Commit. Newly discovered assets are not silently added to shared history.

See the [Quick Start Guide](Docs/QuickStart.md) for the complete workflow.

## Branches and restoration

Create an alternative version of the project:

```powershell
dawvc switch -c alternative-mix

# Edit and save the project in FL Studio, then commit.
dawvc commit -m "Alternative mix"

# Return to the original branch.
dawvc switch main
```

To inspect an older version without replacing the active workspace, substitute an actual commit ID from `dawvc log`:

```powershell
dawvc checkout <commit-id> --restore-to C:\Music\RestoredVersion
```

Use a separate destination for external restoration. Review local changes before switching or checking out another version.

## Filesystem remotes and bare hosting

A bare repository stores shared history without checked-out project files. It is suitable for hosting.

Create an empty receiver with the current project's repository identity:

```powershell
cd C:\Music\MySong

dawvc init --bare --from . --dir C:\DawvcRemotes\MySong
dawvc remote add origin C:\DawvcRemotes\MySong
dawvc remote track origin main

dawvc push --dry-run
dawvc push
```

The destination must be absent or empty. Bare initialization inherits the source identity; independently initializing an unrelated project does not create a compatible receiver.

Alternatively, copy existing history into a new bare repository:

```powershell
dawvc clone C:\Music\MySong C:\DawvcRemotes\MySongCopy --bare
```

Clone a working copy from a hosted repository:

```powershell
dawvc clone C:\DawvcRemotes\MySong C:\Music\MySong-Collaboration
```

Filesystem remotes also support `file://` URLs and mounted network shares. Actual NAS/SMB locking, permissions, and rename behavior must be validated on the intended deployment.

## Self-hosted synchronization over SSH

SSH remotes use this format:

```text
dawvc://[user@]host[:port]/repository-alias
```

Examples:

```text
dawvc://alice@studio-server/projects/my-song
dawvc://alice@nas:2222/projects/my-song
```

The default SSH port is 22.

The repository alias is configured by the host operator. It is not a client-selected filesystem path.

The host requires:

- A compatible DAWVC deployment.
- An OpenSSH server.
- An initialized repository with the source project's identity.
- A host-owned policy mapping allowed aliases to repository paths.
- Explicit push permission for writable repositories.

SSH starts a temporary `dawvc serve` process for each connection. No permanent DAWVC daemon is required, but the host must be online and reachable.

DAWVC uses public-key/agent authentication and strict host-key verification. Unknown or changed host keys are refused; credentials are not embedded in remote URLs.

After the host is configured:

```powershell
dawvc remote add studio dawvc://alice@studio-server/projects/my-song
dawvc remote track studio main

dawvc push --dry-run
dawvc push
```

Another collaborator can clone the same project:

```powershell
dawvc clone dawvc://alice@studio-server/projects/my-song C:\Music\SharedSong
```

See [Self-hosting over SSH](Docs/Self_Hosting_SSH.md) for provisioning, authorization, host-key setup, and platform requirements.

## Remote management and upstreams

```powershell
dawvc remote add <name> <url>
dawvc remote remove <name>
dawvc remote list
dawvc remote track <name> <remote-branch> [--local-branch <local-branch>]
```

Configure a differently named remote branch:

```powershell
dawvc remote track origin published
```

Plain Push and Pull now use `origin/published` for the active local branch.

Target selection follows these rules:

- An explicit matching upstream remote preserves its branch mapping.
- A different explicit remote defaults to the local branch name.
- An explicit Push branch selects that local branch and the same destination branch name.
- An explicit Pull branch selects a remote source to integrate into the active local branch.

`remote track` works offline. The remote must already be configured; the selected local branch must exist, except that the active unborn branch is allowed. The remote branch may be created by a future Push.

## Fetch, Pull, and previews

```powershell
dawvc fetch origin
dawvc pull --dry-run
dawvc pull
```

Fetch retrieves and verifies history and updates remote-tracking references. Pull fetches its selected source and integrates a fast-forward update into the active local branch.

Fresh Fetch/Pull previews read current remote advertisements and bounded commit/snapshot metadata. They work with missing or stale tracking references.

```powershell
dawvc fetch origin --dry-run
dawvc pull --dry-run
```

These previews do not update local objects, refs, configuration, workspace files, the staging index, or quarantine.

Fetch/Pull preview counts are transfer estimates. They do not download remote blob bodies, prove their availability, or predict dirty-workspace conflicts. Previews do not reserve refs or provide an atomic view during concurrent changes. Actual execution fetches and validates again.

## Resolving diverged histories

Push and Pull do not automatically merge independently changed projects.

When collaborators create diverged commits:

1. Commit or move conflicting local work.
2. Fetch the remote history.
3. Review the available versions.
4. Explicitly choose one complete snapshot.

For example:

```powershell
dawvc fetch origin
dawvc merge origin/main --take incoming -m "Choose incoming project version"
dawvc push
```

Use `--take local` to retain the local snapshot instead.

Merge creates a two-parent commit, preserving both histories. The choice applies to the complete project snapshot, including its recorded asset set.

DAWVC does not automatically combine arrangements, automation, recordings, or FLP internals.

See the [Collaborator Workflow](Docs/Collaborator_Workflow.md) for a full example.

## Recovery and concurrent operations

Inspect interrupted workspace publication:

```powershell
dawvc recover
```

Explicitly recover or finalize it:

```powershell
dawvc recover --rollback
```

For a Prepared publication, recovery restores the saved pre-publication state. For a Committed publication, it verifies the completed state and finalizes recovery metadata.

Unexpected edits, corrupt backups, or ambiguous recovery data cause refusal rather than forced overwrite.

Participating local writers and incoming push sessions use repository-wide ownership:

| Exit code | Meaning |
| --- | --- |
| `10` | Pending or conflicting workspace recovery requires attention. |
| `11` | Another participating local operation owns the repository. |
| `130` | The operation was cooperatively cancelled. |

A busy receiving host is reported through the remote error contract.

Await the active operation before retrying. Persistent empty lock files are normal; never delete them to break a live owner's lock.

Cancellation does not guarantee that already published changes were undone. Inspect repository state after an interruption.

See the [Recovery Guide](Docs/Recovery_Guide.md) for exact coverage, cleanup, and failure boundaries.

## Dependencies and FL Studio portability

DAWVC records dependency information discovered by the FL Studio adapter and supports local bindings to installed resources.

```powershell
dawvc scan
dawvc doctor
dawvc bind <dependency-id> <local-path>
```

Keep these boundaries in mind:

- Required plugins must be installed and licensed separately.
- Plugin binaries, license keys, and external libraries are not automatically supplied or bundled.
- Explicitly bundled files become part of shared repository history; only include material you may share.
- A matching or newer plugin version does not by itself guarantee identical playback.
- FL Studio may require Extra Search Folders or manual relinking when paths differ between machines.
- Unsupported project formats may fall back to opaque file tracking, with reduced dependency inspection.

A successful integrity check proves repository integrity, not complete musical reproducibility.

See [FL Studio Search Paths & Relinking](Docs/FLStudio_Search_Paths_Relinking.md).

## CLI overview

Use `dawvc <command> --help` for exact arguments and options.

| Command | Purpose |
| --- | --- |
| `dawvc init` | Initialize a working repository. |
| `dawvc init --bare --from <source>` | Provision an empty hosting repository with source identity. |
| `dawvc clone <url> [directory]` | Create a working copy; `--bare` creates hosting storage. |
| `dawvc scan` | Inspect project format and discover dependencies. |
| `dawvc status` | Inspect working-tree and staging state. |
| `dawvc add <path>...` | Stage project files or assets. |
| `dawvc commit -m <message>` | Record a project version. |
| `dawvc log` | Inspect first-parent commit history. |
| `dawvc branch [name]` | List or create local branches; deletion options are available. |
| `dawvc switch <branch>` | Switch the working tree to another branch. |
| `dawvc checkout <reference>` | Restore a committed version, optionally into a separate directory. |
| `dawvc remote add <name> <url>` | Configure a remote. |
| `dawvc remote remove <name>` | Remove a configured remote. |
| `dawvc remote list` | List configured remotes. |
| `dawvc remote track <name> <branch>` | Configure a local branch's upstream. |
| `dawvc fetch [remote] [branch]` | Retrieve remote history and update tracking refs. |
| `dawvc pull [remote] [branch]` | Fetch and integrate into the active local branch. |
| `dawvc push [remote] [branch]` | Publish committed history with fast-forward protection. |
| `dawvc merge <reference> --take local\|incoming -m <message>` | Join diverged histories using one complete snapshot. |
| `dawvc recover [--rollback]` | Inspect or explicitly recover interrupted workspace publication. |
| `dawvc doctor` | Report project dependency and environment health. |
| `dawvc bind <dependency-id> <path>` | Bind a recorded dependency to a local resource. |
| `dawvc fsck` | Verify repository objects and history integrity. |
| `dawvc serve` | Serve configured repositories through an SSH stdio session. |

Complete reference: [CLI Command Reference](Docs/CLI_Reference.md).

## Preview scope and validation

The current preview targets FL Studio projects on Windows x64. The engine and CLI are also exercised by automated tests on Ubuntu; that does not establish a supported native FL Studio deployment on every OS.

Automated validation includes:

- Unit, infrastructure, adapter, integration, and end-to-end tests.
- A synthetic collaboration workflow using the extracted Windows release executable.
- SSH protocol and process-lifetime tests.
- Workspace recovery tests using actual process termination.
- Repeated Windows post-exit lock checks.
- CI test-result artifacts retained for failure diagnosis.

Real FL Studio opening/playback, two physical workstations, native Windows console cancellation, and deployment-specific NAS/SMB and power-loss behavior require separate field acceptance.

Use copies of projects during preview testing. See [Preview Acceptance](Docs/Preview_Acceptance.md) and [Known Limitations](Docs/Known_Limitations.md).

## Development and packaging

### Requirements

- .NET 10 SDK.
- Git.
- PowerShell for the Windows packaging script.
- OpenSSH client/server when testing actual SSH hosting.
- Windows and an appropriate FL Studio installation for native adapter acceptance.

### Build and test

```powershell
dotnet restore
dotnet build -c Release --no-restore
dotnet test -c Release --no-build --logger "trx"
dotnet format --no-restore --verify-no-changes
```

### Package the Windows distribution

```powershell
pwsh -File ./scripts/package.ps1 -Version 0.3.0-preview.1
```

This produces a self-contained Windows x64 ZIP and `SHA256SUMS.txt` under `artifacts`.

### Solution structure

```text
src/
  DawVcs.Domain/                 Domain models and contracts
  DawVcs.Application/            Use cases and orchestration
  DawVcs.Infrastructure/         Filesystem storage, SQLite, and transports
  DawVcs.Adapters.Abstractions/   DAW adapter contracts
  DawVcs.Adapters.FLStudio/       Read-only FL Studio inspection
  DawVcs.Bootstrap/              Shared engine composition
  DawVcs.Cli/                    Command-line application

tests/                          Automated tests and test-only process host
scripts/                        Packaging and acceptance tools
Docs/                           Guides, requirements, design, and decisions
```

Independent clients can reuse the engine through `DawVcs.Bootstrap`. See [Engine Integration](Docs/Engine_Integration.md) for ownership, progress, cancellation, and failure contracts.

## Documentation

### User and operator guides

- [Installation & Setup](Docs/Installation.md)
- [Quick Start](Docs/QuickStart.md)
- [CLI Command Reference](Docs/CLI_Reference.md)
- [Collaborator Workflow](Docs/Collaborator_Workflow.md)
- [Self-hosting over SSH](Docs/Self_Hosting_SSH.md)
- [Recovery Guide](Docs/Recovery_Guide.md)
- [FL Studio Search Paths & Relinking](Docs/FLStudio_Search_Paths_Relinking.md)
- [Known Limitations](Docs/Known_Limitations.md)
- [Preview Acceptance](Docs/Preview_Acceptance.md)

### Architecture and development

- [Engine Integration](Docs/Engine_Integration.md)
- [Remote Protocol](Docs/Remote_Protocol.md)
- [Technical Design](Docs/Technical%20Design.md)
- [Requirements](Docs/MVP%20Requirements.md)
- [Implementation Plan](Docs/Implementation%20plan.md)
- [Security Threat Model](Docs/SecurityThreatModelReview.md)
- [Software Bill of Materials](Docs/SBOM.md)
- [Architecture Decision Records](Docs/adr/README.md)

## License

DAWVC is licensed under the [MIT License](LICENSE).

Copyright (c) 2026 DAWVC Contributors.
