namespace DawVcs.Application.Diagnostics;

public enum HealthStatus
{
    Healthy,
    Warning,
    Error
}

public sealed record ArtifactHealth(
    string Path,
    string DawName,
    string? DetectedVersion,
    HealthStatus Status,
    bool IsBlocking,
    IReadOnlyList<string> Findings);
