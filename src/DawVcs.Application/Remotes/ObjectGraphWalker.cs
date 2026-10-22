using DawVcs.Domain.Common;
using DawVcs.Domain.Hashing;
using DawVcs.Domain.Remotes;
using DawVcs.Domain.Repositories;
using DawVcs.Domain.Serialization;
using DawVcs.Domain.Storage;

namespace DawVcs.Application.Remotes;

public sealed record MissingBlobInfo(ContentHash Hash, long Size, string Path);

public sealed record GraphWalkResult(
    IReadOnlyList<VerifiedIncomingObject> StagedMetadataObjects,
    IReadOnlyList<MissingBlobInfo> MissingBlobs,
    int TotalObjectsToFetch,
    long TotalBytesToFetch);

/// <summary>
/// Walks remote commit and snapshot graphs to compute the minimal missing object closure (FR-FET-002).
/// Stops traversal along any branch where commits already exist in the local object store.
/// </summary>
public sealed class ObjectGraphWalker
{
    private readonly int _maxDepth;

    public ObjectGraphWalker(int maxDepth = 50_000)
    {
        _maxDepth = maxDepth;
    }

    public async Task<GraphWalkResult> WalkClosureAsync(
        IRemoteTransport transport,
        IRepositoryContext context,
        IQuarantineStore quarantineStore,
        IEnumerable<CommitId> remoteTips,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(transport);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(quarantineStore);
        ArgumentNullException.ThrowIfNull(remoteTips);

        var visited = new HashSet<ContentHash>();
        var stagedMetadata = new List<VerifiedIncomingObject>();
        var missingBlobs = new List<MissingBlobInfo>();
        var commitQueue = new Queue<CommitId>();

        foreach (var tip in remoteTips)
        {
            if (visited.Add(tip.Value))
            {
                commitQueue.Enqueue(tip);
            }
        }

        var commitsProcessed = 0;

        while (commitQueue.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var commitId = commitQueue.Dequeue();

            // FR-FET-002: If commit already exists locally, stop traversal along this branch
            if (context.ObjectStore.Exists(commitId.Value))
            {
                continue;
            }

            if (++commitsProcessed > _maxDepth)
            {
                throw new InvalidOperationException($"Commit graph traversal exceeded safety limit of {_maxDepth} commits.");
            }

            // Stream and verify commit from remote into quarantine
            var commitQuarantinePath = quarantineStore.GetIncomingPath(commitId.Value);
            VerifiedIncomingObject verifiedCommit;

            if (File.Exists(commitQuarantinePath))
            {
                var fileInfo = new FileInfo(commitQuarantinePath);
                verifiedCommit = new VerifiedIncomingObject(commitId.Value, ObjectType.Commit, fileInfo.Length - ObjectEnvelopeHeader.HeaderSize, commitQuarantinePath);
            }
            else
            {
                await using var remoteStream = await transport.OpenObjectPayloadStreamAsync(commitId.Value, cancellationToken).ConfigureAwait(false)
                    ?? throw new RemoteException($"Commit object '{commitId}' not found on remote repository '{transport.RemoteUrl}'.");

                await using var quarantineStream = await quarantineStore.CreateIncomingWriteStreamAsync(commitId.Value, cancellationToken).ConfigureAwait(false);

                verifiedCommit = await IncomingObjectVerifier.VerifyAndWriteAsync(
                    remoteStream,
                    quarantineStream,
                    commitId.Value,
                    commitQuarantinePath,
                    ObjectType.Commit,
                    cancellationToken).ConfigureAwait(false);
            }

            stagedMetadata.Add(verifiedCommit);

            // Read commit payload to get Parents and SnapshotId
            byte[] commitPayload = await ReadPayloadFromQuarantineAsync(commitQuarantinePath, verifiedCommit.PayloadLength, cancellationToken).ConfigureAwait(false);
            var commitDto = ObjectPayloadSerializer.ReadCommitDto(commitPayload)
                ?? throw new InvalidEnvelopeException($"Failed to deserialize commit payload '{commitId}'.");

            // Enqueue parents
            if (commitDto.Parents is not null)
            {
                foreach (var parentHex in commitDto.Parents)
                {
                    if (CommitId.TryParse(parentHex, out var parentId))
                    {
                        if (visited.Add(parentId.Value))
                        {
                            commitQueue.Enqueue(parentId);
                        }
                    }
                }
            }

            // Inspect Snapshot
            if (SnapshotId.TryParse(commitDto.SnapshotId, out var snapshotId))
            {
                if (visited.Add(snapshotId.Value))
                {
                    await ProcessSnapshotAsync(
                        transport,
                        context,
                        quarantineStore,
                        snapshotId,
                        stagedMetadata,
                        missingBlobs,
                        visited,
                        cancellationToken).ConfigureAwait(false);
                }
            }
        }

        var totalObjects = stagedMetadata.Count + missingBlobs.Count;
        var totalBytes = stagedMetadata.Sum(m => m.PayloadLength) + missingBlobs.Sum(b => b.Size);

        return new GraphWalkResult(stagedMetadata, missingBlobs, totalObjects, totalBytes);
    }

