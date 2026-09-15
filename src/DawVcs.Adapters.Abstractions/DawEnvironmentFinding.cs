namespace DawVcs.Adapters.Abstractions;

/// <summary>
/// Resultaat van lokale omgevingsdetectie voor een specifieke DAW (FR-DOC-003).
/// </summary>
public sealed record DawEnvironmentFinding(
    string DawName,
    string? Version,
    string? InstallationPath,
    bool IsInstalled,
    string? StatusMessage);
