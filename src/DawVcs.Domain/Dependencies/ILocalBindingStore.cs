namespace DawVcs.Domain.Dependencies;

/// <summary>
/// Domain port voor het opslaan en laden van lokale dependency bindings (FR-BND-001, FR-BND-009).
/// Bindings blijven strict lokaal binnen de actieve workspace.
/// </summary>
public interface ILocalBindingStore
{
    Task<IReadOnlyList<DependencyBinding>> GetAllAsync(CancellationToken cancellationToken = default);

    Task<DependencyBinding?> GetAsync(DependencyId id, CancellationToken cancellationToken = default);

    Task SaveAsync(DependencyBinding binding, CancellationToken cancellationToken = default);

    Task SaveAllAsync(IEnumerable<DependencyBinding> bindings, CancellationToken cancellationToken = default);

    Task RemoveAsync(DependencyId id, CancellationToken cancellationToken = default);
}
