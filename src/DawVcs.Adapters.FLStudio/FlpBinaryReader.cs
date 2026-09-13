using System.Buffers.Binary;
using System.Text;

using DawVcs.Adapters.Abstractions;

namespace DawVcs.Adapters.FLStudio;

/// <summary>
/// Bounded binary reader for FL Studio (.flp) projects.
/// Reads chunks and event streams safely without loading entire files into memory.
/// </summary>
public static class FlpBinaryReader
{
    private const int MaxMetadataScanBytes = 2 * 1024 * 1024; // 2 MB bound for initial metadata scan
    private static readonly byte[] FldtMagic = [0x46, 0x4C, 0x64, 0x74]; // ASCII 'FLdt'

    public record FlpInspectionResult(
        FlpHeader Header,
        string? Version,
        string? Title,
        IReadOnlyList<string> SamplePaths,
        IReadOnlyList<string> PluginNames);

    /// <summary>
    /// Inspects the header and scans the initial event stream of an FLP project.
    /// </summary>
    public static async Task<FlpInspectionResult> InspectAsync(
        Stream stream,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);

        // 1. Read FLhd chunk (14 bytes)
        var headerBuffer = new byte[FlpHeader.ExpectedHeaderChunkSize];
        int headerBytesRead = await stream.ReadAsync(headerBuffer.AsMemory(0, headerBuffer.Length), cancellationToken).ConfigureAwait(false);

        if (!FlpHeader.TryParse(headerBuffer.AsSpan(0, headerBytesRead), out var header, out var headerError))
        {
            throw new InvalidFlpException(headerError ?? "Invalid FLP header.");
        }

        // 2. Read FLdt chunk header (8 bytes: 4 bytes magic + 4 bytes length)
        var chunkHeader = new byte[8];
        int chunkBytesRead = await stream.ReadAsync(chunkHeader.AsMemory(0, 8), cancellationToken).ConfigureAwait(false);
        if (chunkBytesRead < 8 || !chunkHeader.AsSpan(0, 4).SequenceEqual(FldtMagic))
        {
            throw new InvalidFlpException("Missing or invalid 'FLdt' data chunk header.");
        }

        uint dataChunkLength = BinaryPrimitives.ReadUInt32LittleEndian(chunkHeader.AsSpan(4, 4));

        // 3. Scan events in FLdt
        string? detectedVersion = null;
        string? detectedTitle = null;
        var samplePaths = new List<string>();
        var pluginNames = new List<string>();

        long bytesScanned = 0;
        long scanLimit = Math.Min((long)dataChunkLength, MaxMetadataScanBytes);

        var byteBuffer = new byte[1];

