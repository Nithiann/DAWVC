using System.Collections.Concurrent;

using DawVcs.Adapters.Abstractions;

namespace DawVcs.Application.Adapters;

/// <summary>
/// Thread-safe implementatie van IDawAdapterRegistry.
/// </summary>
public sealed class DawAdapterRegistry : IDawAdapterRegistry
{
    private readonly ConcurrentDictionary<string, IDawAdapter> _extensionMap = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentBag<IDawAdapter> _adapters = new();

    public DawAdapterRegistry(IEnumerable<IDawAdapter>? initialAdapters = null)
    {
        if (initialAdapters != null)
        {
            foreach (var adapter in initialAdapters)
            {
                Register(adapter);
            }
        }
    }

    public void Register(IDawAdapter adapter)
    {
        ArgumentNullException.ThrowIfNull(adapter);
        _adapters.Add(adapter);
        foreach (var ext in adapter.SupportedExtensions)
        {
            _extensionMap[ext] = adapter;
        }
    }

    public IDawAdapter? FindAdapterForExtension(string extension)
    {
        if (string.IsNullOrWhiteSpace(extension)) return null;
        var normalized = extension.StartsWith('.') ? extension : "." + extension;
        return _extensionMap.TryGetValue(normalized, out var adapter) ? adapter : null;
    }

    public IReadOnlyCollection<IDawAdapter> GetAllAdapters()
    {
        return _adapters.ToArray();
    }
}
