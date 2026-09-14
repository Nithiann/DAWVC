namespace DawVcs.Application.Diagnostics;

public sealed record DawInstallation(
    string DawName,
    string Version,
    string? ExecutablePath,
    bool IsDetected);

public sealed record EnvironmentHealth(
    string OperatingSystem,
    string DotNetVersion,
    string Architecture,
    IReadOnlyList<DawInstallation> DawInstallations,
    IReadOnlyList<string> Findings);
