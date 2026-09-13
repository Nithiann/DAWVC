namespace DawVcs.Adapters.Abstractions;

/// <summary>
/// Status van project- en formaatdetectie door een DAW-adapter (IMP-0507).
/// </summary>
public enum ProjectDetectionStatus
{
    /// <summary>
    /// Project is positief geïdentificeerd en wordt ondersteund door de adapter.
    /// </summary>
    Valid,

    /// <summary>
    /// Project bezit een geldige header/handtekening, maar vertoont verdachte afwijkingen of niet-fatale corruptie.
    /// </summary>
    Suspicious,

    /// <summary>
    /// Project is herkend als het verwachte DAW-formaat, maar de specifieke versie valt buiten de geverifieerde baseline.
    /// Veilig te behandelen als een opaque project artifact (FR-FLP-008).
    /// </summary>
    Unsupported,

    /// <summary>
    /// Handtekening of binaire structuur is fundamenteel ongeldig of corrupt.
    /// </summary>
    Invalid,

    /// <summary>
    /// Onvoldoende bewijs of onbekend formaat.
    /// </summary>
    Unknown
}
