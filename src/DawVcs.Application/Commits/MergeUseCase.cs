using System.Text.Json;

using DawVcs.Application.Checkouts;
using DawVcs.Application.Common;
using DawVcs.Application.Exceptions;
using DawVcs.Application.Recovery;
using DawVcs.Application.Scanning;
using DawVcs.Domain.Artifacts;
using DawVcs.Domain.Common;
using DawVcs.Domain.Entities;
using DawVcs.Domain.Hashing;
using DawVcs.Domain.Repositories;
using DawVcs.Domain.Serialization;
using DawVcs.Domain.Storage;

namespace DawVcs.Application.Commits;

public enum MergeSnapshotChoice { Local, Incoming }

public sealed record MergeRequest(string RepositoryDirectory, string Reference,
    MergeSnapshotChoice Take, string Message, string? Author = null);

public sealed record MergeResult(CommitId CommitId, CommitId LocalParent, CommitId IncomingParent,
    SnapshotId SnapshotId, BranchName Branch, MergeSnapshotChoice Taken,
    IReadOnlyList<string> RestoredFiles);

/// <summary>Resolve diverged history by explicitly selecting one complete immutable snapshot.</summary>
public sealed class MergeUseCase(Func<string, IRepositoryContext> contextFactory, CheckoutUseCase checkout)
{
    public Task<MergeResult> ExecuteAsync(MergeRequest request, CancellationToken cancellationToken = default)
        => ExecuteAsync(request, cancellationToken, null);

