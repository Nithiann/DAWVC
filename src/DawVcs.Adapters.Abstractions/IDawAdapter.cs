namespace DawVcs.Adapters.Abstractions;

/// <summary>
/// Contract voor DAW-specifieke inspectie- en validatie-adapters.
/// </summary>
public interface IDawAdapter
{
    string DawName { get; }
    IReadOnlyCollection<string> SupportedExtensions { get; }
}
