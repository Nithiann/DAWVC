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
        ["ValhallaVintageVerb"] = ("Valhalla DSP", PluginRole.Effect, "ValhallaVintageVerb")
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

                var (resolvedFullPath, relativePath) = ResolveAssetPath(fullWorkingDir, rawSample);
                var policy = PortabilityPolicyEngine.DetermineAssetPolicy(rawSample, fullWorkingDir);

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
                        originalPath: rawSample,
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

        // 2. Vertaal plugin names naar PluginDependencies (IMP-0605)
        if (detectionResult.Metadata.TryGetValue("PluginNames", out var pluginsStr) && !string.IsNullOrWhiteSpace(pluginsStr))
        {
            var rawPlugins = pluginsStr.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            foreach (var rawPlugin in rawPlugins)
            {
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

    private static (string? FullPath, string? RelativePath) ResolveAssetPath(string workingDirectory, string rawSample)
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
