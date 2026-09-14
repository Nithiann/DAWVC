namespace DawVcs.Application.Diagnostics;

public sealed record FsckReport(
    int SchemaVersion,
    RepositoryIntegrityReport RepositoryIntegrity,
    ArtifactIntegrityReport? ArtifactIntegrity)
{
    public bool IsHealthy => RepositoryIntegrity.IsIntact && (ArtifactIntegrity == null || ArtifactIntegrity.IsIntact);
}
