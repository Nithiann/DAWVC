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

        var normAsset = NormalizePath(assetPath);
        var normWork = NormalizePath(workingDirectory);

        // 1. Controleer op systeempaden -> Forbidden of ReferenceOnly
        foreach (var sys in SystemPathPrefixes)
        {
            var normSys = NormalizePath(sys);
            if (normAsset.StartsWith(normSys, StringComparison.OrdinalIgnoreCase))
            {
                return new PortabilityPolicy(
                    PortabilityMode.ReferenceOnly,
                    "Geïnstalleerd systeembestand of programma-directory.",
                    IsRedistributable: false);
            }
        }

        // 2. Relatieve paden binnen de werkdirectory -> Bundle
        if (!IsRootedPath(normAsset))
        {
            return PortabilityPolicy.BundleDefault;
        }

        if (normAsset.StartsWith(normWork + "/", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(normAsset, normWork, StringComparison.OrdinalIgnoreCase))
        {
            return PortabilityPolicy.BundleDefault;
        }

        // 3. Externe paden: controleer op bekende commerciële trefwoorden -> UserChoice / ReferenceOnly
        var lower = normAsset.ToLowerInvariant();
        foreach (var keyword in CommercialLibraryKeywords)
        {
            if (lower.Contains(keyword, StringComparison.Ordinal))
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

    private static string NormalizePath(string path) =>
        path.Replace('\\', '/').TrimEnd('/');

    private static bool IsRootedPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        if (Path.IsPathRooted(path))
        {
            return true;
        }

        if (path.Length >= 2 && char.IsAsciiLetter(path[0]) && path[1] == ':')
        {
            return true;
        }

        return path.StartsWith('/') || path.StartsWith('\\');
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
