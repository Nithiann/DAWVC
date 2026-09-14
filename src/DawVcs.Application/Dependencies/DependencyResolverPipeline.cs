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
            // STAP 5: Library mapping / Plugin detection
            var (found, pluginPath) = CheckPluginInstalled(plugin);
            if (found && pluginPath is not null)
            {
                return new DependencyBinding(
                    plugin.Id,
                    pluginPath,
                    BindingMethod.LibraryMapping,
                    BindingStatus.Verified,
                    null,
                    DateTimeOffset.UtcNow);
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

    private static (bool Found, string? Path) CheckPluginInstalled(PluginDependency plugin)
    {
        // Native Image-Line plugins are considered available with the DAW
        if (plugin.Plugin.Format == PluginFormat.Native || plugin.Plugin.Vendor.Equals("Image-Line", StringComparison.OrdinalIgnoreCase))
        {
            return (true, "Native FL Studio Plugin");
        }

        // Check common VST3 paths on Windows
        var commonVst3 = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonProgramFiles),
            "VST3",
            $"{plugin.Plugin.Product}.vst3");

        if (Directory.Exists(commonVst3) || File.Exists(commonVst3))
        {
            return (true, commonVst3);
        }

        var userVst3 = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Programs",
            "Common",
            "VST3",
            $"{plugin.Plugin.Product}.vst3");

        if (Directory.Exists(userVst3) || File.Exists(userVst3))
        {
            return (true, userVst3);
        }

        return (false, null);
    }
}
