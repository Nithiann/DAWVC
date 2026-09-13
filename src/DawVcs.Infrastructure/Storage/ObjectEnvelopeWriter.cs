using DawVcs.Domain.Hashing;
using DawVcs.Domain.Storage;

namespace DawVcs.Infrastructure.Storage;

/// <summary>
/// Writes canonical object envelopes (v1) streaming to destination streams.
/// </summary>
public static class ObjectEnvelopeWriter
{
    private const int BufferSize = 64 * 1024;

    /// <summary>
    /// Writes the 56-byte envelope header synchronously to the stream.
    /// </summary>
    public static void WriteHeader(Stream destination, ObjectEnvelopeHeader header)
    {
        ArgumentNullException.ThrowIfNull(destination);
        Span<byte> buffer = stackalloc byte[ObjectEnvelopeHeader.HeaderSize];
        header.WriteTo(buffer);
        destination.Write(buffer);
    }

    /// <summary>
    /// Writes the 56-byte envelope header asynchronously to the stream.
    /// </summary>
    public static async Task WriteHeaderAsync(Stream destination, ObjectEnvelopeHeader header, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(destination);
        var buffer = new byte[ObjectEnvelopeHeader.HeaderSize];
        header.WriteTo(buffer);
        await destination.WriteAsync(buffer.AsMemory(0, buffer.Length), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Writes an entire envelope to a seekable stream by writing a placeholder header, streaming the payload while
    /// computing its BLAKE3 hash and byte length, and then rewriting the finalized header at position 0.
    /// </summary>
    public static async Task<ObjectEnvelopeHeader> WriteEnvelopeStreamingAsync(
        Stream destination,
        Stream payloadSource,
        ObjectType objectType,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentNullException.ThrowIfNull(payloadSource);

        if (!destination.CanSeek)
        {
            throw new ArgumentException("Destination stream must be seekable to support in-place envelope header finalization.", nameof(destination));
        }

        long startPosition = destination.Position;

        // Step 1: Write dummy placeholder header
        var dummyHeader = new ObjectEnvelopeHeader(objectType, 0, ContentHash.Empty);
        await WriteHeaderAsync(destination, dummyHeader, cancellationToken).ConfigureAwait(false);

        // Step 2: Stream payload while hashing
        using var hasher = new Blake3ContentHasher();
        var buffer = new byte[BufferSize];
        ulong totalBytesRead = 0;
        int bytesRead;

        while ((bytesRead = await payloadSource.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken).ConfigureAwait(false)) > 0)
        {
            hasher.Update(buffer.AsSpan(0, bytesRead));
            await destination.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken).ConfigureAwait(false);
            totalBytesRead += (ulong)bytesRead;
        }

        var finalHash = hasher.FinalizeHash();
        var finalHeader = new ObjectEnvelopeHeader(objectType, totalBytesRead, finalHash);

        // Step 3: Seek back to start position and rewrite header
        long endPosition = destination.Position;
        destination.Position = startPosition;
        await WriteHeaderAsync(destination, finalHeader, cancellationToken).ConfigureAwait(false);

        // Restore position to end
        destination.Position = endPosition;

        return finalHeader;
    }
}
