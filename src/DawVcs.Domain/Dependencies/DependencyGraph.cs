namespace DawVcs.Domain.Dependencies;

/// <summary>
/// Aggregate root voor alle afhankelijkheden van een projectversie of snapshot (IMP-0601, TD §11.5).
/// </summary>
public sealed record DependencyGraph
{
    public static readonly DependencyGraph Empty = new([]);

    private readonly List<Dependency> _dependencies;

    public IReadOnlyList<Dependency> Dependencies => _dependencies.AsReadOnly();
    public IReadOnlyList<Dependency> All => Dependencies;

    public DependencyGraph(IEnumerable<Dependency>? dependencies = null)
    {
        _dependencies = dependencies != null ? new List<Dependency>(dependencies) : [];
    }

    public IEnumerable<Dependency> GetBundledDependencies() =>
        _dependencies.Where(d => d.Portability.Mode == PortabilityMode.Bundle);

    public IEnumerable<Dependency> GetReferenceOnlyDependencies() =>
        _dependencies.Where(d => d.Portability.Mode == PortabilityMode.ReferenceOnly);

    public IEnumerable<AssetDependency> GetMissingRequiredBundleDependencies() =>
        _dependencies
            .OfType<AssetDependency>()
            .Where(a => a.Portability.Mode == PortabilityMode.Bundle &&
                        a.Requirement == DependencyRequirement.Required &&
                        a.IsMissing);

    public long CalculateBundleByteSize() =>
        _dependencies
            .OfType<AssetDependency>()
            .Where(a => a.Portability.Mode == PortabilityMode.Bundle && !a.IsMissing)
            .Sum(a => a.FileSize ?? 0);

    public DependencyGraph Add(Dependency dependency)
    {
        ArgumentNullException.ThrowIfNull(dependency);
        var list = new List<Dependency>(_dependencies) { dependency };
        return new DependencyGraph(list);
    }
}
