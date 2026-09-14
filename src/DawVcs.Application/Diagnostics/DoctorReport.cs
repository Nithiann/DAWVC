namespace DawVcs.Application.Diagnostics;

public sealed record DoctorReport(
    int SchemaVersion,
    bool IsHealthy,
    bool HasWarnings,
    double ReproducibilityScore,
    IReadOnlyList<ArtifactHealth> ArtifactHealth,
    IReadOnlyList<DependencyHealth> DependencyHealth,
    EnvironmentHealth EnvironmentHealth,
    int BlockingIssuesCount,
    int WarningsCount);
