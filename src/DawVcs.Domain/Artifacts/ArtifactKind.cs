namespace DawVcs.Domain.Artifacts;

/// <summary>
/// The container shape of a native DAW project artifact.
/// </summary>
public enum ArtifactKind
{
    SingleFile = 1,
    Directory = 2,
    Package = 3,
    Archive = 4
}
