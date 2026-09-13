using DawVcs.Domain.Artifacts;
using DawVcs.Domain.Hashing;

namespace DawVcs.Domain.Repositories;

/// <summary>
/// Domain port for interaction with the local staging area and filesystem metadata cache (FR-STG-001, FR-SCAN-011).
/// </summary>
public interface IStagingIndex
{
    /// <summary>
    /// Explicitly stages an artifact entry to be included in the next commit.
    /// </summary>
    Task StageEntryAsync(ArtifactEntry entry, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes an artifact entry from the staging area.
    /// </summary>
    Task UnstageEntryAsync(ArtifactPath path, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves all currently staged artifact entries.
    /// </summary>
    Task<IReadOnlyList<ArtifactEntry>> GetStagedEntriesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Clears all staged entries from the staging area.
    /// </summary>
    Task ClearStagedEntriesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Attempts to retrieve a cached BLAKE3 hash for a file if its size and last-write timestamp match.
    /// </summary>
    Task<ContentHash?> TryGetCachedHashAsync(ArtifactPath path, long size, DateTimeOffset lastModifiedUtc, CancellationToken cancellationToken = default);

    /// <summary>
    /// Caches the computed BLAKE3 hash alongside the file's size and last-write timestamp.
    /// </summary>
    Task SetCachedHashAsync(ArtifactPath path, long size, DateTimeOffset lastModifiedUtc, ContentHash hash, CancellationToken cancellationToken = default);
}
