using DawVcs.Adapters.Abstractions;
using DawVcs.Domain.Artifacts;
using DawVcs.Domain.Dependencies;
using DawVcs.Domain.Hashing;

namespace DawVcs.Application.Dependencies;

/// <summary>
/// Vertaalt adapterdetectieresultaten en werkdirectorybestanden naar een formele DependencyGraph (IMP-0604, IMP-0605).
/// </summary>
public static class DependencyDiscoveryService
{
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

    public static async Task<DependencyGraph> DiscoverAsync(
        string workingDirectory,
        ProjectDetectionResult detectionResult,
        IDawAdapter? adapter = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);
        ArgumentNullException.ThrowIfNull(detectionResult);

        var dependencies = new List<Dependency>();
        var fullWorkingDir = Path.GetFullPath(workingDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        // 1. Vertaal sample paths naar AssetDependencies (IMP-0604)
        if (detectionResult.Metadata.TryGetValue("SamplePaths", out var samplesStr) && !string.IsNullOrWhiteSpace(samplesStr))
        {
            var rawSamples = samplesStr.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            foreach (var rawSample in rawSamples)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var (resolvedFullPath, relativePath) = ResolveAssetPath(fullWorkingDir, rawSample, adapter);
                var policy = PortabilityPolicyEngine.DetermineAssetPolicy(resolvedFullPath ?? rawSample, fullWorkingDir);

                if (resolvedFullPath != null && File.Exists(resolvedFullPath))
                {
                    var fileInfo = new FileInfo(resolvedFullPath);
                    ContentHash hash;
                    await using (var fs = new FileStream(resolvedFullPath, FileMode.Open, FileAccess.Read, FileShare.Read))
                    {
                        hash = await Blake3ContentHasher.HashAsync(fs, cancellationToken).ConfigureAwait(false);
                    }

                    var assetDep = new AssetDependency(
                        DependencyId.ForAsset(hash),
                        Path.GetFileName(rawSample),
                        DependencyRequirement.Required,
                        DependencySource.NativeProjectParser,
                        policy,
                        relativePath != null ? new ArtifactPath(relativePath) : null,
                        originalPath: resolvedFullPath ?? rawSample,
                        hash: hash,
                        fileSize: fileInfo.Length,
                        isMissing: false);

                    dependencies.Add(assetDep);
                }
                else
                {
                    // Ontbrekend bestand op schijf
                    var assetDep = new AssetDependency(
                        DependencyId.ForUnresolvedAsset(rawSample),
                        Path.GetFileName(rawSample),
                        DependencyRequirement.Required,
                        DependencySource.NativeProjectParser,
                        policy,
                        relativePath: null,
                        originalPath: rawSample,
                        hash: null,
                        fileSize: null,
                        isMissing: true);

                    dependencies.Add(assetDep);
                }
            }
        }

