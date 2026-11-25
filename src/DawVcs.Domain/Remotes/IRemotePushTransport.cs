using DawVcs.Domain.Common;
using DawVcs.Domain.Hashing;
using DawVcs.Domain.Storage;

namespace DawVcs.Domain.Remotes;

/// <summary>Optional receiving capability; read-only transports remain unchanged.</summary>
public interface IRemotePushTransport
{
    Task ValidatePushAsync(BranchName branch, CommitId? expectedTip, CancellationToken cancellationToken = default);
    Task<IRemotePushSession> BeginPushAsync(BranchName branch, CommitId? expectedTip, CancellationToken cancellationToken = default);
}

/// <summary>Caller-owned reception session. Dispose after awaiting all operations.</summary>
public interface IRemotePushSession : IDisposable
{
    /// <returns>True if a missing object was received, false if an existing object was verified.</returns>
    Task<bool> ReceiveObjectAsync(ObjectEnvelopeHeader header, Stream payload, CancellationToken cancellationToken = default);
    Task PublishAsync(CommitId newTip, CancellationToken cancellationToken = default);
}

public sealed class PushRejectedException(string message) : RemoteException(message);

public sealed class PushNonFastForwardException(string remote, string branch)
    : Exception($"Cannot push to '{remote}/{branch}': the destination is not an ancestor of the local branch. Fetch and integrate remote changes first; force-push is not supported.");
