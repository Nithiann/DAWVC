using DawVcs.Application.Common;
using DawVcs.Domain.Common;
using DawVcs.Domain.Hashing;
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
/// </summary>
public sealed class CheckoutRestoreUseCase : IUseCase<CheckoutRestoreRequest, CheckoutRestoreResult>
{
    private readonly Func<string, IRepositoryContext> _contextFactory;

    public CheckoutRestoreUseCase(Func<string, IRepositoryContext> contextFactory)
    {
        _contextFactory = contextFactory ?? throw new ArgumentNullException(nameof(contextFactory));
    }

    public async Task<CheckoutRestoreResult> ExecuteAsync(CheckoutRestoreRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.RepositoryDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Reference);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.RestoreToDirectory);

        var context = _contextFactory(request.RepositoryDirectory);

        // 1. Resolve commit reference (branch, full hash, or prefix)
        var commitId = context.ResolveReference(request.Reference)
            ?? throw new InvalidOperationException($"Reference '{request.Reference}' could not be resolved to a commit.");

        // 2. Load commit
        var commit = await context.LoadCommitAsync(commitId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Commit '{commitId}' not found in repository.");

        // 3. Load snapshot
        var snapshot = await context.LoadSnapshotAsync(commit.SnapshotId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Snapshot '{commit.SnapshotId}' for commit '{commitId}' not found in repository.");

        // 4. Staged materialization
        var targetDir = Path.GetFullPath(request.RestoreToDirectory);
        Directory.CreateDirectory(targetDir);

        var stagingDir = Path.Combine(targetDir, $".tmp_restore_{Guid.NewGuid():N}");
        Directory.CreateDirectory(stagingDir);

        var restoredFiles = new List<string>();

        try
        {
            var entries = snapshot.Project.Root.GetEntries();

            // Materialize and verify every file in staging first
            foreach (var entry in entries)
            {
                var stagingFilePath = Path.Combine(stagingDir, entry.Path.Value);
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

                // Verify restored blob integrity matches the recorded hash
                var actualHash = await Blake3ContentHasher.HashFileAsync(stagingFilePath, cancellationToken).ConfigureAwait(false);
                if (actualHash != entry.Hash)
                {
                    throw new InvalidOperationException($"Integrity verification failed for restored artifact '{entry.Path.Value}'. Expected {entry.Hash}, but got {actualHash}.");
                }
            }

            // Move staged files into final destination
            foreach (var entry in entries)
            {
                var stagingFilePath = Path.Combine(stagingDir, entry.Path.Value);
                var finalFilePath = Path.Combine(targetDir, entry.Path.Value);
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

        return new CheckoutRestoreResult(
            commit.Id,
            snapshot.Id,
            targetDir,
            restoredFiles);
    }
}
