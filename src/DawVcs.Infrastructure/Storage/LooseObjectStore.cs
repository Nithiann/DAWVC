using DawVcs.Domain.Hashing;
using DawVcs.Domain.Storage;
using DawVcs.Infrastructure.FileSystem;

namespace DawVcs.Infrastructure.Storage;

/// <summary>
/// Content-addressed loose object store storing enveloped objects under .dawvc/objects/xx/yyy...
/// Guarantees atomic writes, deduplication, hash collision defense, and crash resilience.
/// </summary>
public sealed class LooseObjectStore : IObjectStore
{
    private readonly string _objectsRoot;
    private readonly string _tempRoot;
    private readonly Action<string>? _onBeforeAtomicPublish;

    public string ObjectsRoot => _objectsRoot;

    public LooseObjectStore(string repositoryRoot, Action<string>? onBeforeAtomicPublish = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);

        _objectsRoot = Path.Combine(Path.GetFullPath(repositoryRoot), ".dawvc", "objects");
        _tempRoot = Path.Combine(_objectsRoot, ".tmp");

        Directory.CreateDirectory(_objectsRoot);
        Directory.CreateDirectory(_tempRoot);

        _onBeforeAtomicPublish = onBeforeAtomicPublish;
    }

    public string GetObjectPath(ContentHash hash)
    {
        var hex = hash.ToString();
        var prefix = hex[..2];
        var remainder = hex[2..];
        return Path.Combine(_objectsRoot, prefix, remainder);
    }

    public bool Exists(ContentHash hash)
    {
        return File.Exists(GetObjectPath(hash));
    }

    public async Task<ContentHash> WriteBlobAsync(Stream payloadStream, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(payloadStream);

        var tempFilePath = Path.Combine(_tempRoot, $"blob_{Guid.NewGuid():N}.tmp");

        try
        {
            ObjectEnvelopeHeader header;
            var fileOptions = new FileStreamOptions
            {
                Mode = FileMode.CreateNew,
                Access = FileAccess.ReadWrite,
                Share = FileShare.None,
                Options = FileOptions.Asynchronous
            };

            await using (var tempFileStream = new FileStream(tempFilePath, fileOptions))
            {
                header = await ObjectEnvelopeWriter.WriteEnvelopeStreamingAsync(
                    tempFileStream,
                    payloadStream,
                    ObjectType.Blob,
                    cancellationToken).ConfigureAwait(false);

                tempFileStream.Flush(flushToDisk: true);
            }

            return await PublishObjectAsync(tempFilePath, header, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            TryDeleteFile(tempFilePath);
            throw;
        }
    }

    public async Task<ContentHash> WriteObjectAsync(
        ObjectType type,
        ReadOnlyMemory<byte> payload,
        CancellationToken cancellationToken = default)
    {
        var hash = Blake3ContentHasher.Hash(payload.Span);
        var targetPath = GetObjectPath(hash);

        if (File.Exists(targetPath))
        {
            // Deduplication & collision verification
            await VerifyExistingObjectAsync(targetPath, hash, payload.Length).ConfigureAwait(false);
            return hash;
        }

        var header = new ObjectEnvelopeHeader(type, (ulong)payload.Length, hash);
        var targetDir = Path.GetDirectoryName(targetPath)!;
        Directory.CreateDirectory(targetDir);

        await AtomicFileWriter.WriteAtomicAsync(
            targetPath,
            async targetStream =>
            {
                if (_onBeforeAtomicPublish is not null)
                {
                    _onBeforeAtomicPublish(targetPath);
                }

                await ObjectEnvelopeWriter.WriteHeaderAsync(targetStream, header, cancellationToken).ConfigureAwait(false);
                await targetStream.WriteAsync(payload, cancellationToken).ConfigureAwait(false);
            },
            cancellationToken).ConfigureAwait(false);

        return hash;
    }

    public async Task<Stream> OpenPayloadStreamAsync(ContentHash hash, CancellationToken cancellationToken = default)
    {
        var path = GetObjectPath(hash);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"Object with hash '{hash}' was not found in the object store.", path);
        }

        var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, FileOptions.Asynchronous);
        var header = await ObjectEnvelopeReader.ReadHeaderAsync(stream, cancellationToken).ConfigureAwait(false);

        if (header.PayloadHash != hash)
        {
            stream.Dispose();
            throw new PayloadHashMismatchException($"Envelope header hash mismatch! Expected {hash}, but header has {header.PayloadHash}.");
        }

        return new PayloadSliceStream(stream, ObjectEnvelopeHeader.HeaderSize, (long)header.PayloadLength);
    }

    public async Task<byte[]> ReadObjectPayloadAsync(ContentHash hash, CancellationToken cancellationToken = default)
    {
        var path = GetObjectPath(hash);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"Object with hash '{hash}' was not found in the object store.", path);
        }

        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, FileOptions.Asynchronous);
        var header = await ObjectEnvelopeReader.ReadHeaderAsync(stream, cancellationToken).ConfigureAwait(false);

        if (header.PayloadHash != hash)
        {
            throw new PayloadHashMismatchException($"Envelope header hash mismatch! Expected {hash}, but header has {header.PayloadHash}.");
        }

        return await ObjectEnvelopeReader.ReadPayloadToMemoryAndVerifyAsync(stream, header, cancellationToken).ConfigureAwait(false);
    }

    public async Task<ObjectEnvelopeHeader> ReadHeaderAsync(ContentHash hash, CancellationToken cancellationToken = default)
    {
        var path = GetObjectPath(hash);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"Object with hash '{hash}' was not found in the object store.", path);
        }

        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return await ObjectEnvelopeReader.ReadHeaderAsync(stream, cancellationToken).ConfigureAwait(false);
    }

    public IReadOnlyList<StoredObjectEntry> EnumerateStoredObjects()
    {
        var entries = new List<StoredObjectEntry>();
        if (!Directory.Exists(_objectsRoot))
        {
            return entries;
        }

        foreach (var subDir in Directory.GetDirectories(_objectsRoot))
        {
            var dirName = Path.GetFileName(subDir);
            if (dirName.StartsWith('.') || dirName.Length != 2)
            {
                continue;
            }

            foreach (var file in Directory.GetFiles(subDir))
            {
                var fileName = Path.GetFileName(file);
                if (fileName.StartsWith('.'))
                {
                    continue;
                }

                var relPath = $"{dirName}/{fileName}";
                var fullHashHex = dirName + fileName;
                if (ContentHash.TryParse(fullHashHex, out var hash))
                {
                    entries.Add(new StoredObjectEntry(relPath, hash, true));
                }
                else
                {
                    entries.Add(new StoredObjectEntry(relPath, null, false));
                }
            }
        }

        return entries;
    }

    public async Task VerifyObjectIntegrityAsync(ContentHash hash, CancellationToken cancellationToken = default)
    {
        var path = GetObjectPath(hash);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"Object with hash '{hash}' was not found in the object store.", path);
        }

        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, FileOptions.Asynchronous);
        var header = await ObjectEnvelopeReader.ReadHeaderAsync(stream, cancellationToken).ConfigureAwait(false);

        if (header.PayloadHash != hash)
        {
            throw new PayloadHashMismatchException($"Envelope header hash mismatch! Expected {hash}, but header has {header.PayloadHash}.");
        }

        using var nullStream = Stream.Null;
        await ObjectEnvelopeReader.ReadAndVerifyPayloadAsync(stream, header, nullStream, cancellationToken).ConfigureAwait(false);
    }

    private async Task<ContentHash> PublishObjectAsync(
        string tempFilePath,
        ObjectEnvelopeHeader header,
        CancellationToken cancellationToken)
    {
        var hash = header.PayloadHash;
        var targetPath = GetObjectPath(hash);
        var targetDirectory = Path.GetDirectoryName(targetPath)!;

        Directory.CreateDirectory(targetDirectory);

        if (File.Exists(targetPath))
        {
            // Object already exists -> deduplication check
            await VerifyExistingObjectAsync(targetPath, hash, (long)header.PayloadLength).ConfigureAwait(false);
            TryDeleteFile(tempFilePath);
            return hash;
        }

        if (_onBeforeAtomicPublish is not null)
        {
            _onBeforeAtomicPublish(tempFilePath);
        }

        File.Move(tempFilePath, targetPath, overwrite: false);
        return hash;
    }

    private static async Task VerifyExistingObjectAsync(string existingPath, ContentHash expectedHash, long expectedPayloadLength)
    {
        await using var stream = new FileStream(existingPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        var header = await ObjectEnvelopeReader.ReadHeaderAsync(stream).ConfigureAwait(false);

        if (header.PayloadHash != expectedHash || (long)header.PayloadLength != expectedPayloadLength)
        {
            throw new HashCollisionException(
                $"Hash collision detected! Target '{existingPath}' declares hash '{header.PayloadHash}' and length {header.PayloadLength}, but incoming object expected '{expectedHash}' and length {expectedPayloadLength}.");
        }
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch
        {
            // Ignore cleanup errors
        }
    }
}

