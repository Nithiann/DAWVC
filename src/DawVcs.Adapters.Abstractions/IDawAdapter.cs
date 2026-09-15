using DawVcs.Domain.Dependencies;

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

    /// <summary>
    /// Geeft aan of de adapter een bestandsextensie of pad ondersteunt.
    /// </summary>
    bool CanHandle(string fileNameOrPath);

    /// <summary>
    /// Ontdekt projectbestanden van deze DAW binnen de opgegeven directory (top-level).
    /// </summary>
    IReadOnlyList<string> DiscoverProjectFiles(string directory);

    /// <summary>
    /// Probeert lokale installaties en omgevingsstatus van deze DAW te detecteren (FR-DOC-003).
    /// </summary>
    Task<IReadOnlyList<DawEnvironmentFinding>> ProbeEnvironmentAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Normaliseert een ruwe pluginaanduiding naar een formele PluginIdentity.
    /// </summary>
    PluginIdentity NormalizePlugin(string rawName);

    /// <summary>
    /// Bepaalt of een plugin native/ingebouwd is voor deze DAW.
    /// </summary>
    bool IsNativePlugin(string pluginName);

    /// <summary>
    /// Bepaalt de rol (instrument of effect) voor een plugin.
    /// </summary>
    PluginRole DeterminePluginRole(string rawName);
}
