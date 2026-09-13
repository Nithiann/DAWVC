namespace DawVcs.Domain.Metadata;

/// <summary>
/// Legt een geobserveerde metadata-waarde vast inclusief herkomst, betrouwbaarheid en tijdstip (FR-FLP-007).
/// </summary>
public sealed record MetadataObservation<T>(
    T Value,
    MetadataSource Source,
    ConfidenceLevel Confidence,
    DateTimeOffset ObservedAt,
    string? AdapterVersion);
