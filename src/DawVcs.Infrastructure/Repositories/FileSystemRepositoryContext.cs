using System.Text;

using DawVcs.Domain.Artifacts;
using DawVcs.Domain.Common;
using DawVcs.Domain.Configuration;
using DawVcs.Domain.Dependencies;
using DawVcs.Domain.Entities;
using DawVcs.Domain.Hashing;
using DawVcs.Domain.Metadata;
using DawVcs.Domain.Remotes;
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
    private readonly string _refsRemotesPath;
    private readonly string _headFilePath;
    private readonly string _remotesFilePath;
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
        _refsRemotesPath = Path.Combine(_dotDawvcPath, "refs", "remotes");
        _headFilePath = Path.Combine(_dotDawvcPath, "HEAD");
        _remotesFilePath = Path.Combine(_dotDawvcPath, "remotes.json");
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

        // 1b. Try resolve as remote tracking branch (e.g. 'origin/main')
        var slashIdx = reference.IndexOf('/');
        if (slashIdx > 0 && slashIdx < reference.Length - 1)
        {
            var rem = reference[..slashIdx];
            var br = reference[(slashIdx + 1)..];
            if (BranchName.TryCreate(br, out var remoteBranchName, out _))
            {
                var remoteCommit = GetRemoteTrackingCommit(rem, remoteBranchName);
                if (remoteCommit.HasValue)
                {
                    return remoteCommit.Value;
                }
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
        return ObjectPayloadSerializer.DeserializeCommit(payload, id);
    }

    public async Task<ProjectSnapshot?> LoadSnapshotAsync(SnapshotId id, CancellationToken cancellationToken = default)
    {
        if (!_objectStore.Exists(id.Value))
        {
            return null;
        }

        var payload = await _objectStore.ReadObjectPayloadAsync(id.Value, cancellationToken).ConfigureAwait(false);
        return ObjectPayloadSerializer.DeserializeSnapshot(payload, id);
    }



    public IReadOnlyList<RemoteConfig> GetRemotes()
    {
        var dto = LoadRemotesConfigDto();
        return dto.Remotes
            .Select(r => new RemoteConfig(new RemoteName(r.Name), r.Url, r.FetchSpecs))
            .ToList();
    }

    public RemoteConfig? GetRemote(string remoteName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(remoteName);
        return GetRemotes().FirstOrDefault(r => r.Name.Value.Equals(remoteName, StringComparison.OrdinalIgnoreCase));
    }

    public void SaveRemote(RemoteConfig remote)
    {
        ArgumentNullException.ThrowIfNull(remote);
        var dto = LoadRemotesConfigDto();
        var existingIdx = dto.Remotes.FindIndex(r => r.Name.Equals(remote.Name.Value, StringComparison.OrdinalIgnoreCase));
        var entry = new RemoteEntryDto
        {
            Name = remote.Name.Value,
            Url = remote.Url,
            FetchSpecs = remote.FetchSpecs != null ? new List<string>(remote.FetchSpecs) : null
        };

        if (existingIdx >= 0)
        {
            dto.Remotes[existingIdx] = entry;
        }
        else
        {
            dto.Remotes.Add(entry);
        }

        SaveRemotesConfigDto(dto);
    }

    public bool RemoveRemote(string remoteName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(remoteName);
        var dto = LoadRemotesConfigDto();
        var removed = dto.Remotes.RemoveAll(r => r.Name.Equals(remoteName, StringComparison.OrdinalIgnoreCase)) > 0;
        if (removed)
        {
            var keysToRemove = dto.Upstreams.Where(kv => kv.Value.Remote.Equals(remoteName, StringComparison.OrdinalIgnoreCase)).Select(kv => kv.Key).ToList();
            foreach (var k in keysToRemove)
            {
                dto.Upstreams.Remove(k);
            }
            SaveRemotesConfigDto(dto);

            // Clean up .dawvc/refs/remotes/<remoteName> if it exists
            var remoteRefsDir = Path.Combine(_refsRemotesPath, remoteName);
            if (Directory.Exists(remoteRefsDir))
            {
                try { Directory.Delete(remoteRefsDir, recursive: true); } catch { }
            }
        }
        return removed;
    }

    public (string? Remote, BranchName? RemoteBranch) GetUpstream(BranchName branch)
    {
        var dto = LoadRemotesConfigDto();
        if (dto.Upstreams.TryGetValue(branch.Value, out var u) && !string.IsNullOrWhiteSpace(u.Remote) && !string.IsNullOrWhiteSpace(u.Branch))
        {
            if (BranchName.TryCreate(u.Branch, out var b, out _))
            {
                return (u.Remote, b);
            }
        }
        return (null, null);
    }

    public void SetUpstream(BranchName branch, string remote, BranchName remoteBranch)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(remote);
        var dto = LoadRemotesConfigDto();
        dto.Upstreams[branch.Value] = new UpstreamEntryDto
        {
            Remote = remote,
            Branch = remoteBranch.Value
        };
        SaveRemotesConfigDto(dto);
    }

    public CommitId? GetRemoteTrackingCommit(string remote, BranchName branch)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(remote);
        var relative = branch.Value.Replace('/', Path.DirectorySeparatorChar);
        var refFile = Path.Combine(_refsRemotesPath, remote, relative);
        if (!File.Exists(refFile))
        {
            return null;
        }

        var hashHex = File.ReadAllText(refFile).Trim();
        return CommitId.TryParse(hashHex, out var commitId) ? commitId : null;
    }

    public async Task UpdateRemoteTrackingCommitAsync(string remote, BranchName branch, CommitId commitId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(remote);
        var relative = branch.Value.Replace('/', Path.DirectorySeparatorChar);
        var refFile = Path.Combine(_refsRemotesPath, remote, relative);
        var dir = Path.GetDirectoryName(refFile);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        var bytes = Encoding.UTF8.GetBytes(commitId.ToString() + "\n");
        await AtomicFileWriter.WriteAtomicAsync(
            refFile,
            async stream =>
            {
                await stream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
            },
            cancellationToken).ConfigureAwait(false);
    }

    private RemotesConfigDto LoadRemotesConfigDto()
    {
        if (!File.Exists(_remotesFilePath))
        {
            return new RemotesConfigDto();
        }

        try
        {
            var bytes = File.ReadAllBytes(_remotesFilePath);
            return CanonicalJsonSerializer.Deserialize<RemotesConfigDto>(bytes) ?? new RemotesConfigDto();
        }
        catch
        {
            return new RemotesConfigDto();
        }
    }

    private void SaveRemotesConfigDto(RemotesConfigDto dto)
    {
        var bytes = CanonicalJsonSerializer.SerializeCanonical(dto);
        var dir = Path.GetDirectoryName(_remotesFilePath);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }
        File.WriteAllBytes(_remotesFilePath, bytes);
    }

    private sealed class RemotesConfigDto
    {
        public int SchemaVersion { get; set; } = 1;
        public List<RemoteEntryDto> Remotes { get; set; } = [];
        public Dictionary<string, UpstreamEntryDto> Upstreams { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }

    private sealed class RemoteEntryDto
    {
        public string Name { get; set; } = "";
        public string Url { get; set; } = "";
        public List<string>? FetchSpecs { get; set; }
    }

    private sealed class UpstreamEntryDto
    {
        public string Remote { get; set; } = "";
        public string Branch { get; set; } = "";
    }

    public void Dispose()
    {
        _stagingIndex.Dispose();
        _localBindings.Dispose();
    }
}
