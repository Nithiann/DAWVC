using System.Text;

using DawVcs.Domain.Artifacts;
using DawVcs.Domain.Common;
using DawVcs.Domain.Configuration;
using DawVcs.Domain.Dependencies;
using DawVcs.Domain.Entities;
using DawVcs.Domain.Hashing;
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

    public FileSystemRepositoryContext(string rootPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);

        _rootPath = Path.GetFullPath(rootPath);
        _dotDawvcPath = Path.Combine(_rootPath, ".dawvc");
        _refsHeadsPath = Path.Combine(_dotDawvcPath, "refs", "heads");
        _headFilePath = Path.Combine(_dotDawvcPath, "HEAD");
        _objectsPath = Path.Combine(_dotDawvcPath, "objects");

        _objectStore = new LooseObjectStore(_rootPath);
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
            return BranchName.Main;
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

        return BranchName.Main;
    }

    public void SetCurrentBranch(BranchName branch)
    {
        Directory.CreateDirectory(_dotDawvcPath);
        File.WriteAllText(_headFilePath, $"ref: refs/heads/{branch.Value}\n");
    }

    public IReadOnlyList<BranchInfo> GetBranches()
    {
        var currentBranch = GetCurrentBranch();
        var result = new List<BranchInfo>();

        if (!Directory.Exists(_refsHeadsPath))
        {
            return [new BranchInfo(currentBranch, GetBranchCommit(currentBranch), true)];
        }

        foreach (var file in Directory.EnumerateFiles(_refsHeadsPath))
        {
            var fileName = Path.GetFileName(file);
            if (BranchName.TryCreate(fileName, out var branchName, out _))
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
        Directory.CreateDirectory(_refsHeadsPath);
        var branchRefFile = Path.Combine(_refsHeadsPath, branch.Value);
        if (File.Exists(branchRefFile))
        {
            throw new InvalidOperationException($"Branch '{branch.Value}' already exists.");
        }

        File.WriteAllText(branchRefFile, commitId.ToString() + "\n");
    }

    public bool DeleteBranch(BranchName branch)
    {
        var currentBranch = GetCurrentBranch();
        if (branch == currentBranch)
        {
            throw new InvalidOperationException($"Cannot delete the currently active branch '{branch.Value}'.");
        }

        var branchRefFile = Path.Combine(_refsHeadsPath, branch.Value);
        if (File.Exists(branchRefFile))
        {
            File.Delete(branchRefFile);
            return true;
        }

        return false;
    }

    public CommitId? GetBranchCommit(BranchName branch)
    {
        var branchRefFile = Path.Combine(_refsHeadsPath, branch.Value);
        if (!File.Exists(branchRefFile))
        {
            return null;
        }

        var hashHex = File.ReadAllText(branchRefFile).Trim();
        return CommitId.TryParse(hashHex, out var commitId) ? commitId : null;
    }

    public async Task UpdateBranchCommitAsync(BranchName branch, CommitId newCommit, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(_refsHeadsPath);
        var branchRefFile = Path.Combine(_refsHeadsPath, branch.Value);
        var bytes = Encoding.UTF8.GetBytes(newCommit.ToString() + "\n");

        await AtomicFileWriter.WriteAtomicAsync(
            branchRefFile,
            async stream =>
            {
                await stream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
            },
            cancellationToken).ConfigureAwait(false);
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
                if ((DependencyKind)d.Kind == DependencyKind.Plugin)
                {
                    depList.Add(new PluginDependency(
                        new DependencyId(d.Id),
                        new PluginIdentity("Unknown", d.Name, PluginFormat.Unknown),
                        portability: policy));
                }
                else
                {
                    depList.Add(new AssetDependency(
                        new DependencyId(d.Id),
                        d.Name,
                        (DependencyRequirement)d.Requirement,
                        DependencySource.NativeProjectParser,
                        policy));
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
        public int Portability { get; set; }
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
