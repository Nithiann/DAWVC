using System.Text;

using DawVcs.Domain.Artifacts;
using DawVcs.Domain.Common;
using DawVcs.Domain.Configuration;
using DawVcs.Domain.Dependencies;
using DawVcs.Domain.Entities;
using DawVcs.Domain.Hashing;
using DawVcs.Domain.Metadata;
using DawVcs.Domain.Repositories;
using DawVcs.Domain.Serialization;
using DawVcs.Domain.Storage;
using DawVcs.Infrastructure.Configuration;
using DawVcs.Infrastructure.Dependencies;
using DawVcs.Infrastructure.FileSystem;
using DawVcs.Infrastructure.Storage;

namespace DawVcs.Infrastructure.Repositories;

/// <summary>
/// Filesystem-based repository context providing ref management, configuration, and object store access.
/// </summary>
public sealed class FileSystemRepositoryContext : IRepositoryContext
{
    private readonly string _rootPath;
    private readonly string _dotDawvcPath;
    private readonly string _refsHeadsPath;
    private readonly string _headFilePath;
    private readonly string _objectsPath;
    private readonly LooseObjectStore _objectStore;
    private readonly SqliteStagingIndex _stagingIndex;
    private readonly JsonLocalBindingStore _localBindings;

    public string RootPath => _rootPath;
    public IObjectStore ObjectStore => _objectStore;
    public IStagingIndex StagingIndex => _stagingIndex;
    public ILocalBindingStore LocalBindings => _localBindings;

    public FileSystemRepositoryContext(string rootPath, Action<string>? onBeforeAtomicPublish = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);

        _rootPath = Path.GetFullPath(rootPath);
        _dotDawvcPath = Path.Combine(_rootPath, ".dawvc");
        _refsHeadsPath = Path.Combine(_dotDawvcPath, "refs", "heads");
        _headFilePath = Path.Combine(_dotDawvcPath, "HEAD");
        _objectsPath = Path.Combine(_dotDawvcPath, "objects");

