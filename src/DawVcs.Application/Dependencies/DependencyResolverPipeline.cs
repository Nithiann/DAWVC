using DawVcs.Domain.Dependencies;
using DawVcs.Domain.Hashing;

namespace DawVcs.Application.Dependencies;

/// <summary>
/// Voert deterministische dependencyresolutie uit volgens de 9-staps resolverpipeline (FR-BND-004, TD §29).
/// Verifieert hashes en markeert afwijkingen expliciet als Mismatch (FR-BND-003, FR-BND-005).
/// </summary>
public static class DependencyResolverPipeline
{
    /// <summary>
    /// Probeert een afzonderlijke dependency te resolven tegen de lokale workspace, bestaande bindings en het bestandssysteem.
    /// </summary>
    public static async Task<DependencyBinding> ResolveAsync(
        string workingDirectory,
        Dependency dependency,
        ILocalBindingStore? localBindingStore = null,
        IReadOnlyDictionary<ContentHash, string>? knownHashIndex = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);
        ArgumentNullException.ThrowIfNull(dependency);

        var fullWorkingDir = Path.GetFullPath(workingDirectory);

        // STAP 2 (pre-check): Bestaande lokale binding
        if (localBindingStore is not null)
        {
            var existingBinding = await localBindingStore.GetAsync(dependency.Id, cancellationToken).ConfigureAwait(false);
            if (existingBinding is not null && File.Exists(existingBinding.Locator))
            {
                if (dependency is AssetDependency assetDep && assetDep.Hash.HasValue)
                {
                    var actualHash = await Blake3ContentHasher.HashFileAsync(existingBinding.Locator, cancellationToken).ConfigureAwait(false);
                    if (actualHash == assetDep.Hash.Value)
                    {
                        return existingBinding with { Status = BindingStatus.Verified, VerifiedHash = actualHash };
                    }
                    return existingBinding with { Status = BindingStatus.Mismatch, VerifiedHash = actualHash };
                }

                return existingBinding;
            }
        }

        // STAP 1 & 3: Repository / Relative asset in workspace
        if (dependency is AssetDependency asset)
        {
            if (asset.RelativePath is not null)
            {
                var relPath = asset.RelativePath.Value.Value.Replace('/', Path.DirectorySeparatorChar);
                var fullPath = Path.Combine(fullWorkingDir, relPath);
                if (File.Exists(fullPath))
                {
                    if (asset.Hash.HasValue)
                    {
                        var actualHash = await Blake3ContentHasher.HashFileAsync(fullPath, cancellationToken).ConfigureAwait(false);
                        if (actualHash == asset.Hash.Value)
                        {
                            var method = asset.Portability.Mode == PortabilityMode.Bundle
                                ? BindingMethod.RepositoryAsset
                                : BindingMethod.RelativePath;

                            return new DependencyBinding(
                                asset.Id,
                                fullPath,
                                method,
                                BindingStatus.Verified,
                                actualHash,
                                DateTimeOffset.UtcNow);
                        }

                        // Bestand bestaat op relatief pad maar hash wijkt af (FR-BND-005)
                        return new DependencyBinding(
                            asset.Id,
                            fullPath,
                            BindingMethod.RelativePath,
                            BindingStatus.Mismatch,
                            actualHash,
                            DateTimeOffset.UtcNow,
                            "File exists at relative path but content hash does not match expected snapshot hash.");
                    }
                }
            }

            // STAP 4: Original Path (diagnostische locator, FR-BND-008)
            if (!string.IsNullOrWhiteSpace(asset.OriginalPath) && File.Exists(asset.OriginalPath))
            {
                if (asset.Hash.HasValue)
                {
                    var actualHash = await Blake3ContentHasher.HashFileAsync(asset.OriginalPath, cancellationToken).ConfigureAwait(false);
                    if (actualHash == asset.Hash.Value)
                    {
                        return new DependencyBinding(
                            asset.Id,
                            asset.OriginalPath,
                            BindingMethod.OriginalPath,
                            BindingStatus.Verified,
                            actualHash,
                            DateTimeOffset.UtcNow);
                    }

                    // Bestand bestaat op origineel pad maar hash wijkt af
                    return new DependencyBinding(
                        asset.Id,
                        asset.OriginalPath,
                        BindingMethod.OriginalPath,
                        BindingStatus.Mismatch,
                        actualHash,
                        DateTimeOffset.UtcNow,
                        "File exists at original path but content hash does not match expected snapshot hash.");
                }
            }

            // STAP 7: Content-hash discovery (FR-BND-007)
            if (asset.Hash.HasValue)
            {
                // Kijk in meegeleverde index indien beschikbaar
                if (knownHashIndex is not null && knownHashIndex.TryGetValue(asset.Hash.Value, out var indexedPath) && File.Exists(indexedPath))
                {
                    return new DependencyBinding(
                        asset.Id,
                        indexedPath,
                        BindingMethod.ContentHashDiscovery,
                        BindingStatus.Verified,
                        asset.Hash.Value,
                        DateTimeOffset.UtcNow,
                        "Discovered via content-hash index.");
                }

                // Zoek lokaal in werkdirectory naar een bestand met dezelfde hash
                var discoveredPath = await SearchByHashAsync(fullWorkingDir, asset.Hash.Value, cancellationToken).ConfigureAwait(false);
                if (discoveredPath is not null)
                {
                    return new DependencyBinding(
                        asset.Id,
                        discoveredPath,
                        BindingMethod.ContentHashDiscovery,
                        BindingStatus.Verified,
                        asset.Hash.Value,
                        DateTimeOffset.UtcNow,
                        "Discovered in working directory via matching content hash.");
                }
            }
        }
        else if (dependency is PluginDependency plugin)
        {
            // STAP 5: Library mapping / Plugin detection with Version Compatibility Check
            var (found, pluginPath, detectedVersion) = CheckPluginInstalled(plugin);
            if (found && pluginPath is not null)
            {
                var isCompatible = IsVersionCompatible(plugin.VersionRequirement, detectedVersion, out var explanation);
                var status = isCompatible ? BindingStatus.Verified : BindingStatus.Mismatch;

                return new DependencyBinding(
                    plugin.Id,
                    pluginPath,
                    BindingMethod.LibraryMapping,
                    status,
                    null,
                    DateTimeOffset.UtcNow,
                    explanation);
            }
        }

