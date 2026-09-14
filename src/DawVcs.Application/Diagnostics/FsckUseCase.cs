using DawVcs.Adapters.Abstractions;
using DawVcs.Application.Adapters;
using DawVcs.Domain.Common;
using DawVcs.Domain.Hashing;
using DawVcs.Domain.Repositories;
using DawVcs.Domain.Storage;

namespace DawVcs.Application.Diagnostics;

public sealed record FsckRequest(
    string RepositoryDirectory,
    bool CheckArtifacts = false);

/// <summary>
/// Voert een volledige integriteitscontrole uit over de objectgraph en repositoryopslag (FR-FSC-001..008, AC-012).
/// Garandeert zero-repair (FR-FSC-007) en strikte scheiding tussen repository- en artifact-integriteit (AC-012).
/// </summary>
public sealed class FsckUseCase
{
    private readonly Func<string, IRepositoryContext> _contextFactory;
    private readonly IDawAdapterRegistry? _adapterRegistry;

    public FsckUseCase(
        Func<string, IRepositoryContext> contextFactory,
        IDawAdapterRegistry? adapterRegistry = null)
    {
        _contextFactory = contextFactory ?? throw new ArgumentNullException(nameof(contextFactory));
        _adapterRegistry = adapterRegistry;
    }

    public async Task<FsckReport> ExecuteAsync(FsckRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.RepositoryDirectory);

        var context = _contextFactory(request.RepositoryDirectory);
        var repoErrors = new List<IntegrityIssue>();
        var repoWarnings = new List<IntegrityIssue>();

        var scannedObjects = new HashSet<ContentHash>();
        var validObjects = new HashSet<ContentHash>();

