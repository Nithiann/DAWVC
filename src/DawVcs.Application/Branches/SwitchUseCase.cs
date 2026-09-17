using DawVcs.Application.Adapters;
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
    private readonly IDawAdapterRegistry? _adapterRegistry;

    public SwitchUseCase(Func<string, IRepositoryContext> contextFactory, IDawAdapterRegistry? adapterRegistry = null)
    {
        _contextFactory = contextFactory ?? throw new ArgumentNullException(nameof(contextFactory));
        _adapterRegistry = adapterRegistry;
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

        // 1. Reeds op de doelbranch?
        if (targetBranch == currentBranch && !request.CreateBranch)
        {
            var headCommit = context.GetBranchCommit(currentBranch);
            return new SwitchResult(
                Branch: currentBranch,
                CommitId: headCommit,
                AlreadyOnBranch: true);
        }

        var branches = context.GetBranches();
        var existingBranch = branches.FirstOrDefault(b => b.Name == targetBranch);

        // 2. Branch aanmaken indien gevraagd (-c / -b)
        var createdNew = false;
        if (request.CreateBranch)
        {
            if (existingBranch != null || context.GetBranchCommit(targetBranch).HasValue)
            {
                throw new InvalidOperationException($"Branch '{targetBranch.Value}' already exists.");
            }

            // Startpunt bepalen: expliciet startpunt of huidige HEAD
            CommitId startingCommit;
            if (!string.IsNullOrWhiteSpace(request.StartPoint))
            {
                startingCommit = context.ResolveReference(request.StartPoint)
                    ?? throw new InvalidOperationException($"Starting point reference '{request.StartPoint}' could not be resolved.");
            }
            else
            {
                startingCommit = context.GetBranchCommit(currentBranch)
                    ?? throw new InvalidOperationException($"Cannot branch from '{currentBranch.Value}': branch has no commits yet.");
            }

            context.CreateBranch(targetBranch, startingCommit);
            createdNew = true;
        }
        else if (existingBranch == null)
        {
            throw new InvalidOperationException($"Branch '{targetBranch.Value}' does not exist.");
        }

        var targetCommit = context.GetBranchCommit(targetBranch);
        CheckoutResult? checkoutResult = null;

        if (targetCommit.HasValue)
        {
            // Checkout voert dirty-workspacecontrole uit; gooit CheckoutStagingException of DirtyWorkspaceException bij onopgeslagen wijzigingen zonder --force
            var checkoutUseCase = new CheckoutUseCase(_contextFactory, _adapterRegistry);
            var checkoutRequest = new CheckoutRequest(
                request.RepositoryDirectory,
                targetBranch.Value,
                Force: request.Force);

            try
            {
                checkoutResult = await checkoutUseCase.ExecuteAsync(checkoutRequest, cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                if (createdNew)
                {
                    try
                    {
                        context.DeleteBranch(targetBranch);
                    }
                    catch
                    {
                        // Best-effort cleanup van niet-voltooide branch
                    }
                }

                throw;
            }
        }
        else
        {
            // Alleen direct HEAD bijwerken als er geen checkout plaatsvond (bijv. weesbranch zonder commits)
            context.SetCurrentBranch(targetBranch);
        }

        return new SwitchResult(
            Branch: targetBranch,
            CommitId: targetCommit,
            RecoveryDirectory: checkoutResult?.RecoveryDirectory,
            RestoredFilesCount: checkoutResult?.RestoredFiles.Count ?? 0,
            CreatedNewBranch: createdNew,
            AlreadyOnBranch: false);
    }
}
