# ADR-REM-003: Shared sync targets and explicit upstream configuration

Date: 23 November 2026. Status: Proposed for PR review.
Requirements: FR-SYN-001..007; WP-SYNC-02.

## Problem

Pull previously fetched origin before resolving its upstream, so a branch tracking
backup/published could fetch the wrong host. An explicit Pull branch was treated
as a local destination, despite the CLI describing a remote source. Push ignored
a differently named upstream branch when its matching remote was explicit.

## Decision

Resolve a shared immutable sync target before transfer. Push selects the requested
local branch or the active one; its explicit branch also names the destination.
Pull always targets the active local branch; its explicit branch names the remote
source. Omitted remote selects upstream or origin. A matching explicit remote
preserves upstream mapping; another remote falls back to the local branch name.
Fetch and Pull integration receive the same resolved source. Pull rechecks HEAD
after Fetch and reports RepositoryStateChangedException (Validation category) if
the local branch changed; it does not proceed to checkout. This is not a global
lock against all concurrent workspace edits. Invalid explicit
remote names fail validation instead of silently selecting a default host.

Expose `remote track <remote> <remote-branch> [--local-branch name]` through the
shared RemoteUseCase. Validate existing local branch/configured remote and allow a
future remote branch for first publish. The active unborn branch is valid. Store
settings using existing remotes.json schema and atomic replacement. No network
connection, host credentials or remote mutation is part of setting an upstream.

CLI clone/fetch/pull now pass InvocationContext cancellation tokens just like
Push. Await engine cleanup; exit 130 identifies cancellation without promising
rollback. Shared engine APIs remain the entry point for independent clients.

## Alternatives and consequences

Separate selection code in each use case caused the observed mismatch. An implicit
post-Push upstream update would add local persistence after remote publication,
introducing a partial-success outcome; explicit tracking avoids it in this WP.
No force-push, merge policy or repository identity change is introduced. Concurrent
configuration writers are not newly serialized; replacement prevents partial files,
not last-writer conflicts. Existing corrupt-config recovery policy is unchanged.

Fresh Pull dry-run ancestry requires a separate preview metadata lifetime design
(WP-SYNC-03). This WP fixes source selection, not that historical limitation. Bare
repository provisioning is WP-SYNC-04. No new on-disk schema or wire protocol.