    private static async Task ProcessSnapshotAsync(
        IRemoteTransport transport,
        IRepositoryContext context,
        IQuarantineStore quarantineStore,
        SnapshotId snapshotId,
        List<VerifiedIncomingObject> stagedMetadata,
        List<MissingBlobInfo> missingBlobs,
        HashSet<ContentHash> visited,
        CancellationToken cancellationToken)
    {
        if (context.ObjectStore.Exists(snapshotId.Value))
        {
            return;
        }

        var snapshotQuarantinePath = quarantineStore.GetIncomingPath(snapshotId.Value);
        VerifiedIncomingObject verifiedSnapshot;

        if (File.Exists(snapshotQuarantinePath))
        {
            var fileInfo = new FileInfo(snapshotQuarantinePath);
            verifiedSnapshot = new VerifiedIncomingObject(snapshotId.Value, ObjectType.Snapshot, fileInfo.Length - ObjectEnvelopeHeader.HeaderSize, snapshotQuarantinePath);
        }
        else
        {
            await using var remoteStream = await transport.OpenObjectPayloadStreamAsync(snapshotId.Value, cancellationToken).ConfigureAwait(false)
                ?? throw new RemoteException($"Snapshot object '{snapshotId}' not found on remote repository '{transport.RemoteUrl}'.");

            await using var quarantineStream = await quarantineStore.CreateIncomingWriteStreamAsync(snapshotId.Value, cancellationToken).ConfigureAwait(false);

            verifiedSnapshot = await IncomingObjectVerifier.VerifyAndWriteAsync(
                remoteStream,
                quarantineStream,
                snapshotId.Value,
                snapshotQuarantinePath,
                ObjectType.Snapshot,
                cancellationToken).ConfigureAwait(false);
        }

        stagedMetadata.Add(verifiedSnapshot);

        byte[] snapshotPayload = await ReadPayloadFromQuarantineAsync(snapshotQuarantinePath, verifiedSnapshot.PayloadLength, cancellationToken).ConfigureAwait(false);
        var snapshotDto = ObjectPayloadSerializer.ReadSnapshotDto(snapshotPayload)
            ?? throw new InvalidEnvelopeException($"Failed to deserialize snapshot payload '{snapshotId}'.");

        if (snapshotDto.Entries is not null)
        {
            foreach (var entry in snapshotDto.Entries)
            {
                if (ContentHash.TryParse(entry.Hash, out var blobHash))
                {
                    if (visited.Add(blobHash))
                    {
                        if (!context.ObjectStore.Exists(blobHash))
                        {
                            missingBlobs.Add(new MissingBlobInfo(blobHash, entry.Size, entry.Path));
                        }
                    }
                }
            }
        }
    }

    private static async Task<byte[]> ReadPayloadFromQuarantineAsync(string filePath, long payloadLength, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, useAsync: true);
        stream.Seek(ObjectEnvelopeHeader.HeaderSize, SeekOrigin.Begin);

        var payload = new byte[payloadLength];
        var read = 0;
        while (read < payloadLength)
        {
            var count = await stream.ReadAsync(payload.AsMemory(read, (int)(payloadLength - read)), cancellationToken).ConfigureAwait(false);
            if (count == 0) break;
            read += count;
        }

        return payload;
    }
}