        // STAP 9: Unresolved
        return new DependencyBinding(
            dependency.Id,
            string.Empty,
            BindingMethod.Unresolved,
            BindingStatus.Unresolved,
            null,
            DateTimeOffset.UtcNow);
    }

    private static async Task<string?> SearchByHashAsync(string rootDirectory, ContentHash targetHash, CancellationToken cancellationToken)
    {
        if (!Directory.Exists(rootDirectory))
        {
            return null;
        }

        var ignoredDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".dawvc", ".git", "bin", "obj" };

        var queue = new Queue<string>();
        queue.Enqueue(rootDirectory);

        while (queue.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var dir = queue.Dequeue();

            try
            {
                foreach (var file in Directory.EnumerateFiles(dir))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    try
                    {
                        var hash = await Blake3ContentHasher.HashFileAsync(file, cancellationToken).ConfigureAwait(false);
                        if (hash == targetHash)
                        {
                            return file;
                        }
                    }
                    catch
                    {
                        // Best effort skipping unreadable files
                    }
                }

                foreach (var subDir in Directory.EnumerateDirectories(dir))
                {
                    var name = Path.GetFileName(subDir);
                    if (!ignoredDirs.Contains(name))
                    {
                        queue.Enqueue(subDir);
                    }
                }
            }
            catch
            {
                // Best effort directory scan
            }
        }

        return null;
    }

    public static (bool Found, string? Path, string? DetectedVersion) CheckPluginInstalled(PluginDependency plugin)
    {
        ArgumentNullException.ThrowIfNull(plugin);

        // Native Image-Line plugins are considered available with the DAW
        if (plugin.Plugin.Format == PluginFormat.Native || plugin.Plugin.Vendor.Equals("Image-Line", StringComparison.OrdinalIgnoreCase))
        {
            return (true, "Native FL Studio Plugin", null);
        }

        // Check common VST3 paths on Windows
        var commonVst3 = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonProgramFiles),
            "VST3",
            $"{plugin.Plugin.Product}.vst3");

        if (Directory.Exists(commonVst3) || File.Exists(commonVst3))
        {
            var binPath = FindBinaryPath(commonVst3);
            var version = binPath is not null ? GetFileVersion(binPath) : null;
            return (true, commonVst3, version);
        }

        var userVst3 = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Programs",
            "Common",
            "VST3",
            $"{plugin.Plugin.Product}.vst3");

        if (Directory.Exists(userVst3) || File.Exists(userVst3))
        {
            var binPath = FindBinaryPath(userVst3);
            var version = binPath is not null ? GetFileVersion(binPath) : null;
            return (true, userVst3, version);
        }

        return (false, null, null);
    }

    public static string? DetectLocalPluginVersion(PluginIdentity plugin)
    {
        ArgumentNullException.ThrowIfNull(plugin);
        var dummy = new PluginDependency(
            DependencyId.ForPlugin(plugin.Vendor, plugin.Product, plugin.Format.ToString()),
            plugin);

        var (found, _, version) = CheckPluginInstalled(dummy);
        return found ? version : null;
    }

    private static string? FindBinaryPath(string path)
    {
        if (File.Exists(path))
        {
            return path;
        }

        if (Directory.Exists(path))
        {
            var winArch = Path.Combine(path, "Contents", "x86_64-win");
            if (Directory.Exists(winArch))
            {
                var match = Directory.EnumerateFiles(winArch, "*.vst3").FirstOrDefault()
                    ?? Directory.EnumerateFiles(winArch, "*.dll").FirstOrDefault();
                if (match is not null)
                {
                    return match;
                }
            }

            return Directory.EnumerateFiles(path, "*.vst3", SearchOption.AllDirectories).FirstOrDefault()
                ?? Directory.EnumerateFiles(path, "*.dll", SearchOption.AllDirectories).FirstOrDefault();
        }

        return null;
    }

    public static string? GetFileVersion(string binaryPath)
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
            // Best effort
        }

        return null;
    }

    private static string CleanVersion(string v)
    {
        var trimmed = v.Trim();
        if (trimmed.Contains(',', StringComparison.Ordinal))
        {
            trimmed = trimmed.Replace(',', '.').Replace(" ", "", StringComparison.Ordinal);
        }
        return trimmed;
    }

    public static bool IsVersionCompatible(string? required, string? installed, out string explanation)
    {
        if (string.IsNullOrWhiteSpace(required))
        {
            explanation = installed is not null
                ? $"Installed version {installed}."
                : "Installed (unversioned).";
            return true;
        }

        if (string.IsNullOrWhiteSpace(installed))
        {
            explanation = $"Required version is {required}, but installed version could not be determined.";
            return true;
        }

        if (TryParseVersion(required, out var reqVer) && TryParseVersion(installed, out var instVer))
        {
            if (instVer >= reqVer)
            {
                explanation = $"Installed version {installed} satisfies requirement >={required}.";
                return true;
            }
            else
            {
                explanation = $"Installed version {installed} is older than project version {required}. Plugins must be the same or higher version.";
                return false;
            }
        }

        if (string.Equals(required, installed, StringComparison.OrdinalIgnoreCase))
        {
            explanation = $"Installed version matches required version {required}.";
            return true;
        }

        explanation = $"Installed version {installed}, required version {required}.";
        return true;
    }

    public static bool TryParseVersion(string v, out Version version)
    {
        var clean = v.TrimStart('v', 'V', '>', '=', ' ');
        var parts = clean.Split(['.', '-', '+', ' '], StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
        {
            version = new Version(0, 0);
            return false;
        }

        var numbers = new List<int>();
        foreach (var part in parts)
        {
            if (int.TryParse(part, out var num))
            {
                numbers.Add(num);
            }
            else
            {
                break;
            }
        }

        if (numbers.Count == 0)
        {
            version = new Version(0, 0);
            return false;
        }

        if (numbers.Count == 1)
        {
            numbers.Add(0);
        }

        version = numbers.Count switch
        {
            2 => new Version(numbers[0], numbers[1]),
            3 => new Version(numbers[0], numbers[1], numbers[2]),
            _ => new Version(numbers[0], numbers[1], numbers[2], numbers[3])
        };
        return true;
    }
}