    public async Task<MergeResult> ExecuteAsync(MergeRequest request, CancellationToken cancellationToken,
        IProgress<OperationProgress>? progress)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.RepositoryDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Reference);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Message);
        if (!Enum.IsDefined(request.Take)) throw new ArgumentException("Select local or incoming explicitly.");
        var author = request.Author ?? Environment.UserName;
        ArgumentException.ThrowIfNullOrWhiteSpace(author);
        cancellationToken.ThrowIfCancellationRequested();
        progress?.Report(new OperationProgress(OperationStage.OpeningRepository));
        WorkspaceRecoveryGuard.ThrowIfPending(request.RepositoryDirectory);
        WorkspaceMutationGuard.Repository(Path.GetFullPath(request.RepositoryDirectory));
        using var context = contextFactory(request.RepositoryDirectory);
        WorkspaceMutationGuard.Repository(context.RootPath);
        var config = context.LoadConfig();
        var branch = context.GetCurrentBranch();
        WorkspaceMutationGuard.Reference(context.RootPath, branch);
        var local = context.GetBranchCommit(branch) ?? throw new ArgumentException("Merge requires an existing local commit.");
        var incoming = context.ResolveReference(request.Reference) ?? throw new ArgumentException("Merge reference not found. Fetch remote history first.");
        if (incoming == local) throw new ArgumentException("The selected tips are identical; no merge is needed.");
        progress?.Report(new OperationProgress(OperationStage.TraversingHistory));
        var commits = new Dictionary<CommitId, Commit>();
        long metadataBytes = 0;
        var edges = 0;
        var localHistory = await ReadHistoryAsync(local).ConfigureAwait(false);
        var incomingHistory = await ReadHistoryAsync(incoming).ConfigureAwait(false);
        if (localHistory.Contains(incoming) || incomingHistory.Contains(local))
            throw new ArgumentException("Merge requires diverged tips; use normal Push/Pull for linear history.");
        if (!localHistory.Overlaps(incomingHistory))
            throw new ArgumentException("Unrelated histories cannot be merged.");

        progress?.Report(new OperationProgress(OperationStage.VerifyingObjects));
        // Both parent tip snapshots must remain recoverable, regardless of the chosen result.
        var snapshots = new Dictionary<SnapshotId, ProjectSnapshot>();
        foreach (var tip in new[] { local, incoming })
        {
            var id = commits[tip].SnapshotId;
            if (snapshots.ContainsKey(id)) continue;
            var payload = await ReadMetadataAsync(id.Value, ObjectType.Snapshot).ConfigureAwait(false);
            ProjectSnapshot snapshot;
            try
            {
                var dto = ObjectPayloadSerializer.ReadSnapshotDto(payload);
                if (dto?.SchemaVersion != 1) throw new InvalidEnvelopeException("Unsupported merge snapshot schema.");
                if (dto.Entries is null || dto.Entries.Length == 0 || dto.Entries.Any(e => e is null)
                    || dto.Dependencies?.Any(d => d is null) == true
                    || (dto.ContainerKind != (int)ArtifactKind.SingleFile && dto.ContainerKind != (int)ArtifactKind.Directory && dto.ContainerKind != (int)ArtifactKind.Package)
                    || (dto.ContainerKind == (int)ArtifactKind.SingleFile && dto.Entries.Length != 1))
                    throw new InvalidEnvelopeException("Invalid merge snapshot shape.");
                snapshot = ObjectPayloadSerializer.DeserializeSnapshot(payload, id)
                    ?? throw new InvalidEnvelopeException("Merge tip snapshot is missing.");
                if (!ContentHash.TryParse(dto.AggregateHash, out var aggregate) || aggregate != snapshot.Project.AggregateHash)
                    throw new InvalidEnvelopeException("Merge snapshot aggregate hash does not match its entries.");
            }
            catch (Exception ex) when (ex is JsonException or ArgumentException or FormatException)
            { throw new InvalidEnvelopeException("Invalid merge snapshot metadata.", ex); }
            var entries = snapshot.Project.Root.GetEntries().ToArray();
            PathSecurityGuard.ValidateAll(entries.Select(e => e.Path.Value));
            WorkspaceMutationGuard.Artifacts(context.RootPath, entries.Select(e => e.Path.Value));
            if (!entries.Any(e => e.Path == config.PrimaryArtifact))
                throw new InvalidEnvelopeException("Merge snapshot does not contain the configured primary artifact.");
            foreach (var entry in entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var header = await context.ObjectStore.ReadHeaderAsync(entry.Blob.Value, cancellationToken).ConfigureAwait(false);
                if (header.ObjectType != ObjectType.Blob || header.PayloadHash != entry.Hash || entry.Hash != entry.Blob.Value
                    || entry.Size < 0 || header.PayloadLength != (ulong)entry.Size)
                    throw new InvalidEnvelopeException("Merge snapshot has an invalid blob reference.");
                await context.ObjectStore.VerifyObjectIntegrityAsync(entry.Blob.Value, cancellationToken).ConfigureAwait(false);
            }
            snapshots.Add(id, snapshot);
        }

        var nestedProgress = progress is null ? null : new NestedOperationProgress(progress);
        var status = await new StatusUseCase(contextFactory).ExecuteAsync(new StatusRequest(context.RootPath), cancellationToken, nestedProgress).ConfigureAwait(false);
        if (status.Status.HasStagedChanges || status.Status.HasWorkingTreeModifications || status.Status.Renamed.Count > 0)
            throw new DirtyWorkspaceException("Merge requires committed tracked files and an empty staging index. Commit or move your changes first.", status.Status);
        EnsureUnchanged();
        var chosen = commits[request.Take == MergeSnapshotChoice.Local ? local : incoming].SnapshotId;
        if (commits.Count >= 50_000 || edges > 1_000_000 - 2)
            throw new InvalidEnvelopeException("The resulting merge graph would exceed its safety limits.");
        var merged = new Commit([local, incoming], chosen, author, DateTimeOffset.UtcNow, request.Message);
        var mergedPayload = merged.ToCanonicalBytes();
        if (mergedPayload.Length > 2 * 1024 * 1024)
            throw new ArgumentException("Merge author/message exceeds the supported commit metadata size.");
        progress?.Report(new OperationProgress(OperationStage.WritingObjects));
        cancellationToken.ThrowIfCancellationRequested();
        EnsureUnchanged();
        WorkspaceMutationGuard.ObjectWrite(context.RootPath, merged.Id);
        await context.ObjectStore.WriteObjectAsync(ObjectType.Commit, mergedPayload, cancellationToken).ConfigureAwait(false);
        EnsureUnchanged();
        var applied = await checkout.ExecuteFastForwardAsync(new FastForwardTarget(context.RootPath, branch, local, merged.Id) { RequireExpectedState = true },
            false, cancellationToken, nestedProgress).ConfigureAwait(false);
        progress?.Report(new OperationProgress(OperationStage.Completed));
        return new MergeResult(merged.Id, local, incoming, chosen, branch, request.Take, applied.RestoredFiles);

        void EnsureUnchanged()
        {
            if (context.GetCurrentBranch() != branch || context.GetBranchCommit(branch) != local)
                throw new RepositoryStateChangedException("Local branch changed during merge. Retry from the intended branch.");
        }

        async Task<byte[]> ReadMetadataAsync(ContentHash hash, ObjectType type)
        {
            var header = await context.ObjectStore.ReadHeaderAsync(hash, cancellationToken).ConfigureAwait(false);
            if (header.ObjectType != type || header.PayloadHash != hash || header.PayloadLength > 2UL * 1024 * 1024)
                throw new InvalidEnvelopeException("Invalid or oversized merge metadata.");
            metadataBytes += (long)header.PayloadLength;
            if (metadataBytes > 64L * 1024 * 1024) throw new InvalidEnvelopeException("Merge metadata exceeds its budget.");
            return await context.ObjectStore.ReadObjectPayloadAsync(hash, cancellationToken).ConfigureAwait(false);
        }

        async Task<HashSet<CommitId>> ReadHistoryAsync(CommitId tip)
        {
            var visited = new HashSet<CommitId> { tip };
            var pending = new Queue<CommitId>();
            pending.Enqueue(tip);
            while (pending.TryDequeue(out var id))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!commits.TryGetValue(id, out var commit))
                {
                    if (commits.Count >= 50_000) throw new InvalidEnvelopeException("Merge history exceeds its traversal limit.");
                    var payload = await ReadMetadataAsync(id.Value, ObjectType.Commit).ConfigureAwait(false);
                    try
                    {
                        if (ObjectPayloadSerializer.ReadCommitDto(payload)?.SchemaVersion != 1)
                            throw new InvalidEnvelopeException("Unsupported merge commit schema.");
                        commit = ObjectPayloadSerializer.DeserializeCommit(payload, id)
                            ?? throw new InvalidEnvelopeException("Merge history is incomplete.");
                    }
                    catch (Exception ex) when (ex is JsonException or ArgumentException or FormatException)
                    { throw new InvalidEnvelopeException("Invalid merge commit metadata.", ex); }
                    edges += commit.Parents.Count;
                    if (edges > 1_000_000) throw new InvalidEnvelopeException("Merge parent graph exceeds its budget.");
                    commits.Add(id, commit);
                }
                foreach (var parent in commit.Parents)
                    if (visited.Add(parent)) pending.Enqueue(parent);
            }
            return visited;
        }
    }
}
