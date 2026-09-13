namespace DawVcs.Adapters.Abstractions;

/// <summary>
/// Structured result returned by DAW adapters after inspecting a candidate project file or directory.
/// </summary>
public sealed record ProjectDetectionResult
{
    public ProjectDetectionStatus Status { get; init; }
    public string DawName { get; init; }
    public string? DetectedVersion { get; init; }
    public double Confidence { get; init; }
    public IReadOnlyDictionary<string, string> Metadata { get; init; }
    public IReadOnlyList<string> Findings { get; init; }

    public bool IsSupported => Status == ProjectDetectionStatus.Valid;

    public ProjectDetectionResult(
        ProjectDetectionStatus status,
        string dawName,
        string? detectedVersion,
        double confidence,
        IReadOnlyDictionary<string, string>? metadata = null,
        IReadOnlyList<string>? findings = null)
    {
        Status = status;
        DawName = dawName;
        DetectedVersion = detectedVersion;
        Confidence = Math.Clamp(confidence, 0.0, 1.0);
        Metadata = metadata ?? new Dictionary<string, string>();
        Findings = findings ?? Array.Empty<string>();
    }

    public static ProjectDetectionResult Valid(
        string dawName,
        string detectedVersion,
        double confidence = 1.0,
        IReadOnlyDictionary<string, string>? metadata = null,
        IEnumerable<string>? findings = null)
    {
        return new ProjectDetectionResult(
            ProjectDetectionStatus.Valid,
            dawName,
            detectedVersion,
            confidence,
            metadata,
            findings?.ToList());
    }

    public static ProjectDetectionResult Unsupported(
        string dawName,
        string? detectedVersion,
        string reason,
        IReadOnlyDictionary<string, string>? metadata = null)
    {
        return new ProjectDetectionResult(
            ProjectDetectionStatus.Unsupported,
            dawName,
            detectedVersion,
            confidence: 0.8,
            metadata,
            new[] { reason });
    }

    public static ProjectDetectionResult Invalid(string dawName, string reason)
    {
        return new ProjectDetectionResult(
            ProjectDetectionStatus.Invalid,
            dawName,
            detectedVersion: null,
            confidence: 0.0,
            metadata: null,
            new[] { reason });
    }

    public static ProjectDetectionResult Unknown(string reason)
    {
        return new ProjectDetectionResult(
            ProjectDetectionStatus.Unknown,
            dawName: "Unknown",
            detectedVersion: null,
            confidence: 0.0,
            metadata: null,
            new[] { reason });
    }
}