        _objectStore = new LooseObjectStore(_rootPath, onBeforeAtomicPublish);
        _stagingIndex = new SqliteStagingIndex(_rootPath);
        _localBindings = new JsonLocalBindingStore(_dotDawvcPath);
    }

    /// <summary>
    /// Finds the nearest repository root by searching upwards from the starting directory.
    /// </summary>
    public static string? FindRoot(string startingDirectory)
    {
        var current = new DirectoryInfo(Path.GetFullPath(startingDirectory));
        while (current != null)
        {
            if (Directory.Exists(Path.Combine(current.FullName, ".dawvc")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        return null;
    }

    public RepositoryConfig LoadConfig()
    {
        return YamlRepositoryConfigManager.ReadFromDirectory(_rootPath);
    }

    public void SaveConfig(RepositoryConfig config)
    {
        YamlRepositoryConfigManager.WriteToDirectory(_rootPath, config);
    }

    public BranchName GetCurrentBranch()
    {
        if (!File.Exists(_headFilePath))
        {
            throw new InvalidOperationException($"Corrupt repository: HEAD file is missing at '{_headFilePath}'.");
        }

        var headContent = File.ReadAllText(_headFilePath).Trim();
        const string prefix = "ref: refs/heads/";
        if (headContent.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            var branch = headContent[prefix.Length..].Trim();
            if (BranchName.TryCreate(branch, out var branchName, out _))
            {
                return branchName;
            }
        }

        throw new InvalidOperationException($"Corrupt repository: Invalid HEAD reference '{headContent}' in '{_headFilePath}'.");
    }

    public void SetCurrentBranch(BranchName branch)
    {
        Directory.CreateDirectory(_dotDawvcPath);
        AtomicFileWriter.WriteAtomic(_headFilePath, $"ref: refs/heads/{branch.Value}\n");
    }

    public IReadOnlyList<BranchInfo> GetBranches()
    {
        var currentBranch = GetCurrentBranch();
        var result = new List<BranchInfo>();

        if (!Directory.Exists(_refsHeadsPath))
        {
            return [new BranchInfo(currentBranch, GetBranchCommit(currentBranch), true)];
        }

        foreach (var file in Directory.EnumerateFiles(_refsHeadsPath, "*", SearchOption.AllDirectories))
        {
            var relPath = Path.GetRelativePath(_refsHeadsPath, file).Replace('\\', '/');
            if (BranchName.TryCreate(relPath, out var branchName, out _))
            {
                var commitId = GetBranchCommit(branchName);
                var isCurrent = branchName == currentBranch;
                result.Add(new BranchInfo(branchName, commitId, isCurrent));
            }
        }

        if (result.Count == 0)
        {
            result.Add(new BranchInfo(currentBranch, GetBranchCommit(currentBranch), true));
        }

        return result.OrderBy(b => b.Name.Value, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public void CreateBranch(BranchName branch, CommitId commitId)
    {
        var branchRefFile = GetBranchRefFile(branch);
        var dir = Path.GetDirectoryName(branchRefFile);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        if (File.Exists(branchRefFile))
        {
            throw new InvalidOperationException($"Branch '{branch.Value}' already exists.");
        }

        AtomicFileWriter.WriteAtomic(branchRefFile, commitId.ToString() + "\n");
    }

    public bool DeleteBranch(BranchName branch)
    {
        var currentBranch = GetCurrentBranch();
        if (branch == currentBranch)
        {
            throw new InvalidOperationException($"Cannot delete the currently active branch '{branch.Value}'.");
        }

        var branchRefFile = GetBranchRefFile(branch);
        if (File.Exists(branchRefFile))
        {
            File.Delete(branchRefFile);

            var parent = Path.GetDirectoryName(branchRefFile);
            while (!string.IsNullOrEmpty(parent) && !string.Equals(Path.GetFullPath(parent), Path.GetFullPath(_refsHeadsPath), StringComparison.OrdinalIgnoreCase))
            {
                if (Directory.Exists(parent) && !Directory.EnumerateFileSystemEntries(parent).Any())
                {
                    Directory.Delete(parent);
                    parent = Path.GetDirectoryName(parent);
                }
                else
                {
                    break;
                }
            }

            return true;
        }

        return false;
    }

    public CommitId? GetBranchCommit(BranchName branch)
    {
        var branchRefFile = GetBranchRefFile(branch);
        if (!File.Exists(branchRefFile))
        {
            return null;
        }

        var hashHex = File.ReadAllText(branchRefFile).Trim();
        return CommitId.TryParse(hashHex, out var commitId) ? commitId : null;
    }

    public async Task UpdateBranchCommitAsync(BranchName branch, CommitId newCommit, CancellationToken cancellationToken = default)
    {
        var branchRefFile = GetBranchRefFile(branch);
        var dir = Path.GetDirectoryName(branchRefFile);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        var bytes = Encoding.UTF8.GetBytes(newCommit.ToString() + "\n");

        await AtomicFileWriter.WriteAtomicAsync(
            branchRefFile,
            async stream =>
            {
                await stream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
            },
            cancellationToken).ConfigureAwait(false);
    }

    private string GetBranchRefFile(BranchName branch)
    {
        var relative = branch.Value.Replace('/', Path.DirectorySeparatorChar);
        return Path.Combine(_refsHeadsPath, relative);
    }

    public CommitId? ResolveReference(string reference)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reference);

        // 1. Try resolve as branch
        if (BranchName.TryCreate(reference, out var branchName, out _))
        {
            var branchCommit = GetBranchCommit(branchName);
            if (branchCommit.HasValue)
            {
                return branchCommit.Value;
            }
        }

        // 2. Try resolve as full 64-character hex CommitId
        if (CommitId.TryParse(reference, out var commitId))
        {
            if (_objectStore.Exists(commitId.Value))
            {
                return commitId;
            }
        }

        // 3. Try prefix match in object store (minimum 4 hex chars)
        if (reference.Length >= 4 && reference.All(Uri.IsHexDigit))
        {
            var prefixDirName = reference[..2].ToLowerInvariant();
            var dir = Path.Combine(_objectsPath, prefixDirName);
            if (Directory.Exists(dir))
            {
                var remainingPrefix = reference[2..].ToLowerInvariant();
                var matchingFiles = Directory.GetFiles(dir)
                    .Where(f => !Path.GetFileName(f).StartsWith('.'))
                    .Where(f => Path.GetFileName(f).StartsWith(remainingPrefix, StringComparison.OrdinalIgnoreCase))
                    .ToList();

                if (matchingFiles.Count == 1)
                {
                    var fullHash = prefixDirName + Path.GetFileName(matchingFiles[0]);
                    if (CommitId.TryParse(fullHash, out var matchedCommitId))
                    {
                        return matchedCommitId;
                    }
                }
                else if (matchingFiles.Count > 1)
                {
                    throw new InvalidOperationException($"Ambiguous reference '{reference}'. Multiple objects match this prefix.");
                }
            }
        }

        return null;
    }

    public async Task<Commit?> LoadCommitAsync(CommitId id, CancellationToken cancellationToken = default)
    {
        if (!_objectStore.Exists(id.Value))
        {
            return null;
        }

        var payload = await _objectStore.ReadObjectPayloadAsync(id.Value, cancellationToken).ConfigureAwait(false);
        var dto = CanonicalJsonSerializer.Deserialize<RawCommitDto>(payload);
        if (dto is null) return null;

        var parents = (dto.Parents ?? Array.Empty<string>())
            .Select(p => CommitId.Parse(p))
            .ToList();

        var snapshotId = SnapshotId.Parse(dto.SnapshotId);
        var timestamp = DateTimeOffset.FromUnixTimeMilliseconds(dto.Timestamp);

        return new Commit(parents, snapshotId, dto.Author, timestamp, dto.Message, id);
    }

    public async Task<ProjectSnapshot?> LoadSnapshotAsync(SnapshotId id, CancellationToken cancellationToken = default)
    {
        if (!_objectStore.Exists(id.Value))
        {
            return null;
        }

        var payload = await _objectStore.ReadObjectPayloadAsync(id.Value, cancellationToken).ConfigureAwait(false);
        var dto = CanonicalJsonSerializer.Deserialize<RawSnapshotDto>(payload);
        if (dto is null) return null;

        var entries = (dto.Entries ?? Array.Empty<RawEntryDto>()).Select(e => new ArtifactEntry(
            new ArtifactPath(e.Path),
            BlobId.Parse(e.BlobId),
            ContentHash.Parse(e.Hash),
            e.Size,
            (ArtifactRole)e.Role
        )).ToList();

        ArtifactRoot root = (ArtifactKind)dto.ContainerKind switch
        {
            ArtifactKind.SingleFile => new SingleFileArtifact(entries.First()),
            ArtifactKind.Directory => new DirectoryArtifact(entries),
            ArtifactKind.Package => new PackageArtifact(entries),
            _ => new SingleFileArtifact(entries.First())
        };

        var project = new ProjectArtifact(dto.DawName, root);
        var createdAt = DateTimeOffset.FromUnixTimeMilliseconds(dto.CreatedAt);

        var depList = new List<Dependency>();
        if (dto.Dependencies != null)
        {
            foreach (var d in dto.Dependencies)
            {
                var portMode = (PortabilityMode)d.Portability;
                var policy = new PortabilityPolicy(portMode);
                var source = Enum.IsDefined(typeof(DependencySource), d.Source) ? (DependencySource)d.Source : DependencySource.NativeProjectParser;
                var requirement = Enum.IsDefined(typeof(DependencyRequirement), d.Requirement) ? (DependencyRequirement)d.Requirement : DependencyRequirement.Required;
                var provenance = !string.IsNullOrWhiteSpace(d.Provenance) ? new MetadataObservation<string>(d.Provenance, "Snapshot") : null;

                if ((DependencyKind)d.Kind == DependencyKind.Plugin)
                {
                    var vendor = !string.IsNullOrWhiteSpace(d.Vendor) ? d.Vendor : "Unknown";
                    var product = !string.IsNullOrWhiteSpace(d.Product) ? d.Product : (!string.IsNullOrWhiteSpace(d.Name) ? d.Name : "Unknown Plugin");
                    var format = d.Format.HasValue && Enum.IsDefined(typeof(PluginFormat), d.Format.Value)
                        ? (PluginFormat)d.Format.Value
                        : PluginFormat.Unknown;
                    var role = d.Role.HasValue && Enum.IsDefined(typeof(PluginRole), d.Role.Value)
                        ? (PluginRole)d.Role.Value
                        : PluginRole.Unknown;

                    depList.Add(new PluginDependency(
                        new DependencyId(d.Id),
                        new PluginIdentity(vendor, product, format),
                        role: role,
                        versionRequirement: d.VersionRequirement,
                        requirement: requirement,
                        source: source,
                        portability: policy,
                        provenance: provenance));
                }
                else if ((DependencyKind)d.Kind == DependencyKind.Environment)
                {
                    depList.Add(new EnvironmentDependency(
                        new DependencyId(d.Id),
                        d.EnvKey ?? "Unknown",
                        d.EnvExpectedValue ?? string.Empty,
                        requirement: requirement,
                        source: source,
                        provenance: provenance));
                }
                else
                {
                    ContentHash? hash = !string.IsNullOrWhiteSpace(d.Hash) && ContentHash.TryParse(d.Hash, out var parsedHash) ? parsedHash : null;
                    ArtifactPath? relPath = !string.IsNullOrWhiteSpace(d.RelativePath) ? new ArtifactPath(d.RelativePath) : null;

                    depList.Add(new AssetDependency(
                        new DependencyId(d.Id),
                        d.Name,
                        requirement,
                        source,
                        policy,
                        relativePath: relPath,
                        originalPath: d.OriginalPath,
                        hash: hash,
                        fileSize: d.FileSize,
                        isMissing: d.IsMissing,
                        provenance: provenance));
                }
            }
        }
        var depGraph = new DependencyGraph(depList);

        return new ProjectSnapshot(
            project,
            createdAt,
            dto.Metadata,
            id,
            isComplete: dto.IsComplete,
            incompleteReason: dto.IncompleteReason,
            dependencies: depGraph);
    }

    private sealed class RawCommitDto
    {
        public int SchemaVersion { get; set; }
        public string[]? Parents { get; set; }
        public string SnapshotId { get; set; } = string.Empty;
        public string Author { get; set; } = string.Empty;
        public long Timestamp { get; set; }
        public string Message { get; set; } = string.Empty;
    }

    private sealed class RawSnapshotDto
    {
        public int SchemaVersion { get; set; }
        public string DawName { get; set; } = string.Empty;
        public int ContainerKind { get; set; }
        public string AggregateHash { get; set; } = string.Empty;
        public long CreatedAt { get; set; }
        public bool IsComplete { get; set; } = true;
        public string? IncompleteReason { get; set; }
        public RawEntryDto[]? Entries { get; set; }
        public RawDependencyDto[]? Dependencies { get; set; }
        public Dictionary<string, string>? Metadata { get; set; }
    }

    private sealed class RawDependencyDto
    {
        public string Id { get; set; } = string.Empty;
        public int Kind { get; set; }
        public string Name { get; set; } = string.Empty;
        public int Requirement { get; set; }
        public int Source { get; set; }
        public int Portability { get; set; }
        public string? Provenance { get; set; }

        // Asset properties
        public string? Hash { get; set; }
        public long? FileSize { get; set; }
        public string? OriginalPath { get; set; }
        public string? RelativePath { get; set; }
        public bool IsMissing { get; set; }

        // Plugin properties
        public string? Vendor { get; set; }
        public string? Product { get; set; }
        public int? Format { get; set; }
        public int? Role { get; set; }
        public string? VersionRequirement { get; set; }

        // Environment properties
        public string? EnvKey { get; set; }
        public string? EnvExpectedValue { get; set; }
    }

    private sealed class RawEntryDto
    {
        public string Path { get; set; } = string.Empty;
        public string BlobId { get; set; } = string.Empty;
        public string Hash { get; set; } = string.Empty;
        public long Size { get; set; }
        public int Role { get; set; }
    }

    public void Dispose()
    {
        _stagingIndex.Dispose();
        _localBindings.Dispose();
    }
}
