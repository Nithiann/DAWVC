using DawVcs.Domain.Common;

namespace DawVcs.Domain.Remotes;

/// <summary>
/// Thrown when a pull cannot be completed because local and remote branch histories have diverged (Exit Code 9).
/// Satisfies FR-PUL-003, AC-033, and fast-forward safety invariants.
/// </summary>
public sealed class RejectedNonFastForwardException : Exception
{
    public string RemoteName { get; }
    public string BranchName { get; }
    public CommitId LocalCommit { get; }
    public CommitId RemoteCommit { get; }

    public RejectedNonFastForwardException(string remoteName, string branchName, CommitId localCommit, CommitId remoteCommit)
        : base($"Cannot pull from remote '{remoteName}': branch '{branchName}' has diverged (local: {localCommit.ToString()[..8]}, remote: {remoteCommit.ToString()[..8]}). Fast-forward only is supported in v0.2. To integrate changes, create a new branch from '{remoteName}/{branchName}' or rebase/merge manually.")
    {
        RemoteName = remoteName;
        BranchName = branchName;
        LocalCommit = localCommit;
        RemoteCommit = remoteCommit;
    }
}
