using DawVcs.Domain.Common;
using DawVcs.Domain.Hashing;

namespace DawVcs.Domain.Artifacts;

/// <summary>
/// An immutable entry within an artifact tree representing a single tracked file.
/// </summary>
public sealed record ArtifactEntry(
    ArtifactPath Path,
    BlobId Blob,
    ContentHash Hash,
    long Size,
    ArtifactRole Role = ArtifactRole.ProjectAsset,
    UnixFileMode? Mode = null)
{
    public static ArtifactEntry Create(ArtifactPath path, ContentHash hash, long size, ArtifactRole role = ArtifactRole.ProjectAsset)
    {
        return new ArtifactEntry(path, new BlobId(hash), hash, size, role);
    }
}
