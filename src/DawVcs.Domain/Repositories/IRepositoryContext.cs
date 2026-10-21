using DawVcs.Domain.Common;
using DawVcs.Domain.Configuration;
using DawVcs.Domain.Dependencies;
using DawVcs.Domain.Entities;
using DawVcs.Domain.Remotes;
using DawVcs.Domain.Storage;

namespace DawVcs.Domain.Repositories;

/// <summary>
/// Domain port for interaction with the local repository state, config, and reference pointers.
/// </summary>
public interface IRepositoryContext : IDisposable
{
    string RootPath { get; }
    IObjectStore ObjectStore { get; }
    IStagingIndex StagingIndex { get; }
    ILocalBindingStore LocalBindings { get; }

    RepositoryConfig LoadConfig();
    void SaveConfig(RepositoryConfig config);

    BranchName GetCurrentBranch();
    void SetCurrentBranch(BranchName branch);

    IReadOnlyList<BranchInfo> GetBranches();
    void CreateBranch(BranchName branch, CommitId commitId);
    bool DeleteBranch(BranchName branch);

    CommitId? GetBranchCommit(BranchName branch);
    Task UpdateBranchCommitAsync(BranchName branch, CommitId newCommit, CancellationToken cancellationToken = default);

    CommitId? ResolveReference(string reference);

    Task<Commit?> LoadCommitAsync(CommitId id, CancellationToken cancellationToken = default);
    Task<ProjectSnapshot?> LoadSnapshotAsync(SnapshotId id, CancellationToken cancellationToken = default);

    // Remote operations (FR-REM-001..012, FR-FET-001..008)
    IReadOnlyList<RemoteConfig> GetRemotes() => Array.Empty<RemoteConfig>();
    RemoteConfig? GetRemote(string remoteName) => null;
    void SaveRemote(RemoteConfig remote) { }
    bool RemoveRemote(string remoteName) => false;

    CommitId? GetRemoteTrackingCommit(string remote, BranchName branch) => null;
    Task UpdateRemoteTrackingCommitAsync(string remote, BranchName branch, CommitId commitId, CancellationToken cancellationToken = default) => Task.CompletedTask;

    (string? Remote, BranchName? RemoteBranch) GetUpstream(BranchName branch) => (null, null);
    void SetUpstream(BranchName branch, string remote, BranchName remoteBranch) { }
}