/// <summary>
/// Read-only stream wrapper presenting a slice of an underlying file stream (excluding the 56-byte header).
/// </summary>
internal sealed class PayloadSliceStream : Stream
{
    private readonly FileStream _underlying;
    private readonly long _offset;
    private readonly long _length;
    private long _position;

    public PayloadSliceStream(FileStream underlying, long offset, long length)
    {
        _underlying = underlying;
        _offset = offset;
        _length = length;
        _position = 0;
        _underlying.Seek(_offset, SeekOrigin.Begin);
    }

    public override bool CanRead => true;
    public override bool CanSeek => true;
    public override bool CanWrite => false;
    public override long Length => _length;

    public override long Position
    {
        get => _position;
        set => Seek(value, SeekOrigin.Begin);
    }

    public override void Flush() { }

    public override int Read(byte[] buffer, int offset, int count)
    {
        long remaining = _length - _position;
        if (remaining <= 0) return 0;

        int toRead = (int)Math.Min(count, remaining);
        int read = _underlying.Read(buffer, offset, toRead);
        _position += read;
        return read;
    }

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        long remaining = _length - _position;
        if (remaining <= 0) return 0;

        int toRead = (int)Math.Min((long)buffer.Length, remaining);
        int read = await _underlying.ReadAsync(buffer[..toRead], cancellationToken).ConfigureAwait(false);
        _position += read;
        return read;
    }

    public override long Seek(long offset, SeekOrigin origin)
    {
        long newPos = origin switch
        {
            SeekOrigin.Begin => offset,
            SeekOrigin.Current => _position + offset,
            SeekOrigin.End => _length + offset,
            _ => throw new ArgumentOutOfRangeException(nameof(origin))
        };

        if (newPos < 0 || newPos > _length)
            throw new ArgumentOutOfRangeException(nameof(offset), "Seek position out of payload bounds.");

        _position = newPos;
        _underlying.Seek(_offset + _position, SeekOrigin.Begin);
        return _position;
    }

    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _underlying.Dispose();
        }
        base.Dispose(disposing);
    }
}