        // 1. Scan en verifieer alle loose objects in de object store
        var storedEntries = context.ObjectStore.EnumerateStoredObjects();
        foreach (var entry in storedEntries)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!entry.HasValidName || !entry.Hash.HasValue)
            {
                repoErrors.Add(new IntegrityIssue(
                    Code: "InvalidObjectFileName",
                    Target: entry.RelativePath,
                    Message: $"File name '{entry.RelativePath}' is not a valid 64-character BLAKE3 hex object path.",
                    IsError: true));
                continue;
            }

            var expectedHash = entry.Hash.Value;
            scannedObjects.Add(expectedHash);

            try
            {
                await context.ObjectStore.VerifyObjectIntegrityAsync(expectedHash, cancellationToken).ConfigureAwait(false);
                validObjects.Add(expectedHash);
            }
            catch (PayloadHashMismatchException ex)
            {
                repoErrors.Add(new IntegrityIssue(
                    Code: "ObjectHashMismatch",
                    Target: expectedHash.ToString(),
                    Message: ex.Message,
                    IsError: true));
            }
            catch (PayloadLengthMismatchException ex)
            {
                repoErrors.Add(new IntegrityIssue(
                    Code: "PayloadTruncated",
                    Target: expectedHash.ToString(),
                    Message: ex.Message,
                    IsError: true));
            }
            catch (InvalidEnvelopeException ex)
            {
                repoErrors.Add(new IntegrityIssue(
                    Code: "CorruptEnvelope",
                    Target: expectedHash.ToString(),
                    Message: ex.Message,
                    IsError: true));
            }
            catch (Exception ex)
            {
                repoErrors.Add(new IntegrityIssue(
                    Code: "ObjectReadError",
                    Target: expectedHash.ToString(),
                    Message: ex.Message,
                    IsError: true));
            }
        }

        // 2. Doorkruis de objectgraph vanaf refs en HEAD
        var reachableHashes = new HashSet<ContentHash>();
        var commitQueue = new Queue<CommitId>();
        var visitedCommits = new HashSet<CommitId>();
        var artifactBlobsToCheck = new List<(string Path, ContentHash BlobHash)>();

        var branches = context.GetBranches();
        foreach (var branch in branches)
        {
            if (branch.CommitId.HasValue && visitedCommits.Add(branch.CommitId.Value))
            {
                commitQueue.Enqueue(branch.CommitId.Value);
            }
        }

        var currentBranch = context.GetCurrentBranch();
        var headCommit = context.GetBranchCommit(currentBranch);
        if (headCommit.HasValue && visitedCommits.Add(headCommit.Value))
        {
            commitQueue.Enqueue(headCommit.Value);
        }

        int missingObjectsCount = 0;

        while (commitQueue.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var currentCommitId = commitQueue.Dequeue();
            reachableHashes.Add(currentCommitId.Value);

            if (!context.ObjectStore.Exists(currentCommitId.Value))
            {
                missingObjectsCount++;
                repoErrors.Add(new IntegrityIssue(
                    Code: "MissingObject",
                    Target: currentCommitId.ToString(),
                    Message: $"Commit object '{currentCommitId}' referenced by branch is missing from object store.",
                    IsError: true));
                continue;
            }

            var commit = await context.LoadCommitAsync(currentCommitId, cancellationToken).ConfigureAwait(false);
            if (commit == null)
            {
                missingObjectsCount++;
                repoErrors.Add(new IntegrityIssue(
                    Code: "MissingObject",
                    Target: currentCommitId.ToString(),
                    Message: $"Failed to deserialize commit object '{currentCommitId}'.",
                    IsError: true));
                continue;
            }

            foreach (var parent in commit.Parents)
            {
                if (visitedCommits.Add(parent))
                {
                    commitQueue.Enqueue(parent);
                }
            }

            // Snapshot verifiëren
            var snapshotHash = commit.SnapshotId.Value;
            reachableHashes.Add(snapshotHash);

            if (!context.ObjectStore.Exists(snapshotHash))
            {
                missingObjectsCount++;
                repoErrors.Add(new IntegrityIssue(
                    Code: "MissingObject",
                    Target: snapshotHash.ToString(),
                    Message: $"Snapshot object '{snapshotHash}' referenced by commit '{currentCommitId}' is missing.",
                    IsError: true));
                continue;
            }

            var snapshot = await context.LoadSnapshotAsync(commit.SnapshotId, cancellationToken).ConfigureAwait(false);
            if (snapshot == null)
            {
                missingObjectsCount++;
                repoErrors.Add(new IntegrityIssue(
                    Code: "MissingObject",
                    Target: snapshotHash.ToString(),
                    Message: $"Failed to deserialize snapshot '{snapshotHash}'.",
                    IsError: true));
                continue;
            }

            // Entries in snapshot
            foreach (var entry in snapshot.Project.Root.GetEntries())
            {
                var blobHash = entry.Blob.Value;
                reachableHashes.Add(blobHash);

                if (!context.ObjectStore.Exists(blobHash))
                {
                    missingObjectsCount++;
                    repoErrors.Add(new IntegrityIssue(
                        Code: "MissingObject",
                        Target: blobHash.ToString(),
                        Message: $"Blob '{blobHash}' for artifact '{entry.Path.Value}' in snapshot '{snapshotHash}' is missing.",
                        IsError: true));
                }
                else
                {
                    artifactBlobsToCheck.Add((entry.Path.Value, blobHash));
                }
            }
        }

        // Wezen (Orphan objects) identificeren
        int orphanCount = 0;
        foreach (var validObj in validObjects)
        {
            if (!reachableHashes.Contains(validObj))
            {
                orphanCount++;
                repoWarnings.Add(new IntegrityIssue(
                    Code: "OrphanObject",
                    Target: validObj.ToString(),
                    Message: $"Object '{validObj}' is not reachable from any branch or reference.",
                    IsError: false));
            }
        }

        var repoIntegrity = new RepositoryIntegrityReport(
            TotalObjectsScanned: scannedObjects.Count,
            ValidObjectsCount: validObjects.Count,
            CorruptObjectsCount: repoErrors.Count(e => e.Code != "MissingObject"),
            MissingObjectsCount: missingObjectsCount,
            OrphanObjectsCount: orphanCount,
            Errors: repoErrors,
            Warnings: repoWarnings);

        // 3. Artifact Integriteit (indien --artifacts is gespecificeerd)
        ArtifactIntegrityReport? artifactIntegrity = null;

        if (request.CheckArtifacts)
        {
            var artErrors = new List<IntegrityIssue>();
            var artWarnings = new List<IntegrityIssue>();
            int validArtifacts = 0;
            int failedArtifacts = 0;

            var distinctArtifacts = artifactBlobsToCheck
                .DistinctBy(a => a.BlobHash)
                .ToList();

            foreach (var (relPath, blobHash) in distinctArtifacts)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var ext = Path.GetExtension(relPath);
                var adapter = _adapterRegistry?.FindAdapterForExtension(ext);
                if (adapter == null)
                {
                    artWarnings.Add(new IntegrityIssue(
                        Code: "NoAdapterForArtifact",
                        Target: relPath,
                        Message: $"No DAW adapter registered for artifact '{relPath}'.",
                        IsError: false));
                    continue;
                }

                try
                {
                    var payloadBytes = await context.ObjectStore.ReadObjectPayloadAsync(blobHash, cancellationToken).ConfigureAwait(false);
                    using var ms = new MemoryStream(payloadBytes);
                    var detectResult = await adapter.DetectAsync(ms, cancellationToken).ConfigureAwait(false);

                    if (detectResult.Status == ProjectDetectionStatus.Invalid)
                    {
                        failedArtifacts++;
                        // AC-012: Adapter validatiefout wordt gerapporteerd onder Artifact Integrity, NOOIT als ObjectHashMismatch onder Repository Integrity!
                        artErrors.Add(new IntegrityIssue(
                            Code: "InvalidArtifactContent",
                            Target: $"{relPath} ({blobHash})",
                            Message: $"Adapter '{adapter.DawName}' reported artifact as invalid: {string.Join("; ", detectResult.Findings)}",
                            IsError: true));
                    }
                    else
                    {
                        validArtifacts++;
                        if (detectResult.Status is ProjectDetectionStatus.Suspicious or ProjectDetectionStatus.Unsupported)
                        {
                            artWarnings.Add(new IntegrityIssue(
                                Code: "SuspiciousArtifactContent",
                                Target: $"{relPath} ({blobHash})",
                                Message: $"Adapter '{adapter.DawName}' noted warnings: {string.Join("; ", detectResult.Findings)}",
                                IsError: false));
                        }
                    }
                }
                catch (Exception ex)
                {
                    failedArtifacts++;
                    // AC-012: Adapter exception wordt gerapporteerd onder Artifact Integrity, NOOIT onder Repository Integrity!
                    artErrors.Add(new IntegrityIssue(
                        Code: "ArtifactInspectionException",
                        Target: $"{relPath} ({blobHash})",
                        Message: $"Exception during adapter inspection of '{relPath}': {ex.Message}",
                        IsError: true));
                }
            }

            artifactIntegrity = new ArtifactIntegrityReport(
                ArtifactsScanned: distinctArtifacts.Count,
                ValidArtifactsCount: validArtifacts,
                FailedArtifactsCount: failedArtifacts,
                Errors: artErrors,
                Warnings: artWarnings);
        }

        return new FsckReport(
            SchemaVersion: 1,
            RepositoryIntegrity: repoIntegrity,
            ArtifactIntegrity: artifactIntegrity);
    }
}
