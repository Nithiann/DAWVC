using DawVcs.Domain.Dependencies;

namespace DawVcs.Application.Dependencies;

/// <summary>
/// Evalueert en kent portability policies toe aan bestanden en plugins (FR-DEP-006 t/m FR-DEP-008, TD §21).
/// </summary>
public static class PortabilityPolicyEngine
{
    private static readonly string[] SystemPathPrefixes =
    [
        "C:\\Windows",
        "C:\\Program Files",
        "C:\\Program Files (x86)",
        "/usr/",
        "/System/",
        "/Library/"
    ];

    private static readonly string[] CommercialLibraryKeywords =
    [
        "kontakt",
        "omnisphere",
        "nexus",
        "splice",
        "loopcloud",
        "samplepack",
        "komplete"
    ];

    /// <summary>
    /// Bepaalt de portability policy voor een asset op basis van relatieve/absolute locatie en werkdirectory.
    /// </summary>
    public static PortabilityPolicy DetermineAssetPolicy(string assetPath, string workingDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(assetPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);

        // 1. Controleer op systeempaden -> Forbidden of ReferenceOnly
        foreach (var sys in SystemPathPrefixes)
        {
            if (assetPath.StartsWith(sys, StringComparison.OrdinalIgnoreCase))
            {
                return new PortabilityPolicy(
                    PortabilityMode.ReferenceOnly,
                    "Geïnstalleerd systeembestand of programma-directory.",
                    IsRedistributable: false);
            }
        }

        // 2. Relatieve paden binnen de werkdirectory -> Bundle
        if (!Path.IsPathRooted(assetPath))
        {
            return PortabilityPolicy.BundleDefault;
        }

        var fullWorkingDir = Path.GetFullPath(workingDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var fullAssetPath = Path.GetFullPath(assetPath);

        if (fullAssetPath.StartsWith(fullWorkingDir + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            return PortabilityPolicy.BundleDefault;
        }

        // 3. Externe paden: controleer op bekende commerciële trefwoorden -> UserChoice / ReferenceOnly
        var lower = fullAssetPath.ToLowerInvariant();
        foreach (var keyword in CommercialLibraryKeywords)
        {
            if (lower.Contains(keyword))
            {
                return new PortabilityPolicy(
                    PortabilityMode.UserChoice,
                    $"Mogelijk commerciële samplelibrary ({keyword}). Vereist gebruikerskeuze.",
                    IsRedistributable: false);
            }
        }

        // 4. Overige externe paden -> ReferenceOnly (externe schijf of gedeelde folder buiten project)
        return new PortabilityPolicy(
            PortabilityMode.ReferenceOnly,
            "Externe locatie buiten werkdirectory. Standaard ReferenceOnly.",
            IsRedistributable: false);
    }

    /// <summary>
    /// Bepaalt de portability policy voor plugins (principieel altijd ReferenceOnly, FR-DEP-006).
    /// </summary>
    public static PortabilityPolicy DeterminePluginPolicy(PluginIdentity plugin)
    {
        ArgumentNullException.ThrowIfNull(plugin);
        return new PortabilityPolicy(
            PortabilityMode.ReferenceOnly,
            "Plugin binaries mogen nooit gebundeld of herdistribueerd worden (FR-DEP-006).",
            IsRedistributable: false);
    }
}
