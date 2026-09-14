namespace DawVcs.Domain.Dependencies;

/// <summary>
/// Bepaalt via welke resolver-methode een lokale binding tot stand is gekomen (FR-BND-004, TD §29).
/// </summary>
public enum BindingMethod
{
    /// <summary>
    /// Reeds gematerialiseerd projectasset in de actieve workspace of managed assetroot.
    /// </summary>
    RepositoryAsset,

    /// <summary>
    /// Eerder lokaal geverifieerde binding met kloppende hash.
    /// </summary>
    VerifiedBinding,

    /// <summary>
    /// Bestand gevonden op het verwachte relatieve pad binnen de workspace.
    /// </summary>
    RelativePath,

    /// <summary>
    /// Bestand gevonden op het oorspronkelijke absolute pad (diagnostische locator, FR-BND-008).
    /// </summary>
    OriginalPath,

    /// <summary>
    /// Standaard library-pad (bijv. VST3/content search directories).
    /// </summary>
    LibraryMapping,

    /// <summary>
    /// Gevonden via de workspace asset-index.
    /// </summary>
    AssetIndex,

    /// <summary>
    /// Automatisch ontdekt via gelijke BLAKE3-contenthash op een andere locatie (FR-BND-007).
    /// </summary>
    ContentHashDiscovery,

    /// <summary>
    /// Handmatig geselecteerd door de gebruiker via 'dawvc bind' (FR-BND-006).
    /// </summary>
    UserSelection,

    /// <summary>
    /// Niet automatisch of handmatig te resolven.
    /// </summary>
    Unresolved
}
