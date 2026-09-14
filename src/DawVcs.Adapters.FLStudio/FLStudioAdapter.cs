using System.Globalization;

using DawVcs.Adapters.Abstractions;
using DawVcs.Domain.Metadata;

namespace DawVcs.Adapters.FLStudio;

/// <summary>
/// Read-only adapter voor inspectie en validatie van FL Studio (.flp) projecten (FR-FLP-001 t/m FR-FLP-012, IMP-0501).
/// Garandeert strikte read-only toegang en declareert uitsluitend veilige inspectie-capabilities.
/// </summary>
public sealed class FLStudioAdapter : IDawAdapter
{
    public string DawName => "FL Studio";

    public string AdapterVersion => "0.1.0";

    public AdapterCapabilities Capabilities { get; } = AdapterCapabilities.ReadOnlyInspection;

    public IReadOnlyCollection<string> SupportedExtensions { get; } = new[] { ".flp" };

    /// <summary>
    /// Inspecteert een projectbestand via ArtifactReadContext en extraheert gestructureerde metadata en provenance.
    /// </summary>
    public async Task<ProjectDetectionResult> DetectAsync(
        ArtifactReadContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        var evidenceList = new List<DetectionEvidence>();
        var observations = new List<MetadataObservation<string>>();
        var findings = new List<string>();
        var metadata = new Dictionary<string, string>();
        var now = DateTimeOffset.UtcNow;

        // 1. Bewijs: Bestandsextensie
        bool hasFlpExtension = string.Equals(context.Extension, ".flp", StringComparison.OrdinalIgnoreCase);
        evidenceList.Add(new DetectionEvidence(
            Category: "FileExtension",
            Description: hasFlpExtension ? "Bestandsextensie is .flp" : $"Afwijkende bestandsextensie: '{context.Extension}'",
            Matched: hasFlpExtension,
            Confidence: hasFlpExtension ? ConfidenceLevel.Verified : ConfidenceLevel.Probable));

        try
        {
            await using var stream = context.OpenReadStream();

            var inspection = await FlpBinaryReader.InspectAsync(stream, cancellationToken).ConfigureAwait(false);

            // 2. Bewijs: FLhd Header
            evidenceList.Add(new DetectionEvidence(
                Category: "BinaryHeader",
                Description: "FLhd header geverifieerd (magic 0x46 0x4C 0x68 0x64).",
                Matched: true,
                Confidence: ConfidenceLevel.Exact,
                Details: $"Format={inspection.Header.Format}, Channels={inspection.Header.ChannelCount}, PPQ={inspection.Header.Ppq}"));

            // 3. Bewijs: FLdt Data Chunk
            evidenceList.Add(new DetectionEvidence(
                Category: "DataChunk",
                Description: "FLdt data chunk header geverifieerd (magic 0x46 0x4C 0x64 0x74).",
                Matched: true,
                Confidence: ConfidenceLevel.Exact,
                Details: $"{inspection.BytesScanned} bytes metadata begrends gescand."));

            // Metadata & Provenance observaties
            metadata["ChannelCount"] = inspection.Header.ChannelCount.ToString(CultureInfo.InvariantCulture);
            metadata["Ppq"] = inspection.Header.Ppq.ToString(CultureInfo.InvariantCulture);
            metadata["Format"] = inspection.Header.Format.ToString(CultureInfo.InvariantCulture);
            metadata["SampleCount"] = inspection.SamplePaths.Count.ToString(CultureInfo.InvariantCulture);
            metadata["PluginCount"] = inspection.PluginNames.Count.ToString(CultureInfo.InvariantCulture);

            observations.Add(new MetadataObservation<string>(
                inspection.Header.ChannelCount.ToString(CultureInfo.InvariantCulture),
                MetadataSource.NativeProjectParser,
                ConfidenceLevel.Exact,
                now,
                AdapterVersion));

            observations.Add(new MetadataObservation<string>(
                inspection.Header.Ppq.ToString(CultureInfo.InvariantCulture),
                MetadataSource.NativeProjectParser,
                ConfidenceLevel.Exact,
                now,
                AdapterVersion));

            findings.Add($"Detected FLhd header with {inspection.Header.ChannelCount} channels and {inspection.Header.Ppq} PPQ.");

            if (!string.IsNullOrEmpty(inspection.Title))
            {
                metadata["ProjectTitle"] = inspection.Title;
                findings.Add($"Project title: '{inspection.Title}'.");
                observations.Add(new MetadataObservation<string>(
                    inspection.Title,
                    MetadataSource.NativeProjectParser,
                    ConfidenceLevel.Verified,
                    now,
                    AdapterVersion));
            }

            if (!string.IsNullOrEmpty(inspection.Comments))
            {
                metadata["ProjectComments"] = inspection.Comments;
            }

            if (inspection.TempoBpm.HasValue)
            {
                metadata["TempoBpm"] = inspection.TempoBpm.Value.ToString(CultureInfo.InvariantCulture);
                findings.Add($"Project tempo: {inspection.TempoBpm.Value} BPM.");
                observations.Add(new MetadataObservation<string>(
                    inspection.TempoBpm.Value.ToString(CultureInfo.InvariantCulture),
                    MetadataSource.NativeProjectParser,
                    ConfidenceLevel.Verified,
                    now,
                    AdapterVersion));
            }

            if (inspection.SamplePaths.Count > 0)
            {
                findings.Add($"Found {inspection.SamplePaths.Count} referenced sample path(s).");
                metadata["SamplePaths"] = string.Join(";", inspection.SamplePaths);
                foreach (var sample in inspection.SamplePaths)
                {
                    observations.Add(new MetadataObservation<string>(
                        sample,
                        MetadataSource.NativeProjectParser,
                        ConfidenceLevel.Verified,
                        now,
                        AdapterVersion));
                }
            }

            if (inspection.PluginNames.Count > 0)
            {
                findings.Add($"Found {inspection.PluginNames.Count} referenced plugin name(s).");
                metadata["PluginNames"] = string.Join(";", inspection.PluginNames);
                foreach (var plugin in inspection.PluginNames)
                {
                    observations.Add(new MetadataObservation<string>(
                        plugin,
                        MetadataSource.NativeProjectParser,
                        ConfidenceLevel.Verified,
                        now,
                        AdapterVersion));
                }
            }

            // 4. Bewijs & Evaluatie van FL Studio versie via format policy
            var (policyStatus, releaseName, policyNote) = FlpFormatPolicy.EvaluateVersion(inspection.Version);

            if (inspection.Version != null)
            {
                metadata["Version"] = inspection.Version;
                if (releaseName != null)
                {
                    metadata["ReleaseName"] = releaseName;
                }

                evidenceList.Add(new DetectionEvidence(
                    Category: "VersionEvent",
                    Description: $"FL Studio versie event gevonden: '{inspection.Version}' ({releaseName ?? "Onbekend"}).",
                    Matched: true,
                    Confidence: ConfidenceLevel.Verified,
                    Details: policyNote));

                observations.Add(new MetadataObservation<string>(
                    inspection.Version,
                    MetadataSource.NativeProjectParser,
                    ConfidenceLevel.Verified,
                    now,
                    AdapterVersion));
            }
            else
            {
                evidenceList.Add(new DetectionEvidence(
                    Category: "VersionEvent",
                    Description: "Geen specifiek versienummer-event (199) aangetroffen in begrensde metadata scan.",
                    Matched: false,
                    Confidence: ConfidenceLevel.Unknown));
            }

            // 5. Structurele anomalie -> Suspicious (IMP-0507)
            if (inspection.IsSuspicious)
            {
                findings.Add($"Waarschuwing: {inspection.SuspiciousReason}");
                return ProjectDetectionResult.Suspicious(
                    DawName,
                    inspection.Version,
                    inspection.SuspiciousReason ?? "Anomalie waargenomen in FLP binaire structuur.",
                    confidence: 0.6,
                    metadata: metadata,
                    evidence: evidenceList);
            }

            // 6. Policy evaluatie
            if (policyStatus == ProjectDetectionStatus.Valid)
            {
                return ProjectDetectionResult.Valid(
                    DawName,
                    inspection.Version ?? "2026.x",
                    confidence: 1.0,
                    metadata: metadata,
                    findings: findings,
                    evidence: evidenceList,
                    observations: observations);
            }

            // 7. Unsupported (veilige fallback naar opaque)
            return ProjectDetectionResult.Unsupported(
                DawName,
                inspection.Version,
                policyNote ?? "FL Studio versie niet ondersteund in v0.1 baseline.",
                metadata: metadata,
                evidence: evidenceList);
        }
        catch (InvalidFlpException ex)
        {
            evidenceList.Add(new DetectionEvidence(
                Category: "BinaryValidation",
                Description: ex.Message,
                Matched: false,
                Confidence: ConfidenceLevel.Exact));

            return ProjectDetectionResult.Invalid(DawName, ex.Message, evidenceList);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            evidenceList.Add(new DetectionEvidence(
                Category: "BinaryValidation",
                Description: $"Onverwachte fout tijdens FLP stream inspectie: {ex.Message}",
                Matched: false,
                Confidence: ConfidenceLevel.Unknown));

            return ProjectDetectionResult.Invalid(DawName, $"Fout bij inspecteren van FLP stream: {ex.Message}", evidenceList);
        }
    }

    /// <summary>
    /// Overload voor het inspecteren van een stream direct (IMP-0501).
    /// </summary>
    public async Task<ProjectDetectionResult> DetectAsync(
        Stream stream,
        CancellationToken cancellationToken = default)
    {
        using var context = new ArtifactReadContext(stream, leaveOpen: true);
        return await DetectAsync(context, cancellationToken).ConfigureAwait(false);
    }
}
