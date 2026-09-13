namespace DawVcs.Domain.Metadata;

/// <summary>
/// Zekerheidsniveau van een geëxtraheerde metadata-observatie.
/// </summary>
public enum ConfidenceLevel
{
    Exact,
    Verified,
    Probable,
    Inferred,
    Unknown
}
