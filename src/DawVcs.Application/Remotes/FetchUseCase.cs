using DawVcs.Domain.Common;
using DawVcs.Domain.Configuration;
using DawVcs.Domain.Hashing;
using DawVcs.Domain.Remotes;
using DawVcs.Domain.Repositories;
using DawVcs.Domain.Storage;

namespace DawVcs.Application.Remotes;

public sealed record FetchRequest(
    string RepositoryDirectory,
    string? RemoteName = null,
    string? Branch = null,
    bool DryRun = false);

public sealed record UpdatedRemoteRef(
    BranchName Branch,
    CommitId? OldCommit,
    CommitId NewCommit);

public sealed record FetchResult(
    string RemoteName,
    IReadOnlyList<UpdatedRemoteRef> UpdatedRefs,
    int ObjectsFetched,
    long BytesFetched,
    bool IsDryRun);

/// <summary>
/// Fetches commits, snapshots, and blobs from a remote repository into quarantine,
/// verifies integrity, promotes objects, and updates remote-tracking refs (FR-FET-001..008).
/// </summary>
public class FetchUseCase
{
    private readonly Func<string, IRepositoryContext> _contextFactory;
    private readonly IRemoteTransportFactory _transportFactory;
    private readonly Func<string, IQuarantineStore> _quarantineStoreFactory;
    private readonly ObjectGraphWalker _graphWalker;

    public FetchUseCase(
        Func<string, IRepositoryContext> contextFactory,
        IRemoteTransportFactory transportFactory,
        Func<string, IQuarantineStore> quarantineStoreFactory,
        ObjectGraphWalker? graphWalker = null)
    {
        _contextFactory = contextFactory ?? throw new ArgumentNullException(nameof(contextFactory));
        _transportFactory = transportFactory ?? throw new ArgumentNullException(nameof(transportFactory));
        _quarantineStoreFactory = quarantineStoreFactory ?? throw new ArgumentNullException(nameof(quarantineStoreFactory));
        _graphWalker = graphWalker ?? new ObjectGraphWalker();
    }

