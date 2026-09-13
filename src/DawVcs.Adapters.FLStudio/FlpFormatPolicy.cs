using DawVcs.Adapters.Abstractions;

namespace DawVcs.Adapters.FLStudio;

/// <summary>
/// Beheert de formaat- en versiebeleidsregels voor FL Studio projecten (IMP-0503).
/// Valideert FL Studio 2026.x (interne versie 25.x) en eerdere moderne versies (24.x, 21.x, 20.x).
/// Onbekende of toekomstige versies degraderen gecontroleerd naar Unsupported voor opaque fallback (FR-FLP-008).
/// </summary>
public static class FlpFormatPolicy
{
    public static (ProjectDetectionStatus Status, string? ReleaseName, string? Note) EvaluateVersion(string? versionString)
    {
        if (string.IsNullOrWhiteSpace(versionString))
        {
            return (
                ProjectDetectionStatus.Unsupported,
                null,
                "FLP header is geldig, maar er is geen herkenbaar versienummer gevonden in de binaire metadata.");
        }

        var clean = versionString.Trim();

        // 1. FL Studio 2026.x (interne versie 25.x) - Primaire MVP-doelversie
        if (clean.StartsWith("25.", StringComparison.OrdinalIgnoreCase))
        {
            return (ProjectDetectionStatus.Valid, "FL Studio 2026.x", "Ondersteund in primaire v0.1 baseline.");
        }

        // 2. FL Studio 2024.x (interne versie 24.x)
        if (clean.StartsWith("24.", StringComparison.OrdinalIgnoreCase))
        {
            return (ProjectDetectionStatus.Valid, "FL Studio 2024.x", "Ondersteund in v0.1 baseline.");
        }

        // 3. FL Studio 21.x (interne versie 21.x)
        if (clean.StartsWith("21.", StringComparison.OrdinalIgnoreCase))
        {
            return (ProjectDetectionStatus.Valid, "FL Studio 21.x", "Ondersteund in v0.1 baseline.");
        }

        // 4. FL Studio 20.x (interne versie 20.x)
        if (clean.StartsWith("20.", StringComparison.OrdinalIgnoreCase))
        {
            return (ProjectDetectionStatus.Valid, "FL Studio 20.x", "Ondersteund in v0.1 baseline.");
        }

        // 5. Toekomstige versies (bijv. 26.x+)
        if (int.TryParse(clean.Split('.')[0], out int major) && major >= 26)
        {
            return (
                ProjectDetectionStatus.Unsupported,
                $"FL Studio {major}.x (Future)",
                $"Nieuwere FL Studio-versie '{clean}' valt buiten de geverifieerde v0.1 baseline. Veilig behandeld als opaque project.");
        }

        // 6. Legacy versies (vóór FL Studio 20)
        return (
            ProjectDetectionStatus.Unsupported,
            $"FL Studio Legacy ({clean})",
            $"Oudere FL Studio-versie '{clean}' valt buiten de v0.1 baseline. Veilig behandeld als opaque project.");
    }
}
