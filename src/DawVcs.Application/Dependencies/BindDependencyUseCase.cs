using DawVcs.Application.Common;
using DawVcs.Domain.Dependencies;
using DawVcs.Domain.Hashing;
using DawVcs.Domain.Repositories;

namespace DawVcs.Application.Dependencies;

public sealed record BindDependencyRequest(
    string RepositoryDirectory,
    string DependencyId,
    string LocalPath);

public sealed record BindDependencyResult(
    DependencyBinding Binding);

/// <summary>
/// Verwerkt handmatige dependencykoppeling door de gebruiker ('dawvc bind', FR-BND-006, IMP-0704).
/// Valideert het lokale pad, controleert de hash tegen de verwachting en slaat de binding lokaal op.
/// </summary>
public sealed class BindDependencyUseCase : IUseCase<BindDependencyRequest, BindDependencyResult>
{
    private readonly Func<string, IRepositoryContext> _contextFactory;

    public BindDependencyUseCase(Func<string, IRepositoryContext> contextFactory)
    {
        _contextFactory = contextFactory ?? throw new ArgumentNullException(nameof(contextFactory));
    }

    public async Task<BindDependencyResult> ExecuteAsync(BindDependencyRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.RepositoryDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.DependencyId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.LocalPath);

        var context = _contextFactory(request.RepositoryDirectory);
        var depId = new DependencyId(request.DependencyId);
        var fullPath = Path.GetFullPath(request.LocalPath);

        // 1. Zoek dependency in HEAD snapshot
        var headBranch = context.GetCurrentBranch();
        var headCommitId = context.GetBranchCommit(headBranch);
        ContentHash? expectedHash = null;

        if (headCommitId.HasValue)
        {
            var headCommit = await context.LoadCommitAsync(headCommitId.Value, cancellationToken).ConfigureAwait(false);
            if (headCommit is not null)
            {
                var snapshot = await context.LoadSnapshotAsync(headCommit.SnapshotId, cancellationToken).ConfigureAwait(false);
                if (snapshot is not null)
                {
                    var dep = snapshot.Dependencies.All.FirstOrDefault(d => d.Id == depId);
                    if (dep is AssetDependency asset)
                    {
                        expectedHash = asset.Hash;
                    }
                }
            }
        }

        // 2. Bepaal status op basis van bestandsexistentie en hashverificatie
        BindingStatus status;
        ContentHash? actualHash = null;

        if (!File.Exists(fullPath))
        {
            status = BindingStatus.Missing;
        }
        else if (expectedHash.HasValue)
        {
            actualHash = await Blake3ContentHasher.HashFileAsync(fullPath, cancellationToken).ConfigureAwait(false);
            status = (actualHash == expectedHash.Value)
                ? BindingStatus.Verified
                : BindingStatus.Mismatch;
        }
        else
        {
            // Geen hashverwachting bekend (bijv. plugin of directory)
            status = BindingStatus.Verified;
        }

        var binding = new DependencyBinding(
            depId,
            fullPath,
            BindingMethod.UserSelection,
            status,
            actualHash,
            DateTimeOffset.UtcNow);

        // 3. Sla lokaal op (buiten commits)
        await context.LocalBindings.SaveAsync(binding, cancellationToken).ConfigureAwait(false);

        return new BindDependencyResult(binding);
    }
}
