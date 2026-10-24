using DawVcs.Adapters.Abstractions;
using DawVcs.Application.Adapters;
using DawVcs.Application.Common;
using DawVcs.Application.Dependencies;
using DawVcs.Application.Exceptions;
using DawVcs.Domain.Artifacts;
using DawVcs.Domain.Common;
using DawVcs.Domain.Dependencies;
using DawVcs.Domain.Entities;
using DawVcs.Domain.Hashing;
using DawVcs.Domain.Repositories;
using DawVcs.Domain.Serialization;
using DawVcs.Domain.Storage;

namespace DawVcs.Application.Commits;

public sealed record CommitRequest(
    string RepositoryDirectory,
    string Message,
    string? Author = null,
    bool AllowIncomplete = false,
    bool Consolidate = false);

public sealed record CommitResult(
    CommitId CommitId,
    SnapshotId SnapshotId,
    string Message,
    string Author,
    DateTimeOffset Timestamp,
    BranchName Branch,
    bool IsComplete = true,
    string? IncompleteReason = null);

/// <summary>
/// Orchestreert het committen van projectbestanden en dependencies naar de repositoryhistorie (FR-COM-001 t/m FR-COM-011, FR-DEP-011 t/m FR-DEP-013).
/// </summary>
public sealed class CommitUseCase : IUseCase<CommitRequest, CommitResult>
{
    private readonly Func<string, IRepositoryContext> _contextFactory;
    private readonly IDawAdapterRegistry? _adapterRegistry;

    public CommitUseCase(
        Func<string, IRepositoryContext> contextFactory,
        IDawAdapterRegistry? adapterRegistry = null)
    {
        _contextFactory = contextFactory ?? throw new ArgumentNullException(nameof(contextFactory));
        _adapterRegistry = adapterRegistry;
    }

