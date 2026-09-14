using DawVcs.Domain.Metadata;

namespace DawVcs.Adapters.Abstractions;

/// <summary>
/// Gestructureerd resultaat van projectinspectie door een DAW-adapter (FR-SCAN-003, IMP-0504, IMP-0507).
/// </summary>
public sealed record ProjectDetectionResult
{
    public ProjectDetectionStatus Status { get; init; }
    public string DawName { get; init; }
    public string? DetectedVersion { get; init; }
    public double Confidence { get; init; }
    public IReadOnlyDictionary<string, string> Metadata { get; init; }
    public IReadOnlyList<string> Findings { get; init; }
    public IReadOnlyList<DetectionEvidence> Evidence { get; init; }
    public IReadOnlyList<MetadataObservation<string>> Observations { get; init; }

    /// <summary>
    /// Geeft aan of het project volledig semantisch ondersteund wordt.
    /// </summary>
    public bool IsSupported => Status == ProjectDetectionStatus.Valid;

    /// <summary>
    /// Geeft aan of het project veilig als opaque artifact behandeld moet worden (FR-FLP-008).
    /// </summary>
    public bool RequiresOpaqueFallback => Status is ProjectDetectionStatus.Unsupported or ProjectDetectionStatus.Suspicious;

    public ProjectDetectionResult(
        ProjectDetectionStatus status,
        string dawName,
        string? detectedVersion,
        double confidence,
        IReadOnlyDictionary<string, string>? metadata = null,
        IReadOnlyList<string>? findings = null,
        IReadOnlyList<DetectionEvidence>? evidence = null,
        IReadOnlyList<MetadataObservation<string>>? observations = null)
    {
        Status = status;
        DawName = dawName;
        DetectedVersion = detectedVersion;
        Confidence = Math.Clamp(confidence, 0.0, 1.0);
        Metadata = metadata ?? new Dictionary<string, string>();
        Findings = findings ?? Array.Empty<string>();
        Evidence = evidence ?? Array.Empty<DetectionEvidence>();
        Observations = observations ?? Array.Empty<MetadataObservation<string>>();
    }

    public static ProjectDetectionResult Valid(
        string dawName,
        string detectedVersion,
        double confidence = 1.0,
        IReadOnlyDictionary<string, string>? metadata = null,
        IEnumerable<string>? findings = null,
        IEnumerable<DetectionEvidence>? evidence = null,
        IEnumerable<MetadataObservation<string>>? observations = null)
    {
        return new ProjectDetectionResult(
            ProjectDetectionStatus.Valid,
            dawName,
            detectedVersion,
            confidence,
            metadata,
            findings?.ToList(),
            evidence?.ToList(),
            observations?.ToList());
    }

    public static ProjectDetectionResult Suspicious(
        string dawName,
        string? detectedVersion,
        string reason,
        double confidence = 0.6,
        IReadOnlyDictionary<string, string>? metadata = null,
        IEnumerable<DetectionEvidence>? evidence = null)
    {
        return new ProjectDetectionResult(
            ProjectDetectionStatus.Suspicious,
            dawName,
            detectedVersion,
            confidence,
            metadata,
            new[] { reason },
            evidence?.ToList());
    }

    public static ProjectDetectionResult Unsupported(
        string dawName,
        string? detectedVersion,
        string reason,
        IReadOnlyDictionary<string, string>? metadata = null,
        IEnumerable<DetectionEvidence>? evidence = null)
    {
        return new ProjectDetectionResult(
            ProjectDetectionStatus.Unsupported,
            dawName,
            detectedVersion,
            confidence: 0.8,
            metadata,
            new[] { reason },
            evidence?.ToList());
    }

    public static ProjectDetectionResult Invalid(
        string dawName,
        string reason,
        IEnumerable<DetectionEvidence>? evidence = null)
    {
        return new ProjectDetectionResult(
            ProjectDetectionStatus.Invalid,
            dawName,
            detectedVersion: null,
            confidence: 0.0,
            metadata: null,
            new[] { reason },
            evidence?.ToList());
    }

    public static ProjectDetectionResult Unknown(
        string reason,
        IEnumerable<DetectionEvidence>? evidence = null)
    {
        return new ProjectDetectionResult(
            ProjectDetectionStatus.Unknown,
            dawName: "Unknown",
            detectedVersion: null,
            confidence: 0.0,
            metadata: null,
            new[] { reason },
            evidence?.ToList());
    }
}
