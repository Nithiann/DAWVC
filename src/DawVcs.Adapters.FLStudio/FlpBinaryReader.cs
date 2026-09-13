using System.Buffers.Binary;
using System.Text;

namespace DawVcs.Adapters.FLStudio;

/// <summary>
/// Begrensde binaire lezer voor FL Studio (.flp) projectbestanden (IMP-0505).
/// Leest headers, data chunks en events met strikte limieten en bounds-checks
/// zonder het gehele bestand in geheugen te laden (NFR-SEC-001, NFR-SEC-004).
/// </summary>
public static class FlpBinaryReader
{
    public const int MaxMetadataScanBytes = 2 * 1024 * 1024; // 2 MB scanlimiet voor headers en metadata
    public const int MaxEventPayloadBytes = 10 * 1024 * 1024; // 10 MB sanity check per event

    private static readonly byte[] FldtMagic = [0x46, 0x4C, 0x64, 0x74]; // ASCII 'FLdt'

    public sealed record FlpInspectionResult(
        FlpHeader Header,
        string? Version,
        string? Title,
        string? Comments,
        double? TempoBpm,
        IReadOnlyList<string> SamplePaths,
        IReadOnlyList<string> PluginNames,
        bool IsSuspicious,
        string? SuspiciousReason,
        long BytesScanned);

    /// <summary>
    /// Inspecteert de FLhd header en scant begrends de initiële events in de FLdt chunk.
    /// </summary>
    public static async Task<FlpInspectionResult> InspectAsync(
        Stream stream,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);

        // 1. Lees FLhd chunk (14 bytes)
        var headerBuffer = new byte[FlpHeader.ExpectedHeaderChunkSize];
        int headerBytesRead = await ReadExactAsync(stream, headerBuffer, cancellationToken).ConfigureAwait(false);

        if (headerBytesRead < FlpHeader.ExpectedHeaderChunkSize)
        {
            throw new InvalidFlpException("Invalid FLP header: bestand te kort (< 14 bytes).");
        }

        if (!FlpHeader.TryParse(headerBuffer.AsSpan(0, headerBytesRead), out var header, out var headerError))
        {
            throw new InvalidFlpException(headerError ?? "Invalid FLP: Ongeldige FLhd projectheader.");
        }

        // 2. Lees FLdt chunk header (8 bytes: 4 bytes magic + 4 bytes length)
        var chunkHeader = new byte[8];
        int chunkBytesRead = await ReadExactAsync(stream, chunkHeader, cancellationToken).ConfigureAwait(false);
        if (chunkBytesRead < 8 || !chunkHeader.AsSpan(0, 4).SequenceEqual(FldtMagic))
        {
            throw new InvalidFlpException("Ontbrekende of ongeldige 'FLdt' data chunk header.");
        }

        uint dataChunkLength = BinaryPrimitives.ReadUInt32LittleEndian(chunkHeader.AsSpan(4, 4));

        // 3. Scan events in FLdt
        string? detectedVersion = null;
        string? detectedTitle = null;
        string? detectedComments = null;
        double? detectedTempo = null;
        var samplePaths = new List<string>();
        var pluginNames = new List<string>();
        bool isSuspicious = false;
        string? suspiciousReason = null;

        long bytesScanned = 0;
        long scanLimit = Math.Min((long)dataChunkLength, MaxMetadataScanBytes);

        var byteBuffer = new byte[4];

