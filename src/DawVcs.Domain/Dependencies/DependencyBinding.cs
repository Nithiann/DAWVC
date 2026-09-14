using DawVcs.Domain.Hashing;

namespace DawVcs.Domain.Dependencies;

/// <summary>
/// Lokale koppeling van een gedeelde dependency-identity aan een machine-specifieke locator (FR-BND-001..009).
/// Bindings blijven lokaal en worden nooit in gedeelde manifests of objectidentities opgenomen (FR-BND-001, FR-BND-009).
/// </summary>
public sealed record DependencyBinding(
    DependencyId DependencyId,
    string Locator,
    BindingMethod Method,
    BindingStatus Status,
    ContentHash? VerifiedHash,
    DateTimeOffset BoundAt,
    string? Notes = null)
{
    public DependencyBinding WithStatus(BindingStatus newStatus, ContentHash? verifiedHash = null) =>
        this with { Status = newStatus, VerifiedHash = verifiedHash ?? VerifiedHash, BoundAt = DateTimeOffset.UtcNow };
}
