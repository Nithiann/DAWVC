# Engine integration for independent clients

The CLI and separately maintained clients share `DawVcs.Application` through
`DawVcs.Bootstrap`. No desktop framework, UI thread or GUI package is required.
The repository remains MIT licensed; using the engine does not change its license.
This change does not publish a supported NuGet distribution or an IPC protocol.

## Composition and lifetime

Reference `src/DawVcs.Bootstrap/DawVcs.Bootstrap.csproj` and call
`services.AddDawVcs()` on the host's `IServiceCollection`. Build and dispose the
service provider in the host. Resolve Application use cases from that provider.
The CLI uses the same registration method. Domain and Application do not depend
on Bootstrap; Bootstrap selects Infrastructure and the FL Studio adapter.

Factory-created repository contexts, remote transports and quarantine stores are
caller-owned. Use cases dispose their contexts when their awaited work finishes.
Never reuse a context across calls that independently dispose it. Disposing the
SQLite index clears its own connection pool, releasing idle native handles; it
does not close unrelated pools or interrupt active connections. A host must await
active operations before disposing its resources or deleting a repository.

## Operation contract

Existing request/result use-case entry points remain available to the CLI.
Progress-aware overloads accept optional `IProgress<OperationProgress>` per call.
Stages, completed/total counts and item/byte units are typed. Unknown total is
null and differs from zero. Progress contains no raw paths or free-form messages.
Clients own dispatching, throttling and presentation of callbacks.

Pass the call's cancellation token and await termination, including compensation.
Cancellation is cooperative and does not guarantee rollback of every durable
effect. Nested operations do not prematurely publish a terminal Completed stage.
`OperationFailure.FromException` classifies typed errors without parsing message
text. Unknown errors remain Unknown. Technical exceptions and recovery paths can
contain private data; redact before logging or export. Incomplete rollback must
retain recovery files; see [Recovery Guide](Recovery_Guide.md).

## Repository opening and paged reads

`OpenRepositoryUseCase` validates markers/configuration and returns root, project
and branch without reading the staging index or hashing workspace status.
`ViewerQueries` exposes metadata, first-parent history, commit files and recorded
snapshot dependencies independently of any UI. History defaults to 50 entries,
maximum 100; file/dependency pages default to 200, maximum 500. Use returned
commit cursors/offsets and retain the selected immutable commit ID across pages.
Snapshot dependencies omit original locators, provenance and machine paths.
They describe recorded snapshots, not a live dependency scan.

Queries avoid staging/index repair. Context/object-store construction is now side-effect free. Staging-index creation
is lazy and happens only if requested. Object writes initialize scratch storage;
explicit Init creates its layout. Metadata/viewer/preview reads do not create it.
Snapshot objects are still fully deserialized internally, so bounded output does
not provide bounded-memory loading for arbitrarily large snapshots.

## Compatibility and validation

No persisted schema, object format or successful CLI command contract changes.
Corrupt SQLite index initialization deliberately fails closed instead of erasing
staged intent; see [ADR-009](adr/ADR-009-sqlite-local-index.md).

Regression coverage includes shared registrations, context ownership, progress,
cancellation, failed compensation, pagination, path redaction, corruption byte
preservation and native SQLite handle release. The complete core solution is
validated with Release build/test and formatting checks. Desktop code and GUI
framework dependencies are outside this integration change.

Validation on 23 November 2026 (Linux, .NET SDK 10.0.404): Release build
completed with zero warnings/errors; all 544 core solution tests passed with no
skips; `dotnet format --no-restore --verify-no-changes` passed. Windows
verification remains for CI after PR creation.

## Push integration

Resolve `PushUseCase` from Bootstrap and pass `PushRequest`, a cancellation token
and optional per-call `IProgress<OperationProgress>`. The result pins old/new tips,
local/destination branch names and transferred payload counts. Dry-run counts are
estimates of missing objects at planning time; another actor can fill objects
before transfer. No local tracking or upstream changes are made.

`PushNonFastForwardException` maps to NonFastForward and `PushRejectedException`
is a typed remote failure. Missing/corrupt closure objects are integrity failures.
Progress completion follows publication; await the task rather than inferring
rollback from cancellation. See ADR-REM-001 for the cooperating-writer model.

## SSH integration

The default transport factory recognizes `dawvc://`; the same Application APIs
work for local and SSH remotes. No SSH subprocess is started merely by validating
or adding a remote. Keys/host trust come from OpenSSH configuration and agent,
not engine or GUI credential fields. A host exposes only aliases in its policy
through transient `serve`. See Remote_Protocol.md and Self_Hosting_SSH.md.

## Upstream configuration and sync target selection

Call `RemoteUseCase.TrackRemote(TrackRemoteRequest, CancellationToken)` to configure
an existing local branch against a configured remote; active unborn HEAD is valid.
It performs no network I/O. Existing settings are atomically replaced and other
remote/upstream entries are retained. Resolve Push/Pull through the shared use
cases rather than reimplementing target rules in a client. Pull's optional Branch
is the remote source; its result Branch is always the active local destination.
A matching explicit remote preserves an upstream mapping.

CLI handlers now use the same per-invocation cancellation contract for clone,
fetch, pull and push. Fresh Pull dry-run metadata is implemented in WP-SYNC-03 below; it is distinct
from target selection.

For developer validation, `scripts/test_sync_cancellation.py` sends real SIGINT to
the Linux CLI while an isolated SSH shim blocks connection setup. It checks exit
130 and SSH child termination for all four sync commands, without a real host or
keys. Set DAWVC_DOTNET if needed. This is not native Windows Ctrl+C validation.

Pull rechecks the active branch after Fetch. `RepositoryStateChangedException`
(classified as Validation) stops checkout if the branch changed during transfer.
Fetched metadata may remain. This precondition check is not a cross-process lock
against all concurrent local mutations.

## Fresh preview contract

FetchResult.UpdatedRefs contains planned updates when IsDryRun=true, actual applied
updates otherwise. Preview carries copied read-only advertised refs and verified
commit parent lists, no handles/paths; it remains usable after transport/context
disposal. Pull dry-run uses that snapshot through DeterminePreviewStateAsync plus
existing local commit reads, never cached tracking refs. A custom FetchUseCase
implementation must populate Preview for dry-run; missing snapshots fail closed.

No quarantine factory is invoked or stale sessions pruned during preview. No
staging property is accessed, so disposal does not clear another index owner's
SQLite pool. Each metadata object is capped at 2 MiB, aggregate received metadata
at 64 MiB, inspected object identities at 100,000, commit/ancestry budgets at
50,000 and remote parent links at 1,000,000. Limit, integrity and cancellation
failures preserve local content/layout. Blob payloads and workspace dirty-state
planning are deliberately excluded; see the CLI reference for advisory semantics.
