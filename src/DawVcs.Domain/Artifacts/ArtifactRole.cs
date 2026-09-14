namespace DawVcs.Domain.Artifacts;

/// <summary>
/// The logical role of a file entry within an artifact tree.
/// </summary>
public enum ArtifactRole
{
    PrimaryProjectFile = 1,
    ProjectMetadata = 2,
    ProjectAsset = 3,
    Cache = 4,
    Generated = 5,
    Unknown = 6
}
