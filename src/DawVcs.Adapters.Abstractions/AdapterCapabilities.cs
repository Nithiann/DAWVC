namespace DawVcs.Adapters.Abstractions;

/// <summary>
/// Bepaalt welke bewerkingen een DAW-adapter ondersteunt.
/// Garandeert dat v0.1 adapters nooit native write, merge of round-trip validation claimen (FR-FLP-009).
/// </summary>
public sealed record AdapterCapabilities
{
    public bool CanDetect { get; init; }
    public bool CanExtractMetadata { get; init; }
    public bool CanExtractSampleReferences { get; init; }
    public bool CanExtractPluginReferences { get; init; }
    public bool CanWriteNative { get; init; }
    public bool CanMergeNative { get; init; }
    public bool CanRoundTripValidate { get; init; }

    public AdapterCapabilities(
        bool canDetect = true,
        bool canExtractMetadata = true,
        bool canExtractSampleReferences = false,
        bool canExtractPluginReferences = false,
        bool canWriteNative = false,
        bool canMergeNative = false,
        bool canRoundTripValidate = false)
    {
        CanDetect = canDetect;
        CanExtractMetadata = canExtractMetadata;
        CanExtractSampleReferences = canExtractSampleReferences;
        CanExtractPluginReferences = canExtractPluginReferences;
        CanWriteNative = canWriteNative;
        CanMergeNative = canMergeNative;
        CanRoundTripValidate = canRoundTripValidate;
    }

    /// <summary>
    /// Standaard read-only inspectiemogelijkheden voor FL Studio v0.1.
    /// </summary>
    public static AdapterCapabilities ReadOnlyInspection => new(
        canDetect: true,
        canExtractMetadata: true,
        canExtractSampleReferences: true,
        canExtractPluginReferences: true,
        canWriteNative: false,
        canMergeNative: false,
        canRoundTripValidate: false);
}
