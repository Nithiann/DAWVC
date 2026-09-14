using DawVcs.Application.Common;
using DawVcs.Application.Dependencies;
using DawVcs.Application.Exceptions;
using DawVcs.Application.Scanning;
using DawVcs.Domain.Artifacts;
using DawVcs.Domain.Common;
using DawVcs.Domain.Dependencies;
using DawVcs.Domain.Hashing;
using DawVcs.Domain.Repositories;

namespace DawVcs.Application.Checkouts;

public sealed record CheckoutRequest(
    string RepositoryDirectory,
    string Reference,
    string? RestoreToDirectory = null,
    bool Force = false);

public sealed record CheckoutResult(
    CommitId CommitId,
    SnapshotId SnapshotId,
    string TargetDirectory,
    IReadOnlyList<string> RestoredFiles,
    string? RecoveryDirectory = null,
    string? ManagedAssetRoot = null,
    IReadOnlyList<string>? ManualInstructions = null,
    IReadOnlyList<DependencyBinding>? ResolvedBindings = null);

/// <summary>
/// Beheert veilige, getrapte (staged) checkout en herstel van snapshots (FR-CHK-001..015, AC-009..013).
/// Past dirty-workspacebeveiliging toe, maakt een recoverykopie bij --force, en garandeert atomiciteit.
/// </summary>
public sealed class CheckoutUseCase : IUseCase<CheckoutRequest, CheckoutResult>
{
    private readonly Func<string, IRepositoryContext> _contextFactory;

    public CheckoutUseCase(Func<string, IRepositoryContext> contextFactory)
    {
        _contextFactory = contextFactory ?? throw new ArgumentNullException(nameof(contextFactory));
    }

    public async Task<CheckoutResult> ExecuteAsync(CheckoutRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.RepositoryDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Reference);

        var context = _contextFactory(request.RepositoryDirectory);
        var repoRoot = !string.IsNullOrWhiteSpace(context.RootPath)
            ? Path.GetFullPath(context.RootPath)
            : Path.GetFullPath(request.RepositoryDirectory);

        // 1. Resolve commit reference (branch, full hash of prefix)
        var commitId = context.ResolveReference(request.Reference)
            ?? throw new InvalidOperationException($"Reference '{request.Reference}' could not be resolved to a commit.");