    public virtual async Task<FetchResult> ExecuteAsync(FetchRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var context = _contextFactory(request.RepositoryDirectory);
        var localConfig = context.LoadConfig();

        var remoteNameStr = !string.IsNullOrWhiteSpace(request.RemoteName) ? request.RemoteName : "origin";
        var remoteConfig = context.GetRemote(remoteNameStr)
            ?? throw new RemoteException($"No remote named '{remoteNameStr}' is configured. Use 'dawvc remote add {remoteNameStr} <url>' first.");

        using var transport = _transportFactory.CreateTransport(remoteConfig.Url);

        // 1. Handshake & Repository Identity Verification (FR-FET-001)
        var handshake = await transport.HandshakeAsync(cancellationToken).ConfigureAwait(false);
        if (handshake.SchemaVersion > RepositoryConfig.CurrentSchemaVersion)
        {
            throw new RemoteException($"Remote schemaVersion '{handshake.SchemaVersion}' is newer than supported version '{RepositoryConfig.CurrentSchemaVersion}'.");
        }

        if (handshake.RepositoryId != localConfig.RepositoryId)
        {
            throw new RemoteException($"Remote repositoryId '{handshake.RepositoryId}' does not match local repositoryId '{localConfig.RepositoryId}'.");
        }

        // 2. Enumerate remote refs
        var allRemoteRefs = await transport.ListRefsAsync(cancellationToken).ConfigureAwait(false);
        var targetRefs = new List<RemoteRef>();

        if (!string.IsNullOrWhiteSpace(request.Branch))
        {
            var requestedBranch = request.Branch.Trim();
            var matched = allRemoteRefs.FirstOrDefault(r =>
                r.Name.Equals(requestedBranch, StringComparison.OrdinalIgnoreCase) ||
                r.Name.Equals($"refs/heads/{requestedBranch}", StringComparison.OrdinalIgnoreCase));

            if (matched is null)
            {
                throw new RemoteException($"Remote branch '{requestedBranch}' not found on remote '{remoteNameStr}'.");
            }

            targetRefs.Add(matched);
        }
        else
        {
            targetRefs.AddRange(allRemoteRefs);
        }

        using var quarantineStore = _quarantineStoreFactory(request.RepositoryDirectory);
        quarantineStore.PruneStaleDirectories(TimeSpan.FromHours(24));

        // 3. Compute missing object closure via ObjectGraphWalker (FR-FET-002)
        var remoteTips = targetRefs.Select(r => r.HeadCommit).Distinct().ToList();
        var walkResult = await _graphWalker.WalkClosureAsync(
            transport,
            context,
            quarantineStore,
            remoteTips,
            cancellationToken).ConfigureAwait(false);

        // 4. Handle Dry Run
        if (request.DryRun)
        {
            quarantineStore.DiscardSession();
            return new FetchResult(
                remoteNameStr,
                Array.Empty<UpdatedRemoteRef>(),
                walkResult.TotalObjectsToFetch,
                walkResult.TotalBytesToFetch,
                IsDryRun: true);
        }

        // 5. Stream and verify missing blobs into quarantine, promoting them immediately for resume capability
        var verifiedBlobs = new List<VerifiedIncomingObject>();
        foreach (var blob in walkResult.MissingBlobs)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (context.ObjectStore.Exists(blob.Hash))
            {
                continue;
            }

            var incomingPath = quarantineStore.GetIncomingPath(blob.Hash);
            VerifiedIncomingObject verifiedBlob;
            await using (var remoteStream = await transport.OpenObjectPayloadStreamAsync(blob.Hash, cancellationToken).ConfigureAwait(false)
                ?? throw new RemoteException($"Blob '{blob.Hash}' for path '{blob.Path}' not found on remote '{transport.RemoteUrl}'."))
            {
                await using (var quarantineStream = await quarantineStore.CreateIncomingWriteStreamAsync(blob.Hash, cancellationToken).ConfigureAwait(false))
                {
                    verifiedBlob = await IncomingObjectVerifier.VerifyAndWriteAsync(
                        remoteStream,
                        quarantineStream,
                        blob.Hash,
                        incomingPath,
                        ObjectType.Blob,
                        cancellationToken).ConfigureAwait(false);
                }
            }

            verifiedBlobs.Add(verifiedBlob);

            // Promote verified blob immediately so interrupted fetches don't re-download it
            await quarantineStore.PromoteAsync(verifiedBlob, cancellationToken).ConfigureAwait(false);
        }

        // 6. Promote metadata objects (Snapshots, then Commits)
        await quarantineStore.PromoteAllAsync(walkResult.StagedMetadataObjects, cancellationToken).ConfigureAwait(false);

        // 7. Update remote-tracking refs (FR-FET-006)
        var updatedRefs = new List<UpdatedRemoteRef>();
        foreach (var rRef in targetRefs)
        {
            var branchClean = rRef.Name.StartsWith("refs/heads/", StringComparison.OrdinalIgnoreCase)
                ? rRef.Name["refs/heads/".Length..]
                : rRef.Name;

            if (BranchName.TryCreate(branchClean, out var branchName, out _))
            {
                var oldCommit = context.GetRemoteTrackingCommit(remoteNameStr, branchName);
                if (oldCommit != rRef.HeadCommit)
                {
                    await context.UpdateRemoteTrackingCommitAsync(remoteNameStr, branchName, rRef.HeadCommit, cancellationToken).ConfigureAwait(false);
                    updatedRefs.Add(new UpdatedRemoteRef(branchName, oldCommit, rRef.HeadCommit));
                }
            }
        }

        return new FetchResult(
            remoteNameStr,
            updatedRefs,
            walkResult.TotalObjectsToFetch,
            walkResult.TotalBytesToFetch,
            IsDryRun: false);
    }
}
