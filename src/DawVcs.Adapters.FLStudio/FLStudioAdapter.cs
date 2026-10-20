using System.Globalization;

using DawVcs.Adapters.Abstractions;
using DawVcs.Adapters.FLStudio.Diagnostics;
using DawVcs.Domain.Dependencies;
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
            metadata["PluginCount"] = Math.Max(inspection.Plugins?.Count ?? 0, inspection.PluginNames.Count).ToString(CultureInfo.InvariantCulture);

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

            if (inspection.Plugins != null && inspection.Plugins.Count > 0)
            {
                findings.Add($"Found {inspection.Plugins.Count} referenced plugin occurrence(s).");
                metadata["PluginOccurrences"] = System.Text.Json.JsonSerializer.Serialize(inspection.Plugins);
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
                    evidence: evidenceList) with
                {
                    Completeness = inspection.Completeness,
                    CompletenessReason = inspection.CompletenessReason
                };
            }

            // 6. Policy evaluatie
            if (policyStatus == ProjectDetectionStatus.Valid)
            {
                if (inspection.Completeness == InspectionCompleteness.Partial)
                {
                    findings.Add($"Partial inspection: {inspection.CompletenessReason}");
                }

                return ProjectDetectionResult.Valid(
                    DawName,
                    inspection.Version ?? "2026.x",
                    confidence: 1.0,
                    metadata: metadata,
                    findings: findings,
                    evidence: evidenceList,
                    observations: observations) with
                {
                    Completeness = inspection.Completeness,
                    CompletenessReason = inspection.CompletenessReason
                };
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

    public bool CanHandle(string fileNameOrPath)
    {
        if (string.IsNullOrWhiteSpace(fileNameOrPath)) return false;
        return fileNameOrPath.EndsWith(".flp", StringComparison.OrdinalIgnoreCase);
    }

    public IReadOnlyList<string> DiscoverProjectFiles(string directory)
    {
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
        {
            return Array.Empty<string>();
        }

        return Directory.GetFiles(directory, "*.flp", SearchOption.TopDirectoryOnly);
    }

    public Task<IReadOnlyList<DawEnvironmentFinding>> ProbeEnvironmentAsync(CancellationToken cancellationToken = default)
    {
        return FlStudioEnvironmentProbe.ProbeAsync(cancellationToken);
    }

    private static readonly HashSet<string> NativeFlPlugins = new(StringComparer.OrdinalIgnoreCase)
    {
        "Sampler", "3x Osc", "Fruity Parametric EQ 2", "Fruity Reeverb 2",
        "Fruity Limiter", "Fruity Compressor", "Sytrus", "Harmor", "Harmless",
        "Gross Beat", "Fruity Delay 3", "Fruity Chorus", "Maximus", "Edison",
        "Slicex", "Vocodex", "Fruity Flanger", "Fruity Phaser", "Soundgoodizer"
    };

    private static readonly Dictionary<string, (string Vendor, PluginRole Role, string CanonicalProduct)> KnownThirdPartyPlugins = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Serum"] = ("Xfer Records", PluginRole.Instrument, "Serum"),
        ["Serum_x64"] = ("Xfer Records", PluginRole.Instrument, "Serum"),
        ["FabFilter Pro-Q 3"] = ("FabFilter", PluginRole.Effect, "FabFilter Pro-Q 3"),
        ["FabFilter Pro-C 2"] = ("FabFilter", PluginRole.Effect, "FabFilter Pro-C 2"),
        ["FabFilter Pro-L 2"] = ("FabFilter", PluginRole.Effect, "FabFilter Pro-L 2"),
        ["Sylenth1"] = ("LennarDigital", PluginRole.Instrument, "Sylenth1"),
        ["Sylenth1_x64"] = ("LennarDigital", PluginRole.Instrument, "Sylenth1"),
        ["Massive"] = ("Native Instruments", PluginRole.Instrument, "Massive"),
        ["Kontakt"] = ("Native Instruments", PluginRole.Instrument, "Kontakt"),
        ["Ozone"] = ("iZotope", PluginRole.Effect, "Ozone"),
        ["ValhallaVintageVerb"] = ("Valhalla DSP", PluginRole.Effect, "ValhallaVintageVerb"),
        ["Vital"] = ("Vital Audio", PluginRole.Instrument, "Vital"),
        ["Kickstart"] = ("Cableguys", PluginRole.Effect, "Kickstart"),
        ["Kickstart 2"] = ("Cableguys", PluginRole.Effect, "Kickstart 2"),
        ["OTT"] = ("Xfer Records", PluginRole.Effect, "OTT")
    };

    public bool IsNativePlugin(string pluginName)
    {
        if (string.IsNullOrWhiteSpace(pluginName)) return false;
        var clean = pluginName.Trim();
        if (clean.EndsWith(".vst3", StringComparison.OrdinalIgnoreCase)) clean = clean[..^5];
        else if (clean.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)) clean = clean[..^4];
        return NativeFlPlugins.Contains(clean);
    }

    public PluginIdentity NormalizePlugin(string rawName)
    {
        var clean = rawName.Trim();
        var format = PluginFormat.Unknown;

        if (clean.EndsWith(".vst3", StringComparison.OrdinalIgnoreCase))
        {
            format = PluginFormat.VST3;
            clean = clean[..^5];
        }
        else if (clean.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
        {
            format = PluginFormat.VST2;
            clean = clean[..^4];
        }

        if (NativeFlPlugins.Contains(clean))
        {
            return new PluginIdentity("Image-Line", clean, PluginFormat.Native);
        }

        if (KnownThirdPartyPlugins.TryGetValue(clean, out var known))
        {
            return new PluginIdentity(known.Vendor, known.CanonicalProduct, format != PluginFormat.Unknown ? format : PluginFormat.VST3);
        }

        var normalizedProduct = clean;
        if (normalizedProduct.EndsWith("_x64", StringComparison.OrdinalIgnoreCase))
        {
            normalizedProduct = normalizedProduct[..^4];
        }
        else if (normalizedProduct.EndsWith("_x86", StringComparison.OrdinalIgnoreCase))
        {
            normalizedProduct = normalizedProduct[..^4];
        }

        if (KnownThirdPartyPlugins.TryGetValue(normalizedProduct, out var knownNorm))
        {
            return new PluginIdentity(knownNorm.Vendor, knownNorm.CanonicalProduct, format != PluginFormat.Unknown ? format : PluginFormat.VST3);
        }

        return new PluginIdentity("Unknown", normalizedProduct, format);
    }

    public PluginRole DeterminePluginRole(string rawName)
    {
        if (KnownThirdPartyPlugins.TryGetValue(rawName, out var known))
        {
            return known.Role;
        }

        var clean = rawName;
        if (clean.EndsWith(".vst3", StringComparison.OrdinalIgnoreCase)) clean = clean[..^5];
        else if (clean.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)) clean = clean[..^4];

        if (KnownThirdPartyPlugins.TryGetValue(clean, out var knownClean))
        {
            return knownClean.Role;
        }

        var lower = rawName.ToLowerInvariant();
        if (lower.Contains("eq") || lower.Contains("verb") || lower.Contains("delay") ||
            lower.Contains("filter") || lower.Contains("limiter") || lower.Contains("compressor"))
        {
            return PluginRole.Effect;
        }

        return PluginRole.Instrument;
    }

    public IReadOnlyList<string> GetCheckoutGuidance(string managedAssetRoot, IReadOnlyList<string> restoredFiles)
    {
        return
        [
            $"FL Studio configuration: Add '{managedAssetRoot}' to Options > File Settings > Browser extra search folders to ensure all samples load automatically."
        ];
    }

    public (bool Found, string? Path, string? DetectedVersion) ProbePluginInstallation(PluginDependency plugin)
    {
        ArgumentNullException.ThrowIfNull(plugin);

        // 1. Native Image-Line / FL Studio plugins
        if (plugin.Plugin.Format == PluginFormat.Native ||
            plugin.Plugin.Vendor.Equals("Image-Line", StringComparison.OrdinalIgnoreCase) ||
            IsNativePlugin(plugin.Plugin.Product))
        {
            return (true, "Native FL Studio Plugin", null);
        }

        // 2. Exact expected file path from project metadata (Event 213 subchunk 55)
        if (!string.IsNullOrWhiteSpace(plugin.ExpectedFilePath))
        {
            var expPath = plugin.ExpectedFilePath;
            if (File.Exists(expPath) || Directory.Exists(expPath))
            {
                var bin = FindBinaryPath(expPath);
                var version = bin != null ? GetFileVersion(bin) : null;
                return (true, expPath, version);
            }
        }

        var product = plugin.Plugin.Product;
        var vendor = plugin.Plugin.Vendor;

        // 3. Common VST3 search paths on Windows
        var vst3Roots = new List<string>
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonProgramFiles), "VST3"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonProgramFilesX86), "VST3"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Common", "VST3")
        };

        foreach (var root in vst3Roots.Where(Directory.Exists))
        {
            var direct = Path.Combine(root, $"{product}.vst3");
            if (File.Exists(direct) || Directory.Exists(direct))
            {
                var bin = FindBinaryPath(direct);
                return (true, direct, bin != null ? GetFileVersion(bin) : null);
            }

            if (!string.IsNullOrWhiteSpace(vendor) && !vendor.Equals("Unknown", StringComparison.OrdinalIgnoreCase))
            {
                var vendorDirect = Path.Combine(root, vendor, $"{product}.vst3");
                if (File.Exists(vendorDirect) || Directory.Exists(vendorDirect))
                {
                    var bin = FindBinaryPath(vendorDirect);
                    return (true, vendorDirect, bin != null ? GetFileVersion(bin) : null);
                }
            }

            var prodSub = Path.Combine(root, product, $"{product}.vst3");
            if (File.Exists(prodSub) || Directory.Exists(prodSub))
            {
                var bin = FindBinaryPath(prodSub);
                return (true, prodSub, bin != null ? GetFileVersion(bin) : null);
            }

            try
            {
                foreach (var sub in Directory.EnumerateDirectories(root))
                {
                    var candidate = Path.Combine(sub, $"{product}.vst3");
                    if (File.Exists(candidate) || Directory.Exists(candidate))
                    {
                        var bin = FindBinaryPath(candidate);
                        return (true, candidate, bin != null ? GetFileVersion(bin) : null);
                    }
                }
            }
            catch
            {
                // Best effort
            }
        }

        // 4. Common VST2 search paths on Windows
        var vst2Roots = new List<string>
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "VstPlugins"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Steinberg", "VstPlugins"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonProgramFiles), "VST2"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "VstPlugins"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steinberg", "VstPlugins")
        };

        var productNames = new List<string> { product, $"{product}_x64", $"{product}_x86" };

        foreach (var root in vst2Roots.Where(Directory.Exists))
        {
            foreach (var pName in productNames)
            {
                var directDll = Path.Combine(root, $"{pName}.dll");
                if (File.Exists(directDll))
                {
                    return (true, directDll, GetFileVersion(directDll));
                }

                if (!string.IsNullOrWhiteSpace(vendor) && !vendor.Equals("Unknown", StringComparison.OrdinalIgnoreCase))
                {
                    var vendorDll = Path.Combine(root, vendor, $"{pName}.dll");
                    if (File.Exists(vendorDll))
                    {
                        return (true, vendorDll, GetFileVersion(vendorDll));
                    }
                }
            }

            try
            {
                foreach (var sub in Directory.EnumerateDirectories(root))
                {
                    foreach (var pName in productNames)
                    {
                        var candidate = Path.Combine(sub, $"{pName}.dll");
                        if (File.Exists(candidate))
                        {
                            return (true, candidate, GetFileVersion(candidate));
                        }
                    }
                }
            }
            catch
            {
                // Best effort
            }
        }

        return (false, null, null);
    }

    private static string? FindBinaryPath(string path)
    {
        if (File.Exists(path)) return path;
        if (Directory.Exists(path))
        {
            var winArch = Path.Combine(path, "Contents", "x86_64-win");
            if (Directory.Exists(winArch))
            {
                var match = Directory.EnumerateFiles(winArch, "*.vst3").FirstOrDefault()
                    ?? Directory.EnumerateFiles(winArch, "*.dll").FirstOrDefault();
                if (match is not null) return match;
            }
            return Directory.EnumerateFiles(path, "*.vst3", SearchOption.AllDirectories).FirstOrDefault()
                ?? Directory.EnumerateFiles(path, "*.dll", SearchOption.AllDirectories).FirstOrDefault();
        }
        return null;
    }

    private static string? GetFileVersion(string binaryPath)
    {
        try
        {
            var info = System.Diagnostics.FileVersionInfo.GetVersionInfo(binaryPath);
            if (!string.IsNullOrWhiteSpace(info.ProductVersion))
            {
                return CleanVersion(info.ProductVersion);
            }
            if (!string.IsNullOrWhiteSpace(info.FileVersion))
            {
                return CleanVersion(info.FileVersion);
            }
        }
        catch
        {
        }
        return null;
    }

    private static string CleanVersion(string v)
    {
        var trimmed = v.Trim();
        var commaIdx = trimmed.IndexOf(',', StringComparison.Ordinal);
        if (commaIdx > 0)
        {
            trimmed = trimmed.Replace(',', '.').Replace(" ", "", StringComparison.Ordinal);
        }
        return trimmed;
    }

    public string? ResolveMacroPath(string rawPath)
    {
        if (string.IsNullOrWhiteSpace(rawPath)) return null;

        var expanded = Environment.ExpandEnvironmentVariables(rawPath);
        if (File.Exists(expanded))
        {
            return expanded;
        }

        var isFactory = expanded.Contains("%FLStudioFactoryData%", StringComparison.OrdinalIgnoreCase);
        var isData = expanded.Contains("%FLStudioData%", StringComparison.OrdinalIgnoreCase);

        if (isFactory || isData)
        {
            var installDirs = GetFlStudioInstallDirectories();
            foreach (var dir in installDirs)
            {
                var candidate = expanded
                    .Replace("%FLStudioFactoryData%", dir, StringComparison.OrdinalIgnoreCase)
                    .Replace("%FLStudioData%", dir, StringComparison.OrdinalIgnoreCase);

                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
        }

        // Also check FL Studio user data folder: Documents/Image-Line
        var docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        if (!string.IsNullOrEmpty(docs))
        {
            var ilDocs = Path.Combine(docs, "Image-Line");
            if (Directory.Exists(ilDocs))
            {
                var candidate = expanded
                    .Replace("%FLStudioData%", ilDocs, StringComparison.OrdinalIgnoreCase)
                    .Replace("%FLStudioFactoryData%", ilDocs, StringComparison.OrdinalIgnoreCase);

                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
        }

        return null;
    }

    private static List<string> GetFlStudioInstallDirectories()
    {
        var dirs = new List<string>();
        var pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var pfx86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);

        var roots = new List<string>();
        if (!string.IsNullOrEmpty(pf)) roots.Add(Path.Combine(pf, "Image-Line"));
        if (!string.IsNullOrEmpty(pfx86)) roots.Add(Path.Combine(pfx86, "Image-Line"));

        var versionSubdirs = new[]
        {
            "FL Studio 2026",
            "FL Studio 2025",
            "FL Studio 2024",
            "FL Studio 21",
            "FL Studio 20",
            "FL Studio"
        };

        foreach (var root in roots)
        {
            if (!Directory.Exists(root)) continue;
            foreach (var sub in versionSubdirs)
            {
                var candidate = Path.Combine(root, sub);
                if (Directory.Exists(candidate) && !dirs.Contains(candidate, StringComparer.OrdinalIgnoreCase))
                {
                    dirs.Add(candidate);
                }
            }
        }

        if (OperatingSystem.IsWindows())
        {
            try
            {
                using var baseKey = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Image-Line\FL Studio\Paths");
                if (baseKey != null)
                {
                    var appPath = baseKey.GetValue("AppPath") as string;
                    if (!string.IsNullOrEmpty(appPath) && Directory.Exists(appPath) && !dirs.Contains(appPath, StringComparer.OrdinalIgnoreCase))
                    {
                        dirs.Add(appPath);
                    }
                }
            }
            catch
            {
                // Best effort
            }
        }

        return dirs;
    }
}
