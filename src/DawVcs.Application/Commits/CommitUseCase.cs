using DawVcs.Application.Common;
using DawVcs.Domain.Artifacts;
using DawVcs.Domain.Common;
using DawVcs.Domain.Entities;
using DawVcs.Domain.Hashing;
using DawVcs.Domain.Repositories;
using DawVcs.Domain.Serialization;
using DawVcs.Domain.Storage;

namespace DawVcs.Application.Commits;

public sealed record CommitRequest(
    string RepositoryDirectory,
    string Message,
    string? Author = null);

public sealed record CommitResult(
    CommitId CommitId,
    SnapshotId SnapshotId,
    string Message,
    string Author,
    DateTimeOffset Timestamp,
    BranchName Branch);

/// <summary>
/// Orchestrates committing the primary project artifact into repository history (FR-COM-001 through FR-COM-011).
/// </summary>
public sealed class CommitUseCase : IUseCase<CommitRequest, CommitResult>
{
    private readonly Func<string, IRepositoryContext> _contextFactory;

    public CommitUseCase(Func<string, IRepositoryContext> contextFactory)
    {
        _contextFactory = contextFactory;
    }

    public async Task<CommitResult> ExecuteAsync(CommitRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var context = _contextFactory(request.RepositoryDirectory);
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

        // 2. Resolve parent commit from current branch ref
        var currentBranch = context.GetCurrentBranch();
        var currentCommitId = context.GetBranchCommit(currentBranch);
        var parents = new List<CommitId>();
        ProjectSnapshot? parentSnapshot = null;

        if (currentCommitId.HasValue)
        {
            parents.Add(currentCommitId.Value);
            var parentCommit = await context.LoadCommitAsync(currentCommitId.Value, cancellationToken).ConfigureAwait(false);
            if (parentCommit is not null)
            {
                parentSnapshot = await context.LoadSnapshotAsync(parentCommit.SnapshotId, cancellationToken).ConfigureAwait(false);
                if (parentSnapshot is not null)
                {
                    // Carry forward previously tracked assets automatically (FR-STG-002)
                    foreach (var tracked in parentSnapshot.Project.Root.GetEntries())
                    {
                        if (tracked.Path == config.PrimaryArtifact)
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
                    }
                }
            }
        }

        // 3. Include explicitly staged entries (FR-STG-004, AC-004)
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
        }

        // 4. Build ProjectArtifact (SingleFileArtifact or DirectoryArtifact)
        var entriesList = entriesByPath.Values.ToList();
        ArtifactRoot root = entriesList.Count == 1
            ? new SingleFileArtifact(entriesList[0])
            : new DirectoryArtifact(entriesList);

        var projectArtifact = new ProjectArtifact("FL Studio", root);
        var snapshot = new ProjectSnapshot(projectArtifact, DateTimeOffset.UtcNow);

        // 5. Rejection of no-op commits (FR-COM-003)
        if (parentSnapshot is not null && parentSnapshot.Project.AggregateHash == projectArtifact.AggregateHash)
        {
            throw new InvalidOperationException("Nothing to commit, working tree clean.");
        }

        // 6. Store snapshot object in object store
        var snapshotBytes = snapshot.ToCanonicalBytes();
        await context.ObjectStore.WriteObjectAsync(ObjectType.Snapshot, snapshotBytes, cancellationToken).ConfigureAwait(false);

        // 7. Build and store commit object
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

        // 8. Atomically update branch ref pointer (FR-COM-006)
        await context.UpdateBranchCommitAsync(currentBranch, commit.Id, cancellationToken).ConfigureAwait(false);

        // 9. Clear staging index upon successful commit (FR-STG-001)
        await context.StagingIndex.ClearStagedEntriesAsync(cancellationToken).ConfigureAwait(false);

        return new CommitResult(
            commit.Id,
            snapshot.Id,
            commit.Message,
            commit.Author,
            commit.Timestamp,
            currentBranch);
    }

    private static async Task<ContentHash> StreamFileToBlobStoreAsync(IRepositoryContext context, string filePath, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, FileOptions.Asynchronous);
        return await context.ObjectStore.WriteBlobAsync(stream, cancellationToken).ConfigureAwait(false);
    }
}
