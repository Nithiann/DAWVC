using DawVcs.Domain.Hashing;

namespace DawVcs.Domain.Artifacts;

/// <summary>
/// Domain model for a versioned DAW project artifact (single file, directory, package, or archive).
/// </summary>
public sealed record ProjectArtifact
{
    public string DawName { get; init; }
    public ArtifactRoot Root { get; init; }
    public ContentHash AggregateHash { get; init; }

    public ProjectArtifact(string dawName, ArtifactRoot root)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dawName);
        ArgumentNullException.ThrowIfNull(root);

        DawName = dawName;
        Root = root;
        AggregateHash = root.ComputeAggregateHash();
    }

    public static ProjectArtifact CreateSingleFile(string dawName, ArtifactPath path, ContentHash hash, long size)
    {
        var entry = ArtifactEntry.Create(path, hash, size, ArtifactRole.PrimaryProjectFile);
        return new ProjectArtifact(dawName, new SingleFileArtifact(entry));
    }
}
