namespace DawVcs.Domain.Dependencies;

/// <summary>
/// Status van een lokale binding ten opzichte van de verwachte contenthash (FR-BND-002, FR-BND-003, FR-BND-005).
/// </summary>
public enum BindingStatus
{
    /// <summary>
    /// Er is nog geen geschikte lokale locator gevonden of geverifieerd.
    /// </summary>
    Unresolved,

    /// <summary>
    /// De bytes op de lokale locatie zijn geverifieerd tegen de verwachte hash (FR-BND-003).
    /// </summary>
    Verified,

    /// <summary>
    /// Bestand bestaat op de kandidaatlocatie maar de hash wijkt af van de verwachting (FR-BND-005).
    /// </summary>
    Mismatch,

    /// <summary>
    /// De geconfigureerde locator verwijst naar een niet-bestaand bestand of directory.
    /// </summary>
    Missing
}
