using DawVcs.Domain.Common;
using DawVcs.Domain.Hashing;
using DawVcs.Domain.Repositories;

namespace DawVcs.Application.Branches;

public sealed record BranchCreateRequest(
    string RepositoryDirectory,
    string BranchName,
    string? StartPoint = null);

public sealed record BranchDeleteRequest(
    string RepositoryDirectory,
    string BranchName,
    bool Force = false);

/// <summary>
/// Beheert het aanmaken, listen en verwijderen van branches (FR-BRA-001..003).
/// </summary>
public sealed class BranchUseCase
{
    private readonly Func<string, IRepositoryContext> _contextFactory;

    public BranchUseCase(Func<string, IRepositoryContext> contextFactory)
    {
        _contextFactory = contextFactory ?? throw new ArgumentNullException(nameof(contextFactory));
    }

    public Task<IReadOnlyList<BranchInfo>> ListAsync(string repositoryDirectory, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryDirectory);
        cancellationToken.ThrowIfCancellationRequested();

        var context = _contextFactory(repositoryDirectory);
        var branches = context.GetBranches();
        return Task.FromResult(branches);
    }

    public Task<BranchInfo> CreateAsync(BranchCreateRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.RepositoryDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.BranchName);
        cancellationToken.ThrowIfCancellationRequested();

        if (!BranchName.TryCreate(request.BranchName, out var branchName, out var validationError))
        {
            throw new ArgumentException(validationError ?? $"Invalid branch name '{request.BranchName}'.", nameof(request));
        }

        var context = _contextFactory(request.RepositoryDirectory);

        CommitId commitId;
        if (!string.IsNullOrWhiteSpace(request.StartPoint))
        {
            var resolved = context.ResolveReference(request.StartPoint)
                ?? throw new InvalidOperationException($"Start point '{request.StartPoint}' could not be resolved to a commit.");
            commitId = resolved;
        }
        else
        {
            var currentBranch = context.GetCurrentBranch();
            var headCommit = context.GetBranchCommit(currentBranch)
                ?? throw new InvalidOperationException($"Cannot create branch '{branchName.Value}' because the current branch '{currentBranch.Value}' has no commits.");
            commitId = headCommit;
        }

        context.CreateBranch(branchName, commitId);

        var current = context.GetCurrentBranch();
        var branchInfo = new BranchInfo(branchName, commitId, branchName == current);
        return Task.FromResult(branchInfo);
    }

    public Task<bool> DeleteAsync(BranchDeleteRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.RepositoryDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.BranchName);
        cancellationToken.ThrowIfCancellationRequested();

        if (!BranchName.TryCreate(request.BranchName, out var branchName, out var validationError))
        {
            throw new ArgumentException(validationError ?? $"Invalid branch name '{request.BranchName}'.", nameof(request));
        }

        var context = _contextFactory(request.RepositoryDirectory);
        var currentBranch = context.GetCurrentBranch();

        if (branchName == currentBranch)
        {
            throw new InvalidOperationException($"Cannot delete the currently active branch '{branchName.Value}'.");
        }

        var deleted = context.DeleteBranch(branchName);
        return Task.FromResult(deleted);
    }
}
