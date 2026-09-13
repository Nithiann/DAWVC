using DawVcs.Domain.Metadata;

namespace DawVcs.Adapters.Abstractions;

/// <summary>
/// Representeert een specifiek observatiebewijs dat bijdraagt aan het detectieresultaat en de confidence (FR-SCAN-003).
/// </summary>
public sealed record DetectionEvidence(
    string Category,
    string Description,
    bool Matched,
    ConfidenceLevel Confidence,
    string? Details = null);