        while (bytesScanned < scanLimit)
        {
            cancellationToken.ThrowIfCancellationRequested();

            int read = await stream.ReadAsync(byteBuffer.AsMemory(0, 1), cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                // Onverwacht einde van stream vóór bereiken van scanlimiet
                if (bytesScanned < dataChunkLength && stream.CanSeek && stream.Length < (dataChunkLength + FlpHeader.ExpectedHeaderChunkSize + 8))
                {
                    isSuspicious = true;
                    suspiciousReason = $"Stream is afgekapt na {bytesScanned} bytes terwijl FLdt chunk {dataChunkLength} bytes declareert.";
                }
                break;
            }
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
                if (eventId == 68 && detectedTempo == null) // Integer tempo
                {
                    int r = await ReadExactAsync(stream, byteBuffer.AsMemory(0, 2), cancellationToken).ConfigureAwait(false);
                    bytesScanned += r;
                    if (r == 2)
                    {
                        ushort bpm = BinaryPrimitives.ReadUInt16LittleEndian(byteBuffer.AsSpan(0, 2));
                        if (bpm is >= 10 and <= 999)
                        {
                            detectedTempo = bpm;
                        }
                    }
                }
                else
                {
                    if (stream.CanSeek)
                    {
                        stream.Seek(2, SeekOrigin.Current);
                    }
                    else
                    {
                        await ReadExactAsync(stream, byteBuffer.AsMemory(0, 2), cancellationToken).ConfigureAwait(false);
                    }
                    bytesScanned += 2;
                }
            }
            else if (eventId < 192)
            {
                // 4-byte data (DWord)
                if (eventId == 156) // Fine tempo (DWORD in micro-BPM: bpm = dword / 1000.0)
                {
                    int r = await ReadExactAsync(stream, byteBuffer.AsMemory(0, 4), cancellationToken).ConfigureAwait(false);
                    bytesScanned += r;
                    if (r == 4)
                    {
                        uint rawTempo = BinaryPrimitives.ReadUInt32LittleEndian(byteBuffer.AsSpan(0, 4));
                        double bpm = rawTempo / 1000.0;
                        if (bpm is >= 10.0 and <= 999.0)
                        {
                            detectedTempo = Math.Round(bpm, 2);
                        }
                    }
                }
                else
                {
                    if (stream.CanSeek)
                    {
                        stream.Seek(4, SeekOrigin.Current);
                    }
                    else
                    {
                        await ReadExactAsync(stream, byteBuffer.AsMemory(0, 4), cancellationToken).ConfigureAwait(false);
                    }
                    bytesScanned += 4;
                }
            }
            else
            {
                // Variable length data (Event >= 192)
                var (payloadLength, lenBytes) = await ReadVariableLengthAsync(stream, cancellationToken).ConfigureAwait(false);
                bytesScanned += lenBytes;

                if (payloadLength < 0 || payloadLength > MaxEventPayloadBytes)
                {
                    // Malformed LEB128 of buiten proportionele eventlengte
                    isSuspicious = true;
                    suspiciousReason = $"Ongeldige variabele eventlengte ({payloadLength} bytes) aangetroffen bij event {eventId}.";
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

                    detectedTitle = DecodeFlpString(textBuffer, totalRead);
                }
                else if (eventId == 200 && detectedComments is null) // Comments / info
                {
                    var textBuffer = new byte[payloadLength];
                    int totalRead = await ReadExactAsync(stream, textBuffer, cancellationToken).ConfigureAwait(false);
                    bytesScanned += totalRead;

                    detectedComments = DecodeFlpString(textBuffer, totalRead);
                }
                else if (eventId == 203) // Sample path / recording locator
                {
                    var textBuffer = new byte[payloadLength];
                    int totalRead = await ReadExactAsync(stream, textBuffer, cancellationToken).ConfigureAwait(false);
                    bytesScanned += totalRead;

                    var path = DecodeFlpString(textBuffer, totalRead);
                    if (!string.IsNullOrWhiteSpace(path) && !samplePaths.Contains(path))
                    {
                        samplePaths.Add(path);
                    }
                }
                else if (eventId == 214) // Plugin name / generator/effect reference
                {
                    var textBuffer = new byte[payloadLength];
                    int totalRead = await ReadExactAsync(stream, textBuffer, cancellationToken).ConfigureAwait(false);
                    bytesScanned += totalRead;

                    var name = DecodeFlpString(textBuffer, totalRead);
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
            detectedComments,
            detectedTempo,
            samplePaths,
            pluginNames,
            isSuspicious,
            suspiciousReason,
            bytesScanned);
    }

    private static async Task<(int Length, int BytesConsumed)> ReadVariableLengthAsync(Stream stream, CancellationToken cancellationToken)
    {
        int length = 0;
        int shift = 0;
        int bytesConsumed = 0;
        var buf = new byte[1];

        while (true)
        {
            int read = await stream.ReadAsync(buf.AsMemory(0, 1), cancellationToken).ConfigureAwait(false);
            if (read == 0) return (-1, bytesConsumed);
            bytesConsumed++;

            byte b = buf[0];
            length |= (b & 0x7F) << shift;
            if ((b & 0x80) == 0) break;
            shift += 7;

            if (shift > 28) // Prevent integer overflow on malformed LEB128
            {
                return (-1, bytesConsumed);
            }
        }

        return (length, bytesConsumed);
    }

    private static async Task<int> ReadExactAsync(Stream stream, Memory<byte> buffer, CancellationToken cancellationToken)
    {
        int total = 0;
        while (total < buffer.Length)
        {
            int r = await stream.ReadAsync(buffer.Slice(total), cancellationToken).ConfigureAwait(false);
            if (r == 0) break;
            total += r;
        }
        return total;
    }

    private static async Task<int> ReadExactAsync(Stream stream, byte[] buffer, CancellationToken cancellationToken)
    {
        return await ReadExactAsync(stream, buffer.AsMemory(), cancellationToken).ConfigureAwait(false);
    }

    private static string DecodeFlpString(byte[] buffer, int length)
    {
        if (length <= 0) return string.Empty;

        // FL Studio 12+ stores many text events in UTF-16 LE
        if (length >= 2 && buffer[1] == 0 && (length == 2 || length >= 4 && buffer[3] == 0))
        {
            return CleanString(Encoding.Unicode.GetString(buffer, 0, length));
        }

        return CleanString(Encoding.UTF8.GetString(buffer, 0, length));
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
