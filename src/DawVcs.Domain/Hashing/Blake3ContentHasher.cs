namespace DawVcs.Domain.Hashing;

/// <summary>
/// High-performance BLAKE3 implementation of <see cref="IContentHasher"/> using SIMD-accelerated native primitives.
/// </summary>
public sealed class Blake3ContentHasher : IContentHasher
{
    private const int StreamBufferSize = 64 * 1024; // 64 KB streaming buffer
    private Blake3.Hasher? _hasher;
    private bool _disposed;

    public Blake3ContentHasher()
    {
        _hasher = Blake3.Hasher.New();
    }

    /// <summary>
    /// Computes the BLAKE3 hash of a single in-memory buffer.
    /// </summary>
    public static ContentHash Hash(ReadOnlySpan<byte> data)
    {
        using var hasher = new Blake3ContentHasher();
        hasher.Update(data);
        return hasher.FinalizeHash();
    }

    /// <summary>
    /// Computes the BLAKE3 hash of a stream asynchronously without loading the entire stream into memory.
    /// </summary>
    public static async Task<ContentHash> HashAsync(Stream stream, CancellationToken cancellationToken = default)
    {
        using var hasher = new Blake3ContentHasher();
        await hasher.UpdateAsync(stream, cancellationToken).ConfigureAwait(false);
        return hasher.FinalizeHash();
    }

    public void Update(ReadOnlySpan<byte> data)
    {
        ObjectDisposedException.ThrowIf(_disposed || _hasher is null, this);
        _hasher.Update(data);
    }

    public async Task UpdateAsync(Stream stream, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed || _hasher is null, this);
        ArgumentNullException.ThrowIfNull(stream);

        var buffer = new byte[StreamBufferSize];
        int bytesRead;
        while ((bytesRead = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken).ConfigureAwait(false)) > 0)
        {
            _hasher.Update(buffer.AsSpan(0, bytesRead));
        }
    }

    public ContentHash FinalizeHash()
    {
        ObjectDisposedException.ThrowIf(_disposed || _hasher is null, this);
        var hash = _hasher.Finalize();
        return new ContentHash(hash.AsSpan());
    }

    public void Reset()
    {
        ObjectDisposedException.ThrowIf(_disposed || _hasher is null, this);
        _hasher.Reset();
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _hasher?.Dispose();
            _hasher = null;
            _disposed = true;
        }
    }
}