        while (bytesScanned < scanLimit)
        {
            cancellationToken.ThrowIfCancellationRequested();

            int read = await stream.ReadAsync(byteBuffer.AsMemory(0, 1), cancellationToken).ConfigureAwait(false);
            if (read == 0) break;
            bytesScanned++;

            byte eventId = byteBuffer[0];

            if (eventId < 64)
            {
                // 1-byte data
                if (stream.CanSeek)
                {
                    stream.Seek(1, SeekOrigin.Current);
                }
                else
                {
                    await stream.ReadAsync(byteBuffer.AsMemory(0, 1), cancellationToken).ConfigureAwait(false);
                }
                bytesScanned += 1;
            }
            else if (eventId < 128)
            {
                // 2-byte data (Word)
                if (stream.CanSeek)
                {
                    stream.Seek(2, SeekOrigin.Current);
                }
                else
                {
                    var skip = new byte[2];
                    await stream.ReadAsync(skip.AsMemory(0, 2), cancellationToken).ConfigureAwait(false);
                }
                bytesScanned += 2;
            }
            else if (eventId < 192)
            {
                // 4-byte data (DWord)
                if (stream.CanSeek)
                {
                    stream.Seek(4, SeekOrigin.Current);
                }
                else
                {
                    var skip = new byte[4];
                    await stream.ReadAsync(skip.AsMemory(0, 4), cancellationToken).ConfigureAwait(false);
                }
                bytesScanned += 4;
            }
            else
            {
                // Variable length data (Event >= 192)
                int payloadLength = await ReadVariableLengthAsync(stream, cancellationToken).ConfigureAwait(false);
                if (payloadLength < 0 || payloadLength > 10 * 1024 * 1024) // Sanity check: 10 MB per event
                {
                    break;
                }

                if (eventId == 199 && detectedVersion is null) // FL Studio Version string
                {
                    var textBuffer = new byte[payloadLength];
                    int totalRead = await ReadExactAsync(stream, textBuffer, cancellationToken).ConfigureAwait(false);
                    bytesScanned += totalRead;

                    detectedVersion = CleanString(Encoding.ASCII.GetString(textBuffer, 0, totalRead));
                }
                else if (eventId == 201 && detectedTitle is null) // Project Title
                {
                    var textBuffer = new byte[payloadLength];
                    int totalRead = await ReadExactAsync(stream, textBuffer, cancellationToken).ConfigureAwait(false);
                    bytesScanned += totalRead;

                    detectedTitle = CleanString(Encoding.UTF8.GetString(textBuffer, 0, totalRead));
                }
                else if (eventId == 203) // Sample path
                {
                    var textBuffer = new byte[payloadLength];
                    int totalRead = await ReadExactAsync(stream, textBuffer, cancellationToken).ConfigureAwait(false);
                    bytesScanned += totalRead;

                    var path = CleanString(Encoding.UTF8.GetString(textBuffer, 0, totalRead));
                    if (!string.IsNullOrWhiteSpace(path) && !samplePaths.Contains(path))
                    {
                        samplePaths.Add(path);
                    }
                }
                else if (eventId == 214) // Plugin name
                {
                    var textBuffer = new byte[payloadLength];
                    int totalRead = await ReadExactAsync(stream, textBuffer, cancellationToken).ConfigureAwait(false);
                    bytesScanned += totalRead;

                    var name = CleanString(Encoding.UTF8.GetString(textBuffer, 0, totalRead));
                    if (!string.IsNullOrWhiteSpace(name) && !pluginNames.Contains(name))
                    {
                        pluginNames.Add(name);
                    }
                }
                else
                {
                    // Skip unhandled variable-length payload
                    if (stream.CanSeek)
                    {
                        stream.Seek(payloadLength, SeekOrigin.Current);
                    }
                    else
                    {
                        var discard = new byte[Math.Min(payloadLength, 8192)];
                        int remaining = payloadLength;
                        while (remaining > 0)
                        {
                            int toRead = Math.Min(remaining, discard.Length);
                            int r = await stream.ReadAsync(discard.AsMemory(0, toRead), cancellationToken).ConfigureAwait(false);
                            if (r == 0) break;
                            remaining -= r;
                        }
                    }
                    bytesScanned += payloadLength;
                }
            }
        }

        return new FlpInspectionResult(
            header,
            detectedVersion,
            detectedTitle,
            samplePaths,
            pluginNames);
    }

    private static async Task<int> ReadVariableLengthAsync(Stream stream, CancellationToken cancellationToken)
    {
        int length = 0;
        int shift = 0;
        var buf = new byte[1];

        while (true)
        {
            int read = await stream.ReadAsync(buf.AsMemory(0, 1), cancellationToken).ConfigureAwait(false);
            if (read == 0) return -1;

            byte b = buf[0];
            length |= (b & 0x7F) << shift;
            if ((b & 0x80) == 0) break;
            shift += 7;

            if (shift > 28) // Prevent overflow
                return -1;
        }

        return length;
    }

    private static async Task<int> ReadExactAsync(Stream stream, byte[] buffer, CancellationToken cancellationToken)
    {
        int total = 0;
        while (total < buffer.Length)
        {
            int r = await stream.ReadAsync(buffer.AsMemory(total, buffer.Length - total), cancellationToken).ConfigureAwait(false);
            if (r == 0) break;
            total += r;
        }
        return total;
    }

    private static string CleanString(string s)
    {
        return s.Trim('\0', ' ', '\r', '\n');
    }
}

public class InvalidFlpException : Exception
{
    public InvalidFlpException(string message) : base(message) { }
}
