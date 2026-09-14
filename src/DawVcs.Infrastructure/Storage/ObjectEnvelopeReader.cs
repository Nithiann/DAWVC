using DawVcs.Domain.Hashing;
using DawVcs.Domain.Storage;

namespace DawVcs.Infrastructure.Storage;

/// <summary>
/// Reads and validates canonical object envelopes (v1) streaming from source streams.
/// </summary>
public static class ObjectEnvelopeReader
{
    private const int BufferSize = 64 * 1024;

    /// <summary>
    /// Reads and validates the 56-byte envelope header from the stream.
    /// </summary>
    public static async Task<ObjectEnvelopeHeader> ReadHeaderAsync(Stream source, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);

        var buffer = new byte[ObjectEnvelopeHeader.HeaderSize];
        int totalRead = 0;

        while (totalRead < ObjectEnvelopeHeader.HeaderSize)
        {
            int bytesRead = await source.ReadAsync(
                buffer.AsMemory(totalRead, ObjectEnvelopeHeader.HeaderSize - totalRead),
                cancellationToken).ConfigureAwait(false);

            if (bytesRead == 0)
            {
                throw new InvalidEnvelopeException(
                    $"Header is truncated. Expected {ObjectEnvelopeHeader.HeaderSize} bytes but stream ended after {totalRead} bytes.");
            }

            totalRead += bytesRead;
        }

        return ObjectEnvelopeHeader.ReadFrom(buffer);
    }

    /// <summary>
    /// Streams the payload out to a destination stream while verifying length and BLAKE3 hash.
    /// </summary>
    public static async Task ReadAndVerifyPayloadAsync(
        Stream source,
        ObjectEnvelopeHeader header,
        Stream destination,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(destination);

        using var hasher = new Blake3ContentHasher();
        var buffer = new byte[BufferSize];
        ulong remaining = header.PayloadLength;

        while (remaining > 0)
        {
            int toRead = (int)Math.Min((ulong)buffer.Length, remaining);
            int bytesRead = await source.ReadAsync(buffer.AsMemory(0, toRead), cancellationToken).ConfigureAwait(false);

            if (bytesRead == 0)
            {
                throw new PayloadLengthMismatchException(
                    $"Payload ended prematurely. Expected {header.PayloadLength} bytes, but stream terminated with {remaining} bytes remaining.");
            }

            hasher.Update(buffer.AsSpan(0, bytesRead));
            await destination.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken).ConfigureAwait(false);
            remaining -= (ulong)bytesRead;
        }

        var computedHash = hasher.FinalizeHash();
        if (computedHash != header.PayloadHash)
        {
            throw new PayloadHashMismatchException(
                $"Payload hash mismatch! Header declared: {header.PayloadHash}, but computed: {computedHash}.");
        }
    }

    /// <summary>
    /// Reads the payload into a byte array while verifying length and BLAKE3 hash.
    /// </summary>
    public static async Task<byte[]> ReadPayloadToMemoryAndVerifyAsync(
        Stream source,
        ObjectEnvelopeHeader header,
        CancellationToken cancellationToken = default)
    {
        if (header.PayloadLength > int.MaxValue)
        {
            throw new InvalidOperationException($"Payload of {header.PayloadLength} bytes is too large to fit in memory buffer.");
        }

        using var ms = new MemoryStream((int)header.PayloadLength);
        await ReadAndVerifyPayloadAsync(source, header, ms, cancellationToken).ConfigureAwait(false);
        return ms.ToArray();
    }
}
