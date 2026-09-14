namespace DawVcs.Application.Diagnostics;

public sealed record ArtifactIntegrityReport(
    int ArtifactsScanned,
    int ValidArtifactsCount,
    int FailedArtifactsCount,
    IReadOnlyList<IntegrityIssue> Errors,
    IReadOnlyList<IntegrityIssue> Warnings)
{
    public bool IsIntact => Errors.Count == 0;
}