        // 2. Vertaal plugin occurrences naar PluginDependencies (IMP-0605)
        bool handledOccurrences = false;
        if (detectionResult.Metadata.TryGetValue("PluginOccurrences", out var occurrencesJson) && !string.IsNullOrWhiteSpace(occurrencesJson))
        {
            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(occurrencesJson);
                int index = 0;
                foreach (var element in doc.RootElement.EnumerateArray())
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    index++;
                    var rawName = element.GetProperty("Name").GetString() ?? "Unknown";
                    string? vendor = element.TryGetProperty("Vendor", out var v) && v.ValueKind == System.Text.Json.JsonValueKind.String ? v.GetString() : null;
                    string? filePath = element.TryGetProperty("FilePath", out var fp) && fp.ValueKind == System.Text.Json.JsonValueKind.String ? fp.GetString() : null;
                    string location = element.TryGetProperty("Location", out var loc) && loc.ValueKind == System.Text.Json.JsonValueKind.String ? (loc.GetString() ?? "General") : "General";

                    PluginRole role = PluginRole.Unknown;
                    if (element.TryGetProperty("Role", out var rVal))
                    {
                        if (Enum.TryParse<PluginRole>(rVal.ToString(), true, out var parsedRole))
                        {
                            role = parsedRole;
                        }
                    }

                    PluginFormat format = PluginFormat.Unknown;
                    if (element.TryGetProperty("Format", out var fVal))
                    {
                        if (Enum.TryParse<PluginFormat>(fVal.ToString(), true, out var parsedFormat))
                        {
                            format = parsedFormat;
                        }
                    }

                    var identity = adapter != null ? adapter.NormalizePlugin(rawName) : NormalizePluginIdentity(rawName);
                    if (!string.IsNullOrWhiteSpace(vendor) && (identity.Vendor == "Unknown" || string.IsNullOrWhiteSpace(identity.Vendor)))
                    {
                        identity = identity with { Vendor = vendor };
                    }
                    if (format != PluginFormat.Unknown && identity.Format == PluginFormat.Unknown)
                    {
                        identity = identity with { Format = format };
                    }

                    if (role == PluginRole.Unknown)
                    {
                        role = adapter != null ? adapter.DeterminePluginRole(rawName) : DeterminePluginRole(rawName);
                    }

                    var policy = PortabilityPolicyEngine.DeterminePluginPolicy(identity);
                    var localVersion = DependencyResolverPipeline.DetectLocalPluginVersion(identity);

                    var depId = DependencyId.ForPlugin(identity.Vendor, identity.Product, identity.Format.ToString(), $"{location}#{index}");

                    var pluginDep = new PluginDependency(
                        depId,
                        identity,
                        role,
                        versionRequirement: localVersion,
                        requirement: DependencyRequirement.Required,
                        source: DependencySource.NativeProjectParser,
                        portability: policy,
                        location: location,
                        expectedFilePath: filePath);

                    dependencies.Add(pluginDep);
                    handledOccurrences = true;
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                // Fallback to legacy PluginNames if parsing fails
            }
        }

        if (!handledOccurrences && detectionResult.Metadata.TryGetValue("PluginNames", out var pluginsStr) && !string.IsNullOrWhiteSpace(pluginsStr))
        {
            var rawPlugins = pluginsStr.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            foreach (var rawPlugin in rawPlugins)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var identity = adapter != null ? adapter.NormalizePlugin(rawPlugin) : NormalizePluginIdentity(rawPlugin);
                var role = adapter != null ? adapter.DeterminePluginRole(rawPlugin) : DeterminePluginRole(rawPlugin);
                var policy = PortabilityPolicyEngine.DeterminePluginPolicy(identity);
                var localVersion = DependencyResolverPipeline.DetectLocalPluginVersion(identity);

                var pluginDep = new PluginDependency(
                    DependencyId.ForPlugin(identity.Vendor, identity.Product, identity.Format.ToString()),
                    identity,
                    role,
                    versionRequirement: localVersion,
                    requirement: DependencyRequirement.Required,
                    source: DependencySource.NativeProjectParser,
                    portability: policy);

                dependencies.Add(pluginDep);
            }
        }

        return new DependencyGraph(dependencies);
    }

    private static (string? FullPath, string? RelativePath) ResolveAssetPath(string workingDirectory, string rawSample, IDawAdapter? adapter = null)
    {
        // 1. Direct bestaand pad (absoluut of direct relatief)
        if (Path.IsPathRooted(rawSample))
        {
            if (File.Exists(rawSample))
            {
                if (rawSample.StartsWith(workingDirectory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                {
                    var rel = Path.GetRelativePath(workingDirectory, rawSample);
                    return (rawSample, rel);
                }
                return (rawSample, null);
            }
        }
        else
        {
            var direct = Path.Combine(workingDirectory, rawSample);
            if (File.Exists(direct))
            {
                var rel = Path.GetRelativePath(workingDirectory, direct);
                return (direct, rel);
            }

            // Zoek in gangbare subfolders
            var audioPath = Path.Combine(workingDirectory, "Audio", rawSample);
            if (File.Exists(audioPath))
            {
                return (audioPath, Path.GetRelativePath(workingDirectory, audioPath));
            }

            var samplesPath = Path.Combine(workingDirectory, "Samples", rawSample);
            if (File.Exists(samplesPath))
            {
                return (samplesPath, Path.GetRelativePath(workingDirectory, samplesPath));
            }
        }

        // 2. Probeer DAW macro's / environment variabelen op te lossen (bijv. %FLStudioFactoryData%)
        if (adapter != null)
        {
            var resolvedMacro = adapter.ResolveMacroPath(rawSample);
            if (resolvedMacro != null && File.Exists(resolvedMacro))
            {
                if (resolvedMacro.StartsWith(workingDirectory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                {
                    var rel = Path.GetRelativePath(workingDirectory, resolvedMacro);
                    return (resolvedMacro, rel);
                }
                return (resolvedMacro, null);
            }
        }
        else
        {
            var expanded = Environment.ExpandEnvironmentVariables(rawSample);
            if (File.Exists(expanded))
            {
                if (expanded.StartsWith(workingDirectory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                {
                    var rel = Path.GetRelativePath(workingDirectory, expanded);
                    return (expanded, rel);
                }
                return (expanded, null);
            }
        }

        // 3. Probeer DAW sample zoekpaden (bijv. FL Studio Browser Extra Search Folders)
        if (adapter != null)
        {
            var resolvedSample = adapter.ResolveSamplePath(rawSample);
            if (resolvedSample != null && File.Exists(resolvedSample))
            {
                if (resolvedSample.StartsWith(workingDirectory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                {
                    var rel = Path.GetRelativePath(workingDirectory, resolvedSample);
                    return (resolvedSample, rel);
                }
                return (resolvedSample, null);
            }
        }

        return (null, null);
    }

    private static PluginIdentity NormalizePluginIdentity(string rawName)
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

        // Onbekende plugin
        var vendor = "Unknown";
        return new PluginIdentity(vendor, normalizedProduct, format);
    }

    private static PluginRole DeterminePluginRole(string rawName)
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
}
