using DawVcs.Domain.Metadata;

namespace DawVcs.Domain.Dependencies;

/// <summary>
/// Abstracte basis voor projectafhankelijkheden (FR-DEP-001, FR-DEP-002, TD §11.5).
/// </summary>
public abstract record Dependency
{
    public DependencyId Id { get; init; }
    public DependencyKind Kind { get; init; }
    public string Name { get; init; }
    public DependencyRequirement Requirement { get; init; }
    public DependencySource Source { get; init; }
    public PortabilityPolicy Portability { get; init; }
    public MetadataObservation<string>? Provenance { get; init; }

    protected Dependency(
        DependencyId id,
        DependencyKind kind,
        string name,
        DependencyRequirement requirement,
        DependencySource source,
        PortabilityPolicy portability,
        MetadataObservation<string>? provenance = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(portability);

        Id = id;
        Kind = kind;
        Name = name;
        Requirement = requirement;
        Source = source;
        Portability = portability;
        Provenance = provenance;
    }
}
