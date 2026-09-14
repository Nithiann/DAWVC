using DawVcs.Domain.Common;
using DawVcs.Domain.Configuration;
using DawVcs.Domain.Dependencies;
using DawVcs.Domain.Entities;
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
}
