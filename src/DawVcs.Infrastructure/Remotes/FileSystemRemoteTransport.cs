using DawVcs.Domain.Common;
using DawVcs.Domain.Hashing;
using DawVcs.Domain.Remotes;
using DawVcs.Infrastructure.Configuration;

namespace DawVcs.Infrastructure.Remotes;

/// <summary>
/// Remote transport communicating with local directory paths, external/USB drives, or UNC network shares (FR-REM-001..005).
/// Supports both non-bare repositories (with a .dawvc subdirectory) and bare repositories.
/// </summary>
public sealed class FileSystemRemoteTransport : IRemoteTransport
{
    private readonly string _remoteUrl;
    private readonly string _configDir;
    private readonly string _dotDawvcPath;
    private readonly string _objectsPath;
    private readonly string _refsHeadsPath;

    public string RemoteUrl => _remoteUrl;

    public FileSystemRemoteTransport(string remoteUrl)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(remoteUrl);

        _remoteUrl = remoteUrl;
        var fullPath = Path.GetFullPath(remoteUrl);

        // Check if non-bare (.dawvc folder exists inside) or bare (objects folder exists in root alongside dawvc.yaml)
        if (Directory.Exists(Path.Combine(fullPath, ".dawvc")))
        {
            _configDir = fullPath;
            _dotDawvcPath = Path.Combine(fullPath, ".dawvc");
        }
        else if (Directory.Exists(Path.Combine(fullPath, "objects")) && File.Exists(Path.Combine(fullPath, YamlRepositoryConfigManager.ConfigFileName)))
        {
            // Bare repository
            _configDir = fullPath;
            _dotDawvcPath = fullPath;
        }
        else
        {
            // Default expectation (non-bare)
            _configDir = fullPath;
            _dotDawvcPath = Path.Combine(fullPath, ".dawvc");
        }

        _objectsPath = Path.Combine(_dotDawvcPath, "objects");
        _refsHeadsPath = Path.Combine(_dotDawvcPath, "refs", "heads");
    }

    public Task<RemoteHandshake> HandshakeAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!Directory.Exists(_configDir))
        {
            throw new RemoteException($"Remote path '{_remoteUrl}' does not exist or is inaccessible.");
        }

        try
        {
            var config = YamlRepositoryConfigManager.ReadFromDirectory(_configDir);
            return Task.FromResult(new RemoteHandshake(config.SchemaVersion, config.RepositoryId, "0.2.0"));
        }
        catch (FileNotFoundException ex)
        {
            throw new RemoteException($"Remote repository at '{_remoteUrl}' is missing configuration file 'dawvc.yaml'.", ex);
        }
        catch (Exception ex)
        {
            throw new RemoteException($"Failed to validate remote repository at '{_remoteUrl}': {ex.Message}", ex);
        }
    }

    public async Task<IReadOnlyList<RemoteRef>> ListRefsAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!Directory.Exists(_refsHeadsPath))
        {
            return Array.Empty<RemoteRef>();
        }

        var results = new List<RemoteRef>();
        var files = Directory.GetFiles(_refsHeadsPath, "*", SearchOption.AllDirectories);

        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var rel = Path.GetRelativePath(_refsHeadsPath, file).Replace('\\', '/');
            var hashHex = (await File.ReadAllTextAsync(file, cancellationToken).ConfigureAwait(false)).Trim();

            if (CommitId.TryParse(hashHex, out var commitId))
            {
                results.Add(new RemoteRef(rel, commitId));
            }
        }

        return results;
    }

    public Task<Stream?> OpenObjectPayloadStreamAsync(ContentHash hash, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var hashStr = hash.ToString();
        var prefix = hashStr[..2].ToLowerInvariant();
        var rest = hashStr[2..].ToLowerInvariant();

        var objectPath = Path.Combine(_objectsPath, prefix, rest);
        if (!File.Exists(objectPath))
        {
            return Task.FromResult<Stream?>(null);
        }

        var stream = new FileStream(objectPath, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, FileOptions.Asynchronous);
        return Task.FromResult<Stream?>(stream);
    }

    public Task<bool> ObjectExistsAsync(ContentHash hash, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var hashStr = hash.ToString();
        var prefix = hashStr[..2].ToLowerInvariant();
        var rest = hashStr[2..].ToLowerInvariant();

        var objectPath = Path.Combine(_objectsPath, prefix, rest);
        return Task.FromResult(File.Exists(objectPath));
    }

    public void Dispose()
    {
    }
}