    public async Task<CommitResult> ExecuteAsync(CommitRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        using var context = _contextFactory(request.RepositoryDirectory);
        var config = context.LoadConfig();

        var primaryFile = Path.Combine(context.RootPath, config.PrimaryArtifact.Value);
        if (!File.Exists(primaryFile))
        {
            throw new FileNotFoundException($"Primary project file '{config.PrimaryArtifact.Value}' was not found in '{context.RootPath}'.", primaryFile);
        }

        // 1. Primary artifact (auto-tracked)
        long primaryFileSize;
        ContentHash primaryBlobHash;
        await using (var fileStream = new FileStream(primaryFile, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, FileOptions.Asynchronous))
        {
            primaryFileSize = fileStream.Length;
            primaryBlobHash = await context.ObjectStore.WriteBlobAsync(fileStream, cancellationToken).ConfigureAwait(false);
        }

        var entriesByPath = new Dictionary<string, ArtifactEntry>(StringComparer.OrdinalIgnoreCase)
        {
            [config.PrimaryArtifact.Value] = ArtifactEntry.Create(config.PrimaryArtifact, primaryBlobHash, primaryFileSize, ArtifactRole.PrimaryProjectFile)
        };

        // 2. Discover and evaluate dependencies from adapter (WP-06, FR-DEP-001..015)
        var dependencyGraph = DependencyGraph.Empty;
        if (_adapterRegistry != null)
        {
            var ext = Path.GetExtension(primaryFile);
            var adapter = _adapterRegistry.FindAdapterForExtension(ext);
            if (adapter != null)
            {
                using var readContext = new ArtifactReadContext(primaryFile);
                var detection = await adapter.DetectAsync(readContext, cancellationToken).ConfigureAwait(false);
                dependencyGraph = await DependencyDiscoveryService.DiscoverAsync(context.RootPath, detection, adapter, cancellationToken).ConfigureAwait(false);
            }
        }

        var missingBundleDependencies = new List<AssetDependency>();
        var updatedDependencies = new List<Dependency>();

        // 2b. Auto-bundle project asset dependencies with PortabilityMode.Bundle (Fase 2)
        foreach (var rawDep in dependencyGraph.Dependencies)
        {
            if (rawDep is AssetDependency assetDep && assetDep.Portability.Mode == PortabilityMode.Bundle)
            {
                string? sourceFilePath = null;
                if (assetDep.RelativePath != null)
                {
                    var relFull = Path.Combine(context.RootPath, assetDep.RelativePath.Value.Value.Replace('/', Path.DirectorySeparatorChar));
                    if (File.Exists(relFull))
                    {
                        sourceFilePath = relFull;
                    }
                }

                if (sourceFilePath == null && !string.IsNullOrWhiteSpace(assetDep.OriginalPath) && File.Exists(assetDep.OriginalPath))
                {
                    sourceFilePath = assetDep.OriginalPath;
                }

                if (sourceFilePath != null && File.Exists(sourceFilePath))
                {
                    var fileInfo = new FileInfo(sourceFilePath);
                    var currentBlobHash = await StreamFileToBlobStoreAsync(context, sourceFilePath, cancellationToken).ConfigureAwait(false);

                    // Determine clean relative snapshot path (e.g. Samples/Kick.wav)
                    ArtifactPath snapshotRelativePath;
                    if (assetDep.RelativePath != null)
                    {
                        snapshotRelativePath = assetDep.RelativePath.Value;
                    }
                    else
                    {
                        var cleanFileName = Path.GetFileName(sourceFilePath);
                        var targetRel = $"Samples/{cleanFileName}";

                        // Collision handling: if a different file with same relative path already exists
                        if (entriesByPath.TryGetValue(targetRel, out var existingEntry) && existingEntry.Hash != currentBlobHash)
                        {
                            targetRel = $"Samples/{currentBlobHash.ToString()[..8]}_{cleanFileName}";
                        }

                        snapshotRelativePath = new ArtifactPath(targetRel);
                    }

                    // Register in entriesByPath
                    entriesByPath[snapshotRelativePath.Value] = ArtifactEntry.Create(
                        snapshotRelativePath,
                        currentBlobHash,
                        fileInfo.Length,
                        ArtifactRole.ProjectAsset);

                    // Optional local consolidation: copy to workspace ./Samples/
                    if (request.Consolidate)
                    {
                        var localDest = Path.Combine(context.RootPath, snapshotRelativePath.Value.Replace('/', Path.DirectorySeparatorChar));
                        if (!File.Exists(localDest))
                        {
                            var localDestDir = Path.GetDirectoryName(localDest);
                            if (!string.IsNullOrEmpty(localDestDir))
                            {
                                Directory.CreateDirectory(localDestDir);
                            }
                            File.Copy(sourceFilePath, localDest, overwrite: false);
                        }
                    }

                    // Update dependency with resolved snapshot relative path and hash
                    updatedDependencies.Add(assetDep with
                    {
                        RelativePath = snapshotRelativePath,
                        Hash = currentBlobHash,
                        FileSize = fileInfo.Length,
                        IsMissing = false
                    });
                }
                else
                {
                    // Bundle dependency is missing on disk
                    missingBundleDependencies.Add(assetDep);
                    updatedDependencies.Add(assetDep);
                }
            }
            else
            {
                updatedDependencies.Add(rawDep);
            }
        }

        dependencyGraph = new DependencyGraph(updatedDependencies);

        // 3. Preserve previously tracked assets from HEAD snapshot (FR-STG-002)
        var currentBranch = context.GetCurrentBranch();
        var headCommitId = context.GetBranchCommit(currentBranch);
        var parents = new List<CommitId>();
        ProjectSnapshot? parentSnapshot = null;

        if (headCommitId is not null)
        {
            parents.Add(headCommitId.Value);
            var headCommit = await context.LoadCommitAsync(headCommitId.Value, cancellationToken).ConfigureAwait(false);
            if (headCommit is not null)
            {
                parentSnapshot = await context.LoadSnapshotAsync(headCommit.SnapshotId, cancellationToken).ConfigureAwait(false);
                if (parentSnapshot is not null)
                {
                    foreach (var tracked in parentSnapshot.Project.Root.GetEntries())
                    {
                        if (tracked.Path == config.PrimaryArtifact)
                        {
                            continue;
                        }

                        if (entriesByPath.ContainsKey(tracked.Path.Value))
                        {
                            continue;
                        }

                        var trackedFullPath = Path.Combine(context.RootPath, tracked.Path.Value);
                        if (File.Exists(trackedFullPath))
                        {
                            var fileInfo = new FileInfo(trackedFullPath);
                            var currentBlobHash = await StreamFileToBlobStoreAsync(context, trackedFullPath, cancellationToken).ConfigureAwait(false);
                            entriesByPath[tracked.Path.Value] = ArtifactEntry.Create(tracked.Path, currentBlobHash, fileInfo.Length, tracked.Role);
                        }
                        else
                        {
                            // Tracked bundle dependency is missing on disk (FR-DEP-012, AC-005)
                            missingBundleDependencies.Add(new AssetDependency(
                                DependencyId.ForAsset(tracked.Hash),
                                tracked.Path.FileName,
                                DependencyRequirement.Required,
                                DependencySource.ManualStaging,
                                PortabilityPolicy.BundleDefault,
                                relativePath: tracked.Path,
                                hash: tracked.Hash,
                                isMissing: true));
                        }
                    }
                }
            }
        }

        // 4. Include explicitly staged entries (FR-STG-004, AC-004)
        var stagedEntries = await context.StagingIndex.GetStagedEntriesAsync(cancellationToken).ConfigureAwait(false);
        foreach (var staged in stagedEntries)
        {
            var stagedFullPath = Path.Combine(context.RootPath, staged.Path.Value);
            if (File.Exists(stagedFullPath))
            {
                var fileInfo = new FileInfo(stagedFullPath);
                var currentBlobHash = await StreamFileToBlobStoreAsync(context, stagedFullPath, cancellationToken).ConfigureAwait(false);
                entriesByPath[staged.Path.Value] = ArtifactEntry.Create(staged.Path, currentBlobHash, fileInfo.Length, staged.Role);
            }
            else
            {
                // Staged bundle dependency is missing on disk (FR-DEP-012, AC-005)
                missingBundleDependencies.Add(new AssetDependency(
                    DependencyId.ForAsset(staged.Hash),
                    staged.Path.FileName,
                    DependencyRequirement.Required,
                    DependencySource.ManualStaging,
                    PortabilityPolicy.BundleDefault,
                    relativePath: staged.Path,
                    hash: staged.Hash,
                    isMissing: true));
            }
        }

        // 5. Check for missing required Bundle dependencies (FR-DEP-012, AC-005)
        bool isComplete = true;
        string? incompleteReason = null;

        if (missingBundleDependencies.Count > 0)
        {
            if (!request.AllowIncomplete)
            {
                throw new IncompleteDependencyException(missingBundleDependencies);
            }

            isComplete = false;
            incompleteReason = $"Ontbrekende verplichte bundle-dependencies: {string.Join(", ", missingBundleDependencies.Select(m => m.Name))}";
        }

        // 6. Build ProjectArtifact (SingleFileArtifact or DirectoryArtifact)
        var entriesList = entriesByPath.Values.ToList();
        ArtifactRoot root = entriesList.Count == 1
            ? new SingleFileArtifact(entriesList[0])
            : new DirectoryArtifact(entriesList);

        var dawName = "Generic DAW";
        if (config != null)
        {
            var primaryExt = Path.GetExtension(config.PrimaryArtifact.Value);
            var matchingAdapter = _adapterRegistry?.FindAdapterForExtension(primaryExt);
            if (matchingAdapter != null)
            {
                dawName = matchingAdapter.DawName;
            }
            else if (parentSnapshot != null)
            {
                dawName = parentSnapshot.Project.DawName;
            }
        }
        else if (parentSnapshot != null)
        {
            dawName = parentSnapshot.Project.DawName;
        }

        var projectArtifact = new ProjectArtifact(dawName, root);
        var snapshot = new ProjectSnapshot(
            projectArtifact,
            DateTimeOffset.UtcNow,
            isComplete: isComplete,
            incompleteReason: incompleteReason,
            dependencies: new DependencyGraph(updatedDependencies));

        // 7. Rejection of no-op commits (FR-COM-003)
        if (parentSnapshot is not null && parentSnapshot.Project.AggregateHash == projectArtifact.AggregateHash)
        {
            throw new InvalidOperationException("Nothing to commit, working tree clean.");
        }

        // 8. Store snapshot object in object store
        var snapshotBytes = snapshot.ToCanonicalBytes();
        await context.ObjectStore.WriteObjectAsync(ObjectType.Snapshot, snapshotBytes, cancellationToken).ConfigureAwait(false);

        // 9. Build and store commit object
        var author = !string.IsNullOrWhiteSpace(request.Author)
            ? request.Author.Trim()
            : (Environment.GetEnvironmentVariable("DAWVC_AUTHOR") ?? Environment.UserName);

        var commit = new Commit(
            parents,
            snapshot.Id,
            author,
            DateTimeOffset.UtcNow,
            request.Message);

        var commitBytes = commit.ToCanonicalBytes();
        await context.ObjectStore.WriteObjectAsync(ObjectType.Commit, commitBytes, cancellationToken).ConfigureAwait(false);

        // 10. Atomically update branch ref pointer (FR-COM-006)
        await context.UpdateBranchCommitAsync(currentBranch, commit.Id, cancellationToken).ConfigureAwait(false);

        // 11. Clear staging index upon successful commit (FR-STG-001)
        await context.StagingIndex.ClearStagedEntriesAsync(cancellationToken).ConfigureAwait(false);

        return new CommitResult(
            commit.Id,
            snapshot.Id,
            commit.Message,
            commit.Author,
            commit.Timestamp,
            currentBranch,
            snapshot.IsComplete,
            snapshot.IncompleteReason);
    }

    private static async Task<ContentHash> StreamFileToBlobStoreAsync(IRepositoryContext context, string filePath, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, FileOptions.Asynchronous);
        return await context.ObjectStore.WriteBlobAsync(stream, cancellationToken).ConfigureAwait(false);
    }
}
