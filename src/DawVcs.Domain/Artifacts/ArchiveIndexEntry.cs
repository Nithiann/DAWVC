using DawVcs.Domain.Hashing;

namespace DawVcs.Domain.Artifacts;

/// <summary>
/// Informational index entry of a file within an inspected archive project (e.g. FL Studio zipped project).
/// </summary>
public sealed record ArchiveIndexEntry(
    ArtifactPath Path,
    ContentHash? Hash,
    long UncompressedSize,
    ArtifactRole Role);
