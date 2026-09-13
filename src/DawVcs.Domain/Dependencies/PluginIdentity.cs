namespace DawVcs.Domain.Dependencies;

/// <summary>
/// Stabiele identiteit van een plugin (FR-DEP-004, TD §11.7).
/// Installatiepaden op de lokale machine maken expliciet GEEN deel uit van de identiteit (FR-DEP-005).
/// </summary>
public sealed record PluginIdentity(
    string Vendor,
    string Product,
    PluginFormat Format,
    string? NativeIdentifier = null)
{
    public override string ToString() => $"{Vendor} - {Product} ({Format})";
}
