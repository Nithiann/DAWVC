using DawVcs.Domain.Hashing;
using DawVcs.Domain.Remotes;
using DawVcs.Domain.Storage;

namespace DawVcs.Infrastructure.Storage;

/// <summary>
/// Manages quarantined incoming objects under .dawvc/incoming/<sessionId>/ before atomic promotion
/// to the repository's loose object store (FR-FET-003, FR-FET-007).
/// </summary>
public sealed class QuarantineObjectStore : IQuarantineStore
{
    private readonly string _repositoryRoot;
    private readonly string _objectsRoot;
    private readonly string _incomingRoot;
    private readonly string _sessionDirectory;
    private bool _disposed;

    public string SessionDirectory => _sessionDirectory;

    public QuarantineObjectStore(string repositoryRoot, string? sessionId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);

        _repositoryRoot = Path.GetFullPath(repositoryRoot);
        _objectsRoot = Path.Combine(_repositoryRoot, ".dawvc", "objects");
        _incomingRoot = Path.Combine(_repositoryRoot, ".dawvc", "incoming");

        var sid = !string.IsNullOrWhiteSpace(sessionId) ? sessionId : Guid.NewGuid().ToString("N");
        _sessionDirectory = Path.Combine(_incomingRoot, sid);

        Directory.CreateDirectory(_sessionDirectory);
    }

    public string GetIncomingPath(ContentHash hash)
    {
        return Path.Combine(_sessionDirectory, hash.ToString());
    }

    public Task<Stream> CreateIncomingWriteStreamAsync(ContentHash hash, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var filePath = GetIncomingPath(hash);

        var fileStream = new FileStream(
            filePath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 64 * 1024,
            useAsync: true);

        return Task.FromResult<Stream>(fileStream);
    }

    public Task PromoteAsync(VerifiedIncomingObject verifiedObject, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(verifiedObject);

        if (!File.Exists(verifiedObject.QuarantineFilePath))
        {
            // Already moved or not present
            return Task.CompletedTask;
        }

        var hex = verifiedObject.Hash.ToString();
        var prefix = hex[..2];
        var remainder = hex[2..];
        var targetDir = Path.Combine(_objectsRoot, prefix);
        var targetPath = Path.Combine(targetDir, remainder);

        Directory.CreateDirectory(targetDir);

        if (File.Exists(targetPath))
        {
            // Object already exists in local object store (idempotent deduplication)
            try
            {
                File.Delete(verifiedObject.QuarantineFilePath);
            }
            catch
            {
                // Ignore transient cleanup error
            }
            return Task.CompletedTask;
        }

        // Atomic move into object store
        File.Move(verifiedObject.QuarantineFilePath, targetPath);
        return Task.CompletedTask;
    }

    public async Task PromoteAllAsync(IEnumerable<VerifiedIncomingObject> verifiedObjects, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(verifiedObjects);

        // Enforce safe promotion ordering per FR-FET-007:
        // 1. Blobs first
        // 2. Snapshots second
        // 3. Commits last
        var sorted = verifiedObjects
            .OrderBy(GetSortOrder)
            .ToList();

        foreach (var obj in sorted)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await PromoteAsync(obj, cancellationToken).ConfigureAwait(false);
        }
    }

    private static int GetSortOrder(VerifiedIncomingObject obj) => obj.ObjectType switch
    {
        ObjectType.Blob => 1,
        ObjectType.Tree => 2,
        ObjectType.Snapshot => 3,
        ObjectType.Commit => 4,
        _ => 5
    };

    public void PruneStaleDirectories(TimeSpan maxAge)
    {
        if (!Directory.Exists(_incomingRoot))
        {
            return;
        }

        var threshold = DateTime.UtcNow - maxAge;
        foreach (var dir in Directory.EnumerateDirectories(_incomingRoot))
        {
            if (string.Equals(dir, _sessionDirectory, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            try
            {
                var lastWrite = Directory.GetLastWriteTimeUtc(dir);
                if (lastWrite < threshold)
                {
                    Directory.Delete(dir, recursive: true);
                }
            }
            catch
            {
                // Transient delete failure on stale directories is acceptable
            }
        }
    }

    public void DiscardSession()
    {
        if (Directory.Exists(_sessionDirectory))
        {
            try
            {
                Directory.Delete(_sessionDirectory, recursive: true);
            }
            catch
            {
                // Ignore transient deletion errors
            }
        }
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            // If session directory is empty, clean it up
            if (Directory.Exists(_sessionDirectory))
            {
                try
                {
                    if (!Directory.EnumerateFileSystemEntries(_sessionDirectory).Any())
                    {
                        Directory.Delete(_sessionDirectory);
                    }
                }
                catch
                {
                    // Ignore cleanup error
                }
            }

            _disposed = true;
        }
    }
}
