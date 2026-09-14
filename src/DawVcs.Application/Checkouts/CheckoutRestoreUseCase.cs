using DawVcs.Application.Common;
using DawVcs.Domain.Common;
using DawVcs.Domain.Repositories;

namespace DawVcs.Application.Checkouts;

public sealed record CheckoutRestoreRequest(
    string RepositoryDirectory,
    string Reference,
    string RestoreToDirectory);

public sealed record CheckoutRestoreResult(
    CommitId CommitId,
    SnapshotId SnapshotId,
    string TargetDirectory,
    IReadOnlyList<string> RestoredFiles);

/// <summary>
/// Orchestrates safe checkout restoration of snapshot artifacts to an external directory without dirtying the workspace (DEC-MVP-009, AC-001).
/// Delegator to <see cref="CheckoutUseCase"/>.
/// </summary>
public sealed class CheckoutRestoreUseCase : IUseCase<CheckoutRestoreRequest, CheckoutRestoreResult>
{
    private readonly CheckoutUseCase _checkoutUseCase;

    public CheckoutRestoreUseCase(Func<string, IRepositoryContext> contextFactory)
    {
        _checkoutUseCase = new CheckoutUseCase(contextFactory);
    }

    public async Task<CheckoutRestoreResult> ExecuteAsync(CheckoutRestoreRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var result = await _checkoutUseCase.ExecuteAsync(new CheckoutRequest(
            request.RepositoryDirectory,
            request.Reference,
            request.RestoreToDirectory), cancellationToken).ConfigureAwait(false);

        return new CheckoutRestoreResult(
            result.CommitId,
            result.SnapshotId,
            result.TargetDirectory,
            result.RestoredFiles);
    }
}
