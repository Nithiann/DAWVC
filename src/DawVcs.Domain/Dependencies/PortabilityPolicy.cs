namespace DawVcs.Domain.Dependencies;

/// <summary>
/// Beleid voor herdistributie en opslag van een afhankelijkheid (FR-DEP-002, TD §21).
/// </summary>
public sealed record PortabilityPolicy(
    PortabilityMode Mode,
    string? Reason = null,
    bool IsRedistributable = false)
{
    public static PortabilityPolicy BundleDefault => new(PortabilityMode.Bundle, "Eigen projectbestand of opname.", IsRedistributable: true);
    public static PortabilityPolicy ReferenceOnlyDefault => new(PortabilityMode.ReferenceOnly, "Software binary of commerciële library.", IsRedistributable: false);
    public static PortabilityPolicy UserChoiceDefault => new(PortabilityMode.UserChoice, "Licentie- of locatieafhankelijk.", IsRedistributable: false);
    public static PortabilityPolicy ForbiddenDefault => new(PortabilityMode.Forbidden, "Niet toegestaan in projectbundels.", IsRedistributable: false);
    public static PortabilityPolicy UnknownDefault => new(PortabilityMode.Unknown, "Nog niet geclassificeerd.", IsRedistributable: false);
}
