using DawVcs.Application.Checkouts;
using DawVcs.Domain.Common;
using DawVcs.Domain.Hashing;
using DawVcs.Domain.Repositories;

namespace DawVcs.Application.Branches;

public sealed record SwitchRequest(
    string RepositoryDirectory,
    string BranchName,
    bool CreateBranch = false,
    string? StartPoint = null,
    bool Force = false);

public sealed record SwitchResult(
    BranchName Branch,
    CommitId? CommitId,
    string? RecoveryDirectory = null,
    int RestoredFilesCount = 0,
    bool CreatedNewBranch = false,
    bool AlreadyOnBranch = false);

/// <summary>
/// Beheert het overschakelen tussen branches met dirty workspace beveiliging en optionele recoverykopie (FR-BRA-004..007).
/// </summary>
public sealed class SwitchUseCase
{
    private readonly Func<string, IRepositoryContext> _contextFactory;

    public SwitchUseCase(Func<string, IRepositoryContext> contextFactory)
    {
        _contextFactory = contextFactory ?? throw new ArgumentNullException(nameof(contextFactory));
    }

    public async Task<SwitchResult> ExecuteAsync(SwitchRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.RepositoryDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.BranchName);

        if (!BranchName.TryCreate(request.BranchName, out var targetBranch, out var validationError))
        {
            throw new ArgumentException(validationError ?? $"Invalid branch name '{request.BranchName}'.", nameof(request));
        }

        var context = _contextFactory(request.RepositoryDirectory);
        var currentBranch = context.GetCurrentBranch();

        if (!request.CreateBranch && targetBranch == currentBranch)
        {
            var currentCommit = context.GetBranchCommit(currentBranch);
            return new SwitchResult(targetBranch, currentCommit, AlreadyOnBranch: true);
        }

        var branches = context.GetBranches();
        var existingBranch = branches.FirstOrDefault(b => b.Name == targetBranch);

        bool createdNewBranch = false;
        if (request.CreateBranch)
        {
            if (existingBranch != null)
            {
                throw new InvalidOperationException($"Branch '{targetBranch.Value}' already exists.");
            }

            CommitId? startCommit = null;
            if (!string.IsNullOrWhiteSpace(request.StartPoint))
            {
                startCommit = context.ResolveReference(request.StartPoint)
                    ?? throw new InvalidOperationException($"Start point '{request.StartPoint}' could not be resolved to a commit.");
            }
            else
            {
                startCommit = context.GetBranchCommit(currentBranch);
            }

            if (startCommit.HasValue)
            {
                context.CreateBranch(targetBranch, startCommit.Value);
            }

            createdNewBranch = true;
        }
        else if (existingBranch == null)
        {
            throw new InvalidOperationException($"Branch '{targetBranch.Value}' does not exist.");
        }

        var targetCommit = context.GetBranchCommit(targetBranch);
        CheckoutResult? checkoutResult = null;

        if (targetCommit.HasValue)
        {
            // Checkout voert dirty-workspacecontrole uit; gooit CheckoutStagingException bij onopgeslagen wijzigingen zonder --force
            var checkoutUseCase = new CheckoutUseCase(_contextFactory);
            var checkoutRequest = new CheckoutRequest(
                request.RepositoryDirectory,
                targetCommit.Value.ToString(),
                Force: request.Force);

            checkoutResult = await checkoutUseCase.ExecuteAsync(checkoutRequest, cancellationToken).ConfigureAwait(false);
        }

        // Update HEAD naar de nieuwe branch
        context.SetCurrentBranch(targetBranch);

        return new SwitchResult(
            Branch: targetBranch,
            CommitId: targetCommit,
            RecoveryDirectory: checkoutResult?.RecoveryDirectory,
            RestoredFilesCount: checkoutResult?.RestoredFiles.Count ?? 0,
            CreatedNewBranch: createdNewBranch,
            AlreadyOnBranch: false);
    }
}
