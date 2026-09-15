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

    public IDawAdapter? FindAdapterForFile(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath)) return null;
        var ext = Path.GetExtension(filePath);
        return FindAdapterForExtension(ext);
    }

    public IReadOnlyCollection<IDawAdapter> GetAllAdapters()
    {
        return _adapters.ToArray();
    }

    public IReadOnlyList<string> FindCandidateProjectFiles(string directory)
    {
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
        {
            return Array.Empty<string>();
        }

        var results = new List<string>();
        foreach (var adapter in _adapters)
        {
            results.AddRange(adapter.DiscoverProjectFiles(directory));
        }

        return results.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(p => p).ToList();
    }
}
