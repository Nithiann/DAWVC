using System.Text.Json;

using DawVcs.Application.Common;
using DawVcs.Domain.Common;
using DawVcs.Domain.Hashing;
using DawVcs.Domain.Remotes;
using DawVcs.Domain.Repositories;
using DawVcs.Domain.Serialization;
using DawVcs.Domain.Storage;

namespace DawVcs.Application.Remotes;

/// <summary>Inspect missing metadata only. No quarantine, blobs, writes or index access.</summary>
internal static class FetchPreviewPlanner
{
    internal const int MaxCommits = 50_000;
    private const int MaxObjects = 100_000;
    private const int MaxParentEdges = 1_000_000;
    private const long MaxMetadataBytes = 64L * 1024 * 1024;

    public static async Task<FetchResult> PlanAsync(string remote, IReadOnlyList<RemoteRef> refs,
        IRemoteTransport transport, IRepositoryContext context, CancellationToken cancellationToken,
        IProgress<OperationProgress>? progress)
    {
        var visited = new Dictionary<ContentHash, (ObjectType Type, long? Size)>();
        var parents = new Dictionary<CommitId, IReadOnlyList<CommitId>>();
        var pending = new Queue<(ContentHash Hash, ObjectType Type, long? Size)>();
        foreach (var head in refs) pending.Enqueue((head.HeadCommit.Value, ObjectType.Commit, null));
        var objects = 0;
        var commits = 0;
        var edges = 0;
        long bytes = 0;
        long metadataBytes = 0;
        while (pending.TryDequeue(out var next))
        {
            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report(new OperationProgress(OperationStage.TraversingHistory, objects));
            if (visited.TryGetValue(next.Hash, out var prior))
            {
                if (prior.Type != next.Type || (prior.Size.HasValue && next.Size.HasValue && prior.Size != next.Size))
                    throw new InvalidEnvelopeException("Conflicting object references in preview metadata.");
                continue;
            }
            if (visited.Count >= MaxObjects || (next.Type == ObjectType.Commit && ++commits > MaxCommits))
                throw new InvalidEnvelopeException("Preview graph exceeds safety limits.");
            visited.Add(next.Hash, (next.Type, next.Size));
            if (context.ObjectStore.Exists(next.Hash)) continue;
            if (next.Type == ObjectType.Blob)
            {
                AddTransfer(next.Size ?? throw new InvalidEnvelopeException("Missing blob size."));
                continue;
            }

            await using var source = await transport.OpenObjectPayloadStreamAsync(next.Hash, cancellationToken).ConfigureAwait(false)
                ?? throw new InvalidEnvelopeException("A remote metadata object required for preview is missing.");
            using var buffer = new MemoryStream();
            VerifiedIncomingObject verified;
            try
            {
                verified = await IncomingObjectVerifier.VerifyAndWriteAsync(source, buffer, next.Hash, "", next.Type, cancellationToken).ConfigureAwait(false);
            }
            catch (JsonException ex) { throw new InvalidEnvelopeException("Invalid preview metadata JSON.", ex); }
            metadataBytes += verified.PayloadLength;
            if (metadataBytes > MaxMetadataBytes) throw new InvalidEnvelopeException("Preview metadata exceeds its aggregate memory budget.");
            AddTransfer(verified.PayloadLength);
            var payload = buffer.GetBuffer().AsMemory(ObjectEnvelopeHeader.HeaderSize, checked((int)verified.PayloadLength));
            try
            {
                if (next.Type == ObjectType.Commit)
                {
                    var commit = ObjectPayloadSerializer.ReadCommitDto(payload.Span)
                        ?? throw new InvalidEnvelopeException("Missing commit metadata.");
                    if (commit.SchemaVersion != 1) throw new UnsupportedEnvelopeVersionException("Unsupported preview commit schema.");
                    var parentIds = (commit.Parents ?? []).Select(ParseCommit).ToArray();
                    edges = checked(edges + parentIds.Length);
                    if (edges > MaxParentEdges) throw new InvalidEnvelopeException("Preview parent graph exceeds its memory budget.");
                    parents.Add(new CommitId(next.Hash), parentIds);
                    foreach (var parent in parentIds) pending.Enqueue((parent.Value, ObjectType.Commit, null));
                    pending.Enqueue((ParseHash(commit.SnapshotId), ObjectType.Snapshot, null));
                }
                else
                {
                    var snapshot = ObjectPayloadSerializer.ReadSnapshotDto(payload.Span)
                        ?? throw new InvalidEnvelopeException("Missing snapshot metadata.");
                    if (snapshot.SchemaVersion != 1) throw new UnsupportedEnvelopeVersionException("Unsupported preview snapshot schema.");
                    if (snapshot.Entries is null) throw new InvalidEnvelopeException("Missing snapshot entries.");
                    foreach (var entry in snapshot.Entries)
                    {
                        var hash = ParseHash(entry.Hash);
                        if (entry.Size < 0 || ParseHash(entry.BlobId) != hash) throw new InvalidEnvelopeException("Invalid snapshot blob reference.");
                        pending.Enqueue((hash, ObjectType.Blob, entry.Size));
                    }
                }
            }
            catch (JsonException ex) { throw new InvalidEnvelopeException("Invalid preview metadata JSON.", ex); }
        }
        var updates = refs.Select(head => new UpdatedRemoteRef(ParseBranch(head.Name),
            context.GetRemoteTrackingCommit(remote, ParseBranch(head.Name)), head.HeadCommit))
            .Where(update => update.OldCommit != update.NewCommit).ToArray();
        return new FetchResult(remote, updates, objects, bytes, true) { Preview = new FetchPreview(refs, parents) };

        void AddTransfer(long size)
        {
            if (size < 0 || size > long.MaxValue - bytes) throw new InvalidEnvelopeException("Preview byte estimate exceeds supported limits.");
            bytes += size;
            objects++;
        }
    }

    internal static BranchName ParseBranch(string name) => BranchName.Parse(
        name.StartsWith("refs/heads/", StringComparison.Ordinal) ? name["refs/heads/".Length..] : name);
    private static ContentHash ParseHash(string text) => ContentHash.TryParse(text, out var hash)
        ? hash : throw new InvalidEnvelopeException("Invalid hash reference in preview metadata.");
    private static CommitId ParseCommit(string text) => new(ParseHash(text));
}
