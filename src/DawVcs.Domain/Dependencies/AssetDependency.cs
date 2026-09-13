using DawVcs.Domain.Artifacts;
using DawVcs.Domain.Hashing;
using DawVcs.Domain.Metadata;

namespace DawVcs.Domain.Dependencies;

/// <summary>
/// Representeert een audiobestand, sample of opname (FR-DEP-001, FR-DEP-003, TD §11.6).
/// </summary>
public sealed record AssetDependency : Dependency
{
    public ArtifactPath? RelativePath { get; init; }
    public string? OriginalPath { get; init; }
    public ContentHash? Hash { get; init; }
    public long? FileSize { get; init; }
    public bool IsMissing { get; init; }

    public AssetDependency(
        DependencyId id,
        string name,
        DependencyRequirement requirement,
        DependencySource source,
        PortabilityPolicy portability,
        ArtifactPath? relativePath = null,
        string? originalPath = null,
        ContentHash? hash = null,
        long? fileSize = null,
        bool isMissing = false,
        MetadataObservation<string>? provenance = null)
        : base(id, DependencyKind.Asset, name, requirement, source, portability, provenance)
    {
        RelativePath = relativePath;
        OriginalPath = originalPath;
        Hash = hash;
        FileSize = fileSize;
        IsMissing = isMissing;
    }
}