        // 2. Load commit & snapshot
        var commit = await context.LoadCommitAsync(commitId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Commit '{commitId}' not found in repository.");

        var snapshot = await context.LoadSnapshotAsync(commit.SnapshotId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Snapshot '{commit.SnapshotId}' for commit '{commitId}' not found in repository.");

        var entries = snapshot.Project.Root.GetEntries().ToList();
        var entryPaths = entries.Select(e => e.Path.Value).ToList();

        // 3. Pre-materialization guards: path security (FR-CHK-012)
        PathSecurityGuard.ValidateAll(entryPaths);

        // 4. Pre-materialization object store check: alle benodigde blobs moeten intact zijn (FR-CHK-001..003)
        foreach (var entry in entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!context.ObjectStore.Exists(entry.Blob.Value))
            {
                throw new CheckoutStagingException($"Required object blob '{entry.Blob.Value}' for artifact '{entry.Path.Value}' is missing from the repository object store.");
            }
        }

        // 5. Bestemming en Dirty Workspace controle (FR-CHK-008, FR-CHK-009, FR-CHK-010, AC-009, AC-010)
        string targetDir;
        string? recoveryDir = null;
        var isExternalRestore = !string.IsNullOrWhiteSpace(request.RestoreToDirectory);

        if (isExternalRestore)
        {
            // --restore-to: herstel naar aparte directory; active workspace blijft onaangeraakt (AC-009)
            targetDir = Path.GetFullPath(request.RestoreToDirectory!);
            Directory.CreateDirectory(targetDir);
        }
        else
        {
            // In-place checkout naar active workspace
            targetDir = repoRoot;

            var statusUseCase = new StatusUseCase(_contextFactory);
            var statusResult = await statusUseCase.ExecuteAsync(new StatusRequest(repoRoot), cancellationToken).ConfigureAwait(false);
            var status = statusResult.Status;

            if (status.HasStagedChanges || status.HasWorkingTreeModifications)
            {
                if (!request.Force)
                {
                    // Dirty workspace weigert met exitcode 7 (AC-009)
                    throw new DirtyWorkspaceException(status);
                }

                // --force: Maak eerst een volledige recovery copy van conflicterende en lokale bestanden (FR-CHK-010, AC-010)
                var filesToBackup = status.Staged.Select(s => s.Path.Value)
                    .Concat(status.Modified.Select(m => m.Path.Value))
                    .Concat(entryPaths.Where(p => File.Exists(Path.Combine(repoRoot, p))))
                    .Distinct(StringComparer.OrdinalIgnoreCase);

                recoveryDir = await RecoveryCopyService.CreateRecoveryCopyAsync(repoRoot, filesToBackup, cancellationToken).ConfigureAwait(false);
            }
        }

        // 6. Staged materialization op hetzelfde volume (FR-CHK-004, ADR-IO-001)
        var stagingBaseDir = isExternalRestore ? targetDir : Path.Combine(repoRoot, ".dawvc");
        var stagingDir = Path.Combine(stagingBaseDir, $".staging_checkout_{Guid.NewGuid():N}");
        Directory.CreateDirectory(stagingDir);

        var restoredFiles = new List<string>();

        try
        {
            // Materialiseer alle bestanden eerst in de staging-directory
            foreach (var entry in entries)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var stagingFilePath = Path.Combine(stagingDir, entry.Path.Value.Replace('/', Path.DirectorySeparatorChar));
                var stagingFileDir = Path.GetDirectoryName(stagingFilePath);
                if (!string.IsNullOrEmpty(stagingFileDir))
                {
                    Directory.CreateDirectory(stagingFileDir);
                }

                await using (var payloadStream = await context.ObjectStore.OpenPayloadStreamAsync(entry.Blob.Value, cancellationToken).ConfigureAwait(false))
                await using (var fileStream = new FileStream(stagingFilePath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 64 * 1024, FileOptions.Asynchronous))
                {
                    await payloadStream.CopyToAsync(fileStream, cancellationToken).ConfigureAwait(false);
                    await fileStream.FlushAsync(cancellationToken).ConfigureAwait(false);
                }

                // 7. Post-materialization hashverificatie per entry (FR-CHK-005, IMP-0707)
                var actualHash = await Blake3ContentHasher.HashFileAsync(stagingFilePath, cancellationToken).ConfigureAwait(false);
                if (actualHash != entry.Hash)
                {
                    throw new CheckoutStagingException($"Integrity verification failed for staged artifact '{entry.Path.Value}'. Expected hash {entry.Hash}, but got {actualHash}.");
                }
            }

            // 8. Atomische installatie van de geverifieerde candidate (FR-CHK-006, ADR-IO-001)
            foreach (var entry in entries)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var stagingFilePath = Path.Combine(stagingDir, entry.Path.Value.Replace('/', Path.DirectorySeparatorChar));
                var finalFilePath = Path.Combine(targetDir, entry.Path.Value.Replace('/', Path.DirectorySeparatorChar));
                var finalFileDir = Path.GetDirectoryName(finalFilePath);
                if (!string.IsNullOrEmpty(finalFileDir))
                {
                    Directory.CreateDirectory(finalFileDir);
                }

                File.Move(stagingFilePath, finalFilePath, overwrite: true);
                restoredFiles.Add(entry.Path.Value);
            }
        }
        finally
        {
            // Ruim staging directory altijd op (bij succes of falen)
            if (Directory.Exists(stagingDir))
            {
                try
                {
                    Directory.Delete(stagingDir, recursive: true);
                }
                catch
                {
                    // Best effort cleanup
                }
            }
        }

        // 9. Werk HEAD en staging index bij (alleen bij in-place checkout)
        if (!isExternalRestore)
        {
            // Verplaats branch pointer indien een branchnaam werd opgegeven
            var branch = new BranchName(request.Reference);
            var existingBranchCommit = context.GetBranchCommit(branch);
            if (existingBranchCommit.HasValue)
            {
                context.SetCurrentBranch(branch);
                await context.UpdateBranchCommitAsync(branch, commit.Id, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                // Checkout van specifieke commit hash
                var currentBranch = context.GetCurrentBranch();
                await context.UpdateBranchCommitAsync(currentBranch, commit.Id, cancellationToken).ConfigureAwait(false);
            }

            // Reset staging index naar schone HEAD toestand
            await context.StagingIndex.ClearStagedEntriesAsync(cancellationToken).ConfigureAwait(false);
        }

        // 10. Dependency Resolution & Local Bindings (FR-BND-001..009, FR-CHK-014, FR-CHK-015, AC-013)
        var resolvedBindings = new List<DependencyBinding>();
        var manualInstructions = new List<string>();

        var managedAssetRoot = Path.Combine(targetDir, "Audio");
        if (!Directory.Exists(managedAssetRoot))
        {
            managedAssetRoot = targetDir;
        }

        if (snapshot.Dependencies.All.Count > 0)
        {
            foreach (var dep in snapshot.Dependencies.All)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var binding = await DependencyResolverPipeline.ResolveAsync(
                    targetDir,
                    dep,
                    context.LocalBindings,
                    knownHashIndex: null,
                    cancellationToken).ConfigureAwait(false);

                resolvedBindings.Add(binding);

                if (dep is PluginDependency plugin && binding.Status != BindingStatus.Verified)
                {
                    manualInstructions.Add($"Plugin missing: {plugin.Plugin.Product} ({plugin.Plugin.Vendor}) [{plugin.Plugin.Format}]. Please install the plugin binary.");
                }
            }

            // Sla op in lokale binding store (blijft machine-lokaal)
            if (!isExternalRestore)
            {
                await context.LocalBindings.SaveAllAsync(resolvedBindings, cancellationToken).ConfigureAwait(false);
            }
        }

        // FL Studio Browser extra search folder instructie (AC-013, FR-CHK-015)
        manualInstructions.Add($"FL Studio configuration: Add '{managedAssetRoot}' to Options > File Settings > Browser extra search folders to ensure all samples load automatically.");

        return new CheckoutResult(
            commit.Id,
            snapshot.Id,
            targetDir,
            restoredFiles,
            recoveryDir,
            managedAssetRoot,
            manualInstructions,
            resolvedBindings);
    }
}
