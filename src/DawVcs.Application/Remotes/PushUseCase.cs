using DawVcs.Application.Common;
using DawVcs.Domain.Common;
using DawVcs.Domain.Configuration;
using DawVcs.Domain.Hashing;
using DawVcs.Domain.Remotes;
using DawVcs.Domain.Repositories;
using DawVcs.Domain.Storage;

namespace DawVcs.Application.Remotes;

public sealed record PushRequest(string RepositoryDirectory, string? RemoteName = null, string? Branch = null, bool DryRun = false);
public sealed record PushResult(string RemoteName, BranchName LocalBranch, BranchName RemoteBranch,
    CommitId? OldCommit, CommitId NewCommit, int ObjectsTransferred, long BytesTransferred, bool IsDryRun, bool UpToDate);

public sealed class PushUseCase(Func<string, IRepositoryContext> contexts, IRemoteTransportFactory transports)
{
    public Task<PushResult> ExecuteAsync(PushRequest request, CancellationToken cancellationToken = default)
        => ExecuteAsync(request, cancellationToken, null);

    public async Task<PushResult> ExecuteAsync(PushRequest request, CancellationToken cancellationToken, IProgress<OperationProgress>? progress)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        progress?.Report(new OperationProgress(OperationStage.OpeningRepository));
        using var context = contexts(request.RepositoryDirectory);
        var config = context.LoadConfig();
        var localBranch = request.Branch is null ? context.GetCurrentBranch() : BranchName.Parse(request.Branch);
        var tip = context.GetBranchCommit(localBranch) ?? throw new ArgumentException("Local branch does not exist or has no commits.", nameof(request));
        var upstream = context.GetUpstream(localBranch);
        var remoteName = request.RemoteName ?? upstream.Remote ?? "origin";
        var remoteBranch = request.Branch is null && request.RemoteName is null
            ? upstream.RemoteBranch ?? localBranch : localBranch;
        var remote = context.GetRemote(remoteName) ?? throw new RemoteException($"No remote named '{remoteName}' is configured.");
        progress?.Report(new OperationProgress(OperationStage.ConnectingRemote));
        using var transport = transports.CreateTransport(remote.Url);
        if (transport is not IRemotePushTransport writer) throw new PushRejectedException("This remote transport does not support push.");
        var handshake = await transport.HandshakeAsync(cancellationToken).ConfigureAwait(false);
        if (handshake.RepositoryId != config.RepositoryId || handshake.SchemaVersion != RepositoryConfig.CurrentSchemaVersion)
            throw new IncompatibleRemoteRepositoryException("Push requires matching repository identity and supported schema.");
        progress?.Report(new OperationProgress(OperationStage.ReadingRemoteReferences));
        var refs = await transport.ListRefsAsync(cancellationToken).ConfigureAwait(false);
        var oldTip = refs.FirstOrDefault(r => string.Equals(r.Name, remoteBranch.Value, StringComparison.Ordinal))?.HeadCommit;
        await writer.ValidatePushAsync(remoteBranch, oldTip, cancellationToken).ConfigureAwait(false);
        progress?.Report(new OperationProgress(OperationStage.TraversingHistory));
        var closure = await PushClosure.ReadAsync(tip, async (hash, type, ct) =>
        {
            if (!context.ObjectStore.Exists(hash)) throw new InvalidEnvelopeException("Local push closure contains a missing object.");
            var header = await context.ObjectStore.ReadHeaderAsync(hash, ct).ConfigureAwait(false);
            if (header.ObjectType != type || (type != ObjectType.Blob && header.PayloadLength > (ulong)IncomingObjectVerifier.MaxMetadataPayloadBytes))
                throw new InvalidEnvelopeException("Invalid local push object type or metadata size.");
            await context.ObjectStore.VerifyObjectIntegrityAsync(hash, ct).ConfigureAwait(false);
            var bytes = type == ObjectType.Blob ? null : await context.ObjectStore.ReadObjectPayloadAsync(hash, ct).ConfigureAwait(false);
            return new PushObject(header, bytes);
        }, cancellationToken).ConfigureAwait(false);
        if (oldTip.HasValue && !closure.Commits.Contains(oldTip.Value.Value))
            throw new PushNonFastForwardException(remoteName, remoteBranch.Value);

        var missing = 0;
        long bytesToSend = 0;
        foreach (var header in closure.Objects)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await using var existing = await transport.OpenObjectPayloadStreamAsync(header.PayloadHash, cancellationToken).ConfigureAwait(false);
            if (existing is null)
            {
                missing++;
                bytesToSend = checked(bytesToSend + (long)header.PayloadLength);
                continue;
            }
            var verified = await IncomingObjectVerifier.VerifyAndWriteAsync(existing, Stream.Null, header.PayloadHash, "", header.ObjectType, cancellationToken).ConfigureAwait(false);
            if ((ulong)verified.PayloadLength != header.PayloadLength || await existing.ReadAsync(new byte[1], cancellationToken).ConfigureAwait(false) != 0)
                throw new InvalidEnvelopeException("Destination object length mismatch.");
        }
        if (request.DryRun)
        {
            await writer.ValidatePushAsync(remoteBranch, oldTip, cancellationToken).ConfigureAwait(false);
            progress?.Report(new OperationProgress(OperationStage.Completed));
            return new PushResult(remoteName, localBranch, remoteBranch, oldTip, tip, missing, bytesToSend, true, oldTip == tip && missing == 0);
        }
        using var session = await writer.BeginPushAsync(remoteBranch, oldTip, cancellationToken).ConfigureAwait(false);
        var count = 0;
        long transferred = 0;
        progress?.Report(new OperationProgress(OperationStage.TransferringObjects));
        foreach (var header in closure.Objects)
        {
            await using var payload = await context.ObjectStore.OpenPayloadStreamAsync(header.PayloadHash, cancellationToken).ConfigureAwait(false);
            if (await session.ReceiveObjectAsync(header, payload, cancellationToken).ConfigureAwait(false))
            {
                count++;
                transferred = checked(transferred + (long)header.PayloadLength);
            }
            progress?.Report(new OperationProgress(OperationStage.TransferringObjects, transferred, unit: OperationProgressUnit.Bytes));
        }
        progress?.Report(new OperationProgress(OperationStage.UpdatingReferences));
        await session.PublishAsync(tip, cancellationToken).ConfigureAwait(false);
        progress?.Report(new OperationProgress(OperationStage.Completed));
        return new PushResult(remoteName, localBranch, remoteBranch, oldTip, tip, count, transferred, false, oldTip == tip && count == 0);
    }
}
