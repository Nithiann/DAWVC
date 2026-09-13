namespace DawVcs.Adapters.Abstractions;

/// <summary>
/// Contract voor DAW-specifieke inspectie- en validatie-adapters (IMP-0501).
/// </summary>
public interface IDawAdapter
{
    string DawName { get; }
    string AdapterVersion { get; }
    AdapterCapabilities Capabilities { get; }
    IReadOnlyCollection<string> SupportedExtensions { get; }

    /// <summary>
    /// Inspecteert een projectbestand via een strikt read-only context (FR-FLP-002).
    /// </summary>
    Task<ProjectDetectionResult> DetectAsync(ArtifactReadContext context, CancellationToken cancellationToken = default);

    /// <summary>
    /// Inspecteert een read-only stream direct.
    /// </summary>
    Task<ProjectDetectionResult> DetectAsync(Stream stream, CancellationToken cancellationToken = default);
}
