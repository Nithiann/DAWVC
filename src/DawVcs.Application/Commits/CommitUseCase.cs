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

        // 1. Write the primary artifact blob to object store (streaming & deduplicated)
        long fileSize;
        ContentHash blobHash;
        await using (var fileStream = new FileStream(primaryFile, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, FileOptions.Asynchronous))
        {
            fileSize = fileStream.Length;
            blobHash = await context.ObjectStore.WriteBlobAsync(fileStream, cancellationToken).ConfigureAwait(false);
        }

        // 2. Build ProjectArtifact and ProjectSnapshot
        var projectArtifact = ProjectArtifact.CreateSingleFile("FL Studio", config.PrimaryArtifact, blobHash, fileSize);
        var snapshot = new ProjectSnapshot(projectArtifact, DateTimeOffset.UtcNow);

        // 3. Resolve parent commit from current branch ref
        var currentBranch = context.GetCurrentBranch();
        var currentCommitId = context.GetBranchCommit(currentBranch);
        var parents = new List<CommitId>();

        if (currentCommitId.HasValue)
        {
            parents.Add(currentCommitId.Value);

            // Rejection of no-op commits (FR-COM-003)
            var parentCommit = await context.LoadCommitAsync(currentCommitId.Value, cancellationToken).ConfigureAwait(false);
            if (parentCommit is not null)
            {
                var parentSnapshot = await context.LoadSnapshotAsync(parentCommit.SnapshotId, cancellationToken).ConfigureAwait(false);
                if (parentSnapshot is not null && parentSnapshot.Project.AggregateHash == projectArtifact.AggregateHash)
                {
                    throw new InvalidOperationException("Nothing to commit, working tree clean.");
                }
            }
        }

        // 4. Store snapshot object in object store
        var snapshotBytes = snapshot.ToCanonicalBytes();
        await context.ObjectStore.WriteObjectAsync(ObjectType.Snapshot, snapshotBytes, cancellationToken).ConfigureAwait(false);

        // 5. Build and store commit object
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

        // 6. Atomically update branch ref pointer (FR-COM-006)
        await context.UpdateBranchCommitAsync(currentBranch, commit.Id, cancellationToken).ConfigureAwait(false);

        return new CommitResult(
            commit.Id,
            snapshot.Id,
            commit.Message,
            commit.Author,
            commit.Timestamp,
            currentBranch);
    }
}
