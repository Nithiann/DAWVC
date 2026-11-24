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

Queries avoid staging/index repair. Stock filesystem context/object-store construction is now side-effect free
(WP-SYNC-09); actual writes initialize their storage and the staging index is lazy.
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


## Bare hosting integration (WP-SYNC-04)

Resolve `BareRepositoryUseCase` from Bootstrap. `BareRepositoryRequest(Source,
TargetDirectory)` initializes an empty same-identity receiver; `CopyHistory: true`
fetches verified reachable history and publishes all fetched branch heads. Results
include configuration, copied branch count, default tip and transfer counts.
`CloneRequest.Bare` exposes the same copy path through Clone without checkout or
Doctor; `Force` plus `Bare` is rejected. Existing positional request arguments keep
their meaning. No GUI or new package/protocol dependency is introduced.

Contexts/transports are disposed before publishing the bare root. Child completion
is suppressed; top-level completion follows directory publication. Await the call
on cancellation; it does not reserve the destination or undo a completed rename.
Source providers must include matching configuration in their handshake. Custom
Fetch providers must supply accurate updated refs/verified objects in the new
empty staging repository. The target is local, even when the source uses SSH.
See ADR-REM-005 and the recovery guide for publication and cleanup boundaries.


## Explicit merge integration (WP-SYNC-05)

Resolve `MergeUseCase`; pass MergeRequest(repository, reference,
MergeSnapshotChoice.Local/Incoming, message, author). There is no default snapshot
choice. The result exposes new commit ID, both ordered parent IDs, selected
snapshot ID, active branch and restored files. Merge accepts only diverged related
local tips; clients own Fetch, review and deliberate snapshot selection. Both tip
snapshots/blobs are verified, selected metadata/dependencies/completeness are
reused by identity, and checkout publishes the result without force.

Progress uses existing typed stages and nested completion suppression. Await
cancellation; a durable merge is not undone after publication. State-change errors
classify as Validation. `FastForwardTarget.RequireExpectedState` opts into active
branch/tip checks immediately before journal application; Merge sets it. Existing
callers retain their prior behavior. These state checks complement the operation-wide ownership added in WP-SYNC-08.
No persisted schema, wire contract, UI or dependency changes. Failed operations
can leave unreachable objects; full historical snapshot/blob auditing remains Fsck.


## Workspace recovery integration (WP-SYNC-07)

Resolve WorkspaceRecoveryUseCase for explicit RollbackAsync; static InspectAsync
reads manifest summaries without opening a context/index. Results expose counts,
IDs and states. WorkspaceRecoveryRequiredException maps to the appended
OperationFailureCategory.RecoveryRequired; CLI exit 10. Clients must not infer
that a process failure undid publication, nor retry mutations before inspecting
pending recovery. No forced conflict overwrite or auto-recovery is exposed.

Checkout's working-repository publication writes a local durable envelope and
retains existing handled rollback. Recovery preserves logical staged entries
(path/blob/hash/size/role), not SQLite/cache bytes. Ownership remains caller/local
operation based; low-level repository/index methods bypass use-case guards and
must not be used concurrently to mutate a recovering repository. Read the recovery
guide for committed decisions, partial staging replay, cleanup and power limits.
No shared object/wire schema or new runtime dependency; a test-only crash host is
not part of the shipped CLI. WP-SYNC-08 adds operation-wide ownership as described below.


## Operation ownership (WP-SYNC-08)

Stock mutation use cases now acquire a fail-fast repository-wide OS handle lease
before context creation and keep it through local work/resource disposal. Status
and Doctor participate because they update caches/bindings. Pull/Push retain local
ownership while awaiting the remote. Await operations and cancellation before
retrying; RepositoryBusyException maps to appended OperationFailureCategory.
RepositoryBusy and CLI exit 11. A busy incoming SSH/filesystem push is RemoteError
(exit 8). No public token/reentrancy API or wire change is introduced.

Known nested calls use internal explicit capability copies; unrelated calls from
progress handlers/Task.Run receive Busy rather than inheriting ambient authority.
A slow progress handler can therefore prolong ownership. Do not await a second
public writer from a handler. Operation, recovery and ref handles always acquire
in that order; no cross-repository waits. Init/Clone/bare provision also use a
persistent hashed sibling creation lease (parent write permission required).

Use the stock composition for this guarantee. Raw context/index methods and custom
overrides/providers must implement their own coordination; old clients, DAW saves
and external restore targets remain outside it. Read-only queries are not atomic
workspace snapshots and custom backend opening may write housekeeping metadata.
Do not delete operation/ref/recovery/creation lock files. Empty files persist after
handles close; process death releases ownership. See ADR-OPS-001 and Recovery Guide.


## Integrated sync targets and previews (WP-SYNC-09)

`RemoteUseCase.TrackRemote(TrackRemoteRequest)` validates/saves an upstream offline;
track mutations share the Busy/recovery guard contract. Push/Pull resolve one
SyncTarget before transfer. Pull always integrates into the active local branch;
explicit matching remote names retain upstream mapping. Existing public request
constructors and bare/merge/recovery entry points are retained.

FetchResult.Preview is an immutable pinned advertisement/verified parent overlay.
Pull dry-run requires this from custom Fetch providers instead of falling back to
stale tracking. Metadata is bounded, verified and kept in memory; blobs are not
fetched by Fetch/Pull preview. Dry-run creates no writer lease or storage/index/
quarantine. Previews are advisory read operations, not atomic views during other
writers; pending recovery still refuses them. Real Fetch/Pull/Push retain ownership
across network waits and nested Status/Checkout/Doctor, and publication still uses
the durable envelope. Both merge parents participate in preview and execution.

Stock context construction no longer creates directories or opens an index.
Disposal only disposes/clears an index pool actually owned by that context. Using
StagingIndex after context disposal throws. Custom providers must maintain the
same read-only/lifetime contract; contexts remain caller-owned and not shared
across concurrent operations. No new on-disk/object/wire schema or runtime package.
