using DawVcs.Application.Common;
using DawVcs.Domain.Artifacts;
using DawVcs.Domain.Hashing;
using DawVcs.Domain.Repositories;

namespace DawVcs.Application.Scanning;

public sealed record StatusRequest(
    string RepositoryDirectory);

public sealed record StatusResult(
    WorkingTreeStatus Status);

/// <summary>
/// Scans the repository working tree against HEAD and staging area (FR-SCAN-008, FR-STG-007, FR-STG-008, FR-STG-010).
/// </summary>
public sealed class StatusUseCase : IUseCase<StatusRequest, StatusResult>
{
    private static readonly HashSet<string> IgnoredDirectoryNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ".dawvc",
        ".git",
        ".vs",
        ".idea",
        "bin",
        "obj"
    };

    private readonly Func<string, IRepositoryContext> _contextFactory;

    public StatusUseCase(Func<string, IRepositoryContext> contextFactory)
    {
        _contextFactory = contextFactory ?? throw new ArgumentNullException(nameof(contextFactory));
    }

    public Task<StatusResult> ExecuteAsync(StatusRequest request, CancellationToken cancellationToken = default)
        => ExecuteAsync(request, cancellationToken, null);

    public async Task<StatusResult> ExecuteAsync(StatusRequest request, CancellationToken cancellationToken, IProgress<OperationProgress>? progress)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.RepositoryDirectory);

        progress?.Report(new OperationProgress(OperationStage.LoadingMetadata));
        cancellationToken.ThrowIfCancellationRequested();
        using var context = _contextFactory(request.RepositoryDirectory);
        var repoRoot = Path.GetFullPath(context.RootPath);
        var config = context.LoadConfig();

        var currentBranch = context.GetCurrentBranch();
        var headCommitId = context.GetBranchCommit(currentBranch);

        // 1. Load HEAD snapshot entries
        var headEntries = new Dictionary<string, ArtifactEntry>(StringComparer.OrdinalIgnoreCase);
        if (headCommitId.HasValue)
        {
            var headCommit = await context.LoadCommitAsync(headCommitId.Value, cancellationToken).ConfigureAwait(false);
            if (headCommit is not null)
            {
                var headSnapshot = await context.LoadSnapshotAsync(headCommit.SnapshotId, cancellationToken).ConfigureAwait(false);
                if (headSnapshot is not null)
                {
                    foreach (var entry in headSnapshot.Project.Root.GetEntries())
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        headEntries[entry.Path.Value] = entry;
                    }
                }
            }
        }

        // 2. Load staged entries from staging index
        var stagedEntries = await context.StagingIndex.GetStagedEntriesAsync(cancellationToken).ConfigureAwait(false);
        var stagedDict = new Dictionary<string, ArtifactEntry>(StringComparer.OrdinalIgnoreCase);

        var stagedList = new List<WorkingTreeItem>();
        var modifiedList = new List<WorkingTreeItem>();
        var untrackedList = new List<WorkingTreeItem>();
        var missingList = new List<WorkingTreeItem>();
        var renamedList = new List<WorkingTreeItem>();

        // Add explicitly staged entries to staged list
        foreach (var entry in stagedEntries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            stagedDict.Add(entry.Path.Value, entry);
            stagedList.Add(new WorkingTreeItem(entry.Path, WorkingTreeItemKind.Staged, entry.Size, entry.Hash));
        }

        // 3. Discover working directory files
        var onDiskFiles = new Dictionary<string, (string FullPath, long Size, ContentHash Hash)>(StringComparer.OrdinalIgnoreCase);

        progress?.Report(new OperationProgress(OperationStage.DiscoveringFiles));
        var candidateList = new List<(string FilePath, ArtifactPath ArtifactPath, long FileLength, DateTime LastModified)>();
        if (Directory.Exists(repoRoot))
        {
            foreach (var file in Directory.EnumerateFiles(repoRoot, "*", SearchOption.AllDirectories))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var relPath = Path.GetRelativePath(repoRoot, file);
                var segments = relPath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                if (segments.Any(s => IgnoredDirectoryNames.Contains(s) || s.StartsWith('.')))
                {
                    continue;
                }

                if (!ArtifactPath.TryCreate(relPath, out var artifactPath, out _))
                {
                    continue;
                }

                var fileInfo = new FileInfo(file);
                candidateList.Add((file, artifactPath, fileInfo.Length, fileInfo.LastWriteTimeUtc));
            }
        }

        // Fast cache check (FR-SCAN-011, NFR-PERF-004)
        var uncachedList = new List<(string FilePath, ArtifactPath ArtifactPath, long FileLength, DateTime LastModified)>();
        var resolvedHashes = new Dictionary<string, ContentHash>(StringComparer.OrdinalIgnoreCase);

        progress?.Report(new OperationProgress(OperationStage.CheckingCache, 0, candidateList.Count));
        var checkedCount = 0;
        foreach (var candidate in candidateList)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var cached = await context.StagingIndex.TryGetCachedHashAsync(candidate.ArtifactPath, candidate.FileLength, candidate.LastModified, cancellationToken).ConfigureAwait(false);
            if (cached.HasValue)
            {
                resolvedHashes[candidate.ArtifactPath.Value] = cached.Value;
            }
            else
            {
                uncachedList.Add(candidate);
            }

            checkedCount++;
            progress?.Report(new OperationProgress(OperationStage.CheckingCache, checkedCount, candidateList.Count));
        }

        // Bounded concurrency hashing for uncached files (NFR-PERF-006)
        progress?.Report(new OperationProgress(OperationStage.HashingFiles, 0, uncachedList.Count));
        if (uncachedList.Count > 0)
        {
            var concurrentResolved = new System.Collections.Concurrent.ConcurrentDictionary<string, ContentHash>(StringComparer.OrdinalIgnoreCase);
            var progressLock = new object();
            var hashedCount = 0;
            await ConcurrencyLimiter.ForEachAsync(uncachedList, async (candidate, ct) =>
            {
                var hash = await Blake3ContentHasher.HashFileAsync(candidate.FilePath, ct).ConfigureAwait(false);
                await context.StagingIndex.SetCachedHashAsync(candidate.ArtifactPath, candidate.FileLength, candidate.LastModified, hash, ct).ConfigureAwait(false);
                concurrentResolved[candidate.ArtifactPath.Value] = hash;
                lock (progressLock)
                {
                    hashedCount++;
                    progress?.Report(new OperationProgress(OperationStage.HashingFiles, hashedCount, uncachedList.Count));
                }
            }, cancellationToken: cancellationToken).ConfigureAwait(false);

            foreach (var (k, v) in concurrentResolved)
            {
                cancellationToken.ThrowIfCancellationRequested();
                resolvedHashes[k] = v;
            }
        }

        progress?.Report(new OperationProgress(OperationStage.ClassifyingChanges));
        foreach (var candidate in candidateList)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var hash = resolvedHashes[candidate.ArtifactPath.Value];
            onDiskFiles[candidate.ArtifactPath.Value] = (candidate.FilePath, candidate.FileLength, hash);

            if (stagedDict.ContainsKey(candidate.ArtifactPath.Value))
            {
                continue;
            }

            if (headEntries.TryGetValue(candidate.ArtifactPath.Value, out var headEntry))
            {
                if (headEntry.Hash != hash)
                {
                    modifiedList.Add(new WorkingTreeItem(candidate.ArtifactPath, WorkingTreeItemKind.Modified, candidate.FileLength, hash));
                }
            }
            else if (candidate.ArtifactPath == config.PrimaryArtifact)
            {
                modifiedList.Add(new WorkingTreeItem(candidate.ArtifactPath, WorkingTreeItemKind.Modified, candidate.FileLength, hash));
            }
            else
            {
                untrackedList.Add(new WorkingTreeItem(candidate.ArtifactPath, WorkingTreeItemKind.Untracked, candidate.FileLength, hash));
            }
        }

        // 4. Detect missing files from HEAD
        var missingCandidates = new List<ArtifactEntry>();
        foreach (var (path, entry) in headEntries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!onDiskFiles.ContainsKey(path) && !stagedDict.ContainsKey(path))
            {
                missingCandidates.Add(entry);
            }
        }

        // 5. Content-based rename detection (FR-STG-010)
        var remainingUntracked = new List<WorkingTreeItem>();
        foreach (var untracked in untrackedList)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ArtifactEntry? matchedMissing = null;
            foreach (var missing in missingCandidates)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (missing.Hash == untracked.Hash)
                {
                    matchedMissing = missing;
                    break;
                }
            }
            if (matchedMissing is not null)
            {
                renamedList.Add(new WorkingTreeItem(untracked.Path, WorkingTreeItemKind.Renamed, untracked.Size, untracked.Hash, matchedMissing.Path));
                missingCandidates.Remove(matchedMissing);
            }
            else
            {
                remainingUntracked.Add(untracked);
            }
        }

        foreach (var missing in missingCandidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            missingList.Add(new WorkingTreeItem(missing.Path, WorkingTreeItemKind.Missing, missing.Size, missing.Hash));
        }

        var status = new WorkingTreeStatus(
            currentBranch,
            headCommitId,
            stagedList,
            modifiedList,
            remainingUntracked,
            missingList,
            renamedList);

        cancellationToken.ThrowIfCancellationRequested();
        progress?.Report(new OperationProgress(OperationStage.Completed));
        return new StatusResult(status);
    }
}
