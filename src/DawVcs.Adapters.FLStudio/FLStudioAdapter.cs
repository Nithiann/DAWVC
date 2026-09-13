using DawVcs.Adapters.Abstractions;

namespace DawVcs.Adapters.FLStudio;

/// <summary>
/// Read-only adapter voor inspectie en validatie van FL Studio (.flp) projecten.
/// </summary>
public sealed class FLStudioAdapter : IDawAdapter
{
    public string DawName => "FL Studio";

    public IReadOnlyCollection<string> SupportedExtensions { get; } = new[] { ".flp" };

    /// <summary>
    /// Inspects the stream in a bounded, read-only manner and extracts metadata without modifying source bytes.
    /// </summary>
    public async Task<ProjectDetectionResult> DetectAsync(Stream stream, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);

        try
        {
            var inspection = await FlpBinaryReader.InspectAsync(stream, cancellationToken).ConfigureAwait(false);

            var metadata = new Dictionary<string, string>
            {
                ["ChannelCount"] = inspection.Header.ChannelCount.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["Ppq"] = inspection.Header.Ppq.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["Format"] = inspection.Header.Format.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["SampleCount"] = inspection.SamplePaths.Count.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["PluginCount"] = inspection.PluginNames.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)
            };

            if (!string.IsNullOrEmpty(inspection.Title))
            {
                metadata["ProjectTitle"] = inspection.Title;
            }

            var findings = new List<string>
            {
                $"Detected FLhd header with {inspection.Header.ChannelCount} channels and {inspection.Header.Ppq} PPQ."
            };

            if (inspection.SamplePaths.Count > 0)
            {
                findings.Add($"Found {inspection.SamplePaths.Count} referenced sample path(s).");
            }

            if (inspection.PluginNames.Count > 0)
            {
                findings.Add($"Found {inspection.PluginNames.Count} referenced plugin name(s).");
            }

            var version = inspection.Version;
            if (string.IsNullOrWhiteSpace(version))
            {
                return ProjectDetectionResult.Unsupported(
                    DawName,
                    detectedVersion: null,
                    reason: "FLP header is valid, but no recognized version event was found in the initial metadata stream.",
                    metadata: metadata);
            }

            // FL Studio 2026.x corresponds to internal version 25.x (and 24.x)
            if (version.StartsWith("25.", StringComparison.OrdinalIgnoreCase) ||
                version.StartsWith("24.", StringComparison.OrdinalIgnoreCase) ||
                version.StartsWith("21.", StringComparison.OrdinalIgnoreCase) ||
                version.StartsWith("20.", StringComparison.OrdinalIgnoreCase))
            {
                return ProjectDetectionResult.Valid(
                    DawName,
                    detectedVersion: version,
                    confidence: 1.0,
                    metadata: metadata,
                    findings: findings);
            }

            // Future or unverified version -> Unsupported (safe for opaque fallback)
            return ProjectDetectionResult.Unsupported(
                DawName,
                detectedVersion: version,
                reason: $"FL Studio version '{version}' is not in the verified baseline. Treated safely as opaque project artifact.",
                metadata: metadata);
        }
        catch (InvalidFlpException ex)
        {
            return ProjectDetectionResult.Invalid(DawName, ex.Message);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return ProjectDetectionResult.Invalid(DawName, $"Error inspecting FLP stream: {ex.Message}");
        }
    }
}
