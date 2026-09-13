namespace DawVcs.Adapters.Abstractions;

/// <summary>
/// Contract voor DAW-specifieke inspectie- en validatie-adapters.
/// </summary>
public interface IDawAdapter
{
    string DawName { get; }
    IReadOnlyCollection<string> SupportedExtensions { get; }

    /// <summary>
    /// Inspects a read-only stream to determine if it is a recognized project file and extracts basic metadata.
    /// </summary>
    Task<ProjectDetectionResult> DetectAsync(Stream stream, CancellationToken cancellationToken = default);
}

