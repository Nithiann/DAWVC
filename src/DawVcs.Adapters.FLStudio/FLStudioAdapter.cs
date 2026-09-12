using DawVcs.Adapters.Abstractions;

namespace DawVcs.Adapters.FLStudio;

/// <summary>
/// Read-only adapter voor inspectie en validatie van FL Studio (.flp) projecten.
/// </summary>
public sealed class FLStudioAdapter : IDawAdapter
{
    public string DawName => "FL Studio";

    public IReadOnlyCollection<string> SupportedExtensions { get; } = new[] { ".flp" };
}
