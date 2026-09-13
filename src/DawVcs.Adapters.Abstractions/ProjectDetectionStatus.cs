namespace DawVcs.Adapters.Abstractions;

public enum ProjectDetectionStatus
{
    /// <summary>
    /// Project is positively identified and supported by the adapter.
    /// </summary>
    Valid,

    /// <summary>
    /// Project is identified as the expected DAW format, but the specific version is not supported for full semantic parsing.
    /// Safe to treat as an opaque project artifact.
    /// </summary>
    Unsupported,

    /// <summary>
    /// Project signature or binary structure is corrupted or invalid.
    /// </summary>
    Invalid,

    /// <summary>
    /// Insufficient evidence or unrecognized format.
    /// </summary>
    Unknown
}
