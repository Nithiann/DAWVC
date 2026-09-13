using DawVcs.Adapters.Abstractions;

namespace DawVcs.Application.Adapters;

/// <summary>
/// Beheert geregistreerde DAW adapters en faciliteert selectie op basis van extensie (IMP-0510).
/// </summary>
public interface IDawAdapterRegistry
{
    void Register(IDawAdapter adapter);
    IDawAdapter? FindAdapterForExtension(string extension);
    IReadOnlyCollection<IDawAdapter> GetAllAdapters();
}
