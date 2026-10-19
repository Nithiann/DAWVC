using System.Buffers.Binary;
using System.Text;

using DawVcs.Adapters.Abstractions;
using DawVcs.Domain.Dependencies;

namespace DawVcs.Adapters.FLStudio;

/// <summary>
/// Begrensde binaire lezer voor FL Studio (.flp) projectbestanden (IMP-0505).
/// Leest headers, data chunks en events met strikte limieten en bounds-checks
/// zonder het gehele bestand in geheugen te laden (NFR-SEC-001, NFR-SEC-004).
/// </summary>
public static class FlpBinaryReader
{
    public const int MaxMetadataScanBytes = 100 * 1024 * 1024; // 100 MB scanlimiet voor headers en metadata
    public const int MaxEventPayloadBytes = 10 * 1024 * 1024; // 10 MB sanity check per event

    private static readonly byte[] FldtMagic = [0x46, 0x4C, 0x64, 0x74]; // ASCII 'FLdt'

    public sealed record FlpPluginInfo(
        string Name,
        string? Vendor,
        string? FilePath,
        string Location,
        PluginRole Role,
        PluginFormat Format);

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
        long BytesScanned,
        InspectionCompleteness Completeness,
        string? CompletenessReason,
        IReadOnlyList<FlpPluginInfo>? Plugins = null);

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
        var plugins = new List<FlpPluginInfo>();
        string? currentChannelName = null;
        string? currentMixerTrackName = null;
        bool inMixerSection = false;
        bool hasEncounteredChannelsOrMixer = false;
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
                    if (stream.Position + 1 > stream.Length)
                    {
                        isSuspicious = true;
                        suspiciousReason = "Premature end of stream while reading 1-byte event.";
                        break;
                    }
                    stream.Seek(1, SeekOrigin.Current);
                }
                else
                {
                    int r = await stream.ReadAsync(byteBuffer.AsMemory(0, 1), cancellationToken).ConfigureAwait(false);
                    if (r == 0)
                    {
                        isSuspicious = true;
                        suspiciousReason = "Premature end of stream while reading 1-byte event.";
                        break;
                    }
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
                    else
                    {
                        isSuspicious = true;
                        suspiciousReason = "Premature end of stream while reading 2-byte tempo event.";
                        break;
                    }
                }
                else
                {
                    if (stream.CanSeek)
                    {
                        if (stream.Position + 2 > stream.Length)
                        {
                            isSuspicious = true;
                            suspiciousReason = "Premature end of stream while reading 2-byte event.";
                            break;
                        }
                        stream.Seek(2, SeekOrigin.Current);
                    }
                    else
                    {
                        int r = await ReadExactAsync(stream, byteBuffer.AsMemory(0, 2), cancellationToken).ConfigureAwait(false);
                        if (r < 2)
                        {
                            isSuspicious = true;
                            suspiciousReason = "Premature end of stream while reading 2-byte event.";
                            break;
                        }
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
                    else
                    {
                        isSuspicious = true;
                        suspiciousReason = "Premature end of stream while reading 4-byte fine tempo event.";
                        break;
                    }
                }
                else
                {
                    if (stream.CanSeek)
                    {
                        if (stream.Position + 4 > stream.Length)
                        {
                            isSuspicious = true;
                            suspiciousReason = "Premature end of stream while reading 4-byte event.";
                            break;
                        }
                        stream.Seek(4, SeekOrigin.Current);
                    }
                    else
                    {
                        int r = await ReadExactAsync(stream, byteBuffer.AsMemory(0, 4), cancellationToken).ConfigureAwait(false);
                        if (r < 4)
                        {
                            isSuspicious = true;
                            suspiciousReason = "Premature end of stream while reading 4-byte event.";
                            break;
                        }
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

                if (eventId == 196) // Sample file path (FLP_Text_SampleFileName)
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
                else if (eventId == 199 && detectedVersion is null) // FL Studio Version string
                {
                    var textBuffer = new byte[payloadLength];
                    int totalRead = await ReadExactAsync(stream, textBuffer, cancellationToken).ConfigureAwait(false);
                    bytesScanned += totalRead;

                    detectedVersion = CleanString(Encoding.ASCII.GetString(textBuffer, 0, totalRead));
                }
                else if (eventId == 200 && detectedComments is null) // Comments / info
                {
                    var textBuffer = new byte[payloadLength];
                    int totalRead = await ReadExactAsync(stream, textBuffer, cancellationToken).ConfigureAwait(false);
                    bytesScanned += totalRead;

                    detectedComments = DecodeFlpString(textBuffer, totalRead);
                }
                else if (eventId == 201) // Project Title OR Plugin/Generator name
                {
                    var textBuffer = new byte[payloadLength];
                    int totalRead = await ReadExactAsync(stream, textBuffer, cancellationToken).ConfigureAwait(false);
                    bytesScanned += totalRead;

                    var text = DecodeFlpString(textBuffer, totalRead);
                    if (!hasEncounteredChannelsOrMixer && detectedTitle is null && !string.IsNullOrWhiteSpace(text))
                    {
                        detectedTitle = text;
                    }
                    else if (!string.IsNullOrWhiteSpace(text) && !text.Equals("Fruity Wrapper", StringComparison.OrdinalIgnoreCase))
                    {
                        if (inMixerSection)
                        {
                            var loc = $"Mixer: {currentMixerTrackName ?? "Insert"}";
                            plugins.Add(new FlpPluginInfo(text, "Image-Line", null, loc, PluginRole.Effect, PluginFormat.Native));
                            if (!pluginNames.Contains(text))
                            {
                                pluginNames.Add(text);
                            }
                        }
                        else if (hasEncounteredChannelsOrMixer)
                        {
                            var loc = $"Channel: {currentChannelName ?? "Generator"}";
                            plugins.Add(new FlpPluginInfo(text, "Image-Line", null, loc, PluginRole.Instrument, PluginFormat.Native));
                            if (!pluginNames.Contains(text))
                            {
                                pluginNames.Add(text);
                            }
                        }
                    }
                }
                else if (eventId == 203) // Channel Rack Channel Name (FLP_Text_ChanName) or legacy sample path
                {
                    var textBuffer = new byte[payloadLength];
                    int totalRead = await ReadExactAsync(stream, textBuffer, cancellationToken).ConfigureAwait(false);
                    bytesScanned += totalRead;

                    var name = DecodeFlpString(textBuffer, totalRead);
                    hasEncounteredChannelsOrMixer = true;
                    if (!string.IsNullOrWhiteSpace(name))
                    {
                        currentChannelName = name;

                        if (HasAudioExtension(name) && !samplePaths.Contains(name))
                        {
                            samplePaths.Add(name);
                        }
                    }
                }
                else if (eventId == 204) // Mixer Track Insert Name (FLP_Text_EffectChanName)
                {
                    var textBuffer = new byte[payloadLength];
                    int totalRead = await ReadExactAsync(stream, textBuffer, cancellationToken).ConfigureAwait(false);
                    bytesScanned += totalRead;

                    var name = DecodeFlpString(textBuffer, totalRead);
                    hasEncounteredChannelsOrMixer = true;
                    inMixerSection = true;
                    if (!string.IsNullOrWhiteSpace(name))
                    {
                        currentMixerTrackName = name;
                    }
                }
                else if (eventId == 213) // VST / Plugin params wrapper chunk (FLP_Text_PluginParams)
                {
                    var payload = new byte[payloadLength];
                    int totalRead = await ReadExactAsync(stream, payload, cancellationToken).ConfigureAwait(false);
                    bytesScanned += totalRead;

                    if (totalRead >= 4)
                    {
                        int offset = 4;
                        string? pName = null;
                        string? pPath = null;
                        string? pVendor = null;

                        while (offset + 12 <= totalRead)
                        {
                            int subId = BinaryPrimitives.ReadInt32LittleEndian(payload.AsSpan(offset, 4));
                            long subLen = BinaryPrimitives.ReadInt64LittleEndian(payload.AsSpan(offset + 4, 8));
                            offset += 12;

                            if (subLen < 0 || offset + subLen > totalRead) break;

                            var chunkBytes = payload.AsSpan(offset, (int)subLen);
                            offset += (int)subLen;

                            if (subId == 54) pName = CleanString(Encoding.UTF8.GetString(chunkBytes));
                            else if (subId == 55) pPath = CleanString(Encoding.UTF8.GetString(chunkBytes));
                            else if (subId == 56) pVendor = CleanString(Encoding.UTF8.GetString(chunkBytes));
                        }

                        if (!string.IsNullOrWhiteSpace(pName) || !string.IsNullOrWhiteSpace(pPath))
                        {
                            var finalName = !string.IsNullOrWhiteSpace(pName)
                                ? pName
                                : Path.GetFileNameWithoutExtension(pPath)!;

                            var format = (pPath != null && pPath.EndsWith(".vst3", StringComparison.OrdinalIgnoreCase))
                                ? PluginFormat.VST3
                                : (pPath != null && pPath.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) ? PluginFormat.VST2 : PluginFormat.VST3);

                            var role = inMixerSection ? PluginRole.Effect : PluginRole.Instrument;
                            var location = inMixerSection
                                ? $"Mixer: {currentMixerTrackName ?? "Insert"}"
                                : $"Channel: {currentChannelName ?? "Generator"}";

                            plugins.Add(new FlpPluginInfo(finalName, pVendor, pPath, location, role, format));

                            if (!pluginNames.Contains(finalName))
                            {
                                pluginNames.Add(finalName);
                            }
                        }
                    }
                }
                else if (eventId == 214) // Plugin name / generator/effect reference
                {
                    var textBuffer = new byte[payloadLength];
                    int totalRead = await ReadExactAsync(stream, textBuffer, cancellationToken).ConfigureAwait(false);
                    bytesScanned += totalRead;

                    var name = DecodeFlpString(textBuffer, totalRead);
                    if (!string.IsNullOrWhiteSpace(name))
                    {
                        if (!pluginNames.Contains(name))
                        {
                            pluginNames.Add(name);
                        }

                        var role = inMixerSection ? PluginRole.Effect : PluginRole.Instrument;
                        var location = inMixerSection
                            ? $"Mixer: {currentMixerTrackName ?? "Insert"}"
                            : $"Channel: {currentChannelName ?? "Generator"}";
                        plugins.Add(new FlpPluginInfo(name, "Unknown", null, location, role, PluginFormat.Unknown));
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

        InspectionCompleteness completeness;
        string? completenessReason = null;

        if (isSuspicious)
        {
            completeness = InspectionCompleteness.Partial;
            completenessReason = suspiciousReason;
        }
        else if (bytesScanned < dataChunkLength)
        {
            completeness = InspectionCompleteness.Partial;
            completenessReason = $"Scan limit reached at {bytesScanned:N0} bytes (declared chunk length is {dataChunkLength:N0} bytes). Events beyond scan limit were not inspected.";
        }
        else
        {
            completeness = InspectionCompleteness.Complete;
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
            bytesScanned,
            completeness,
            completenessReason,
            plugins);
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

    public static string DecodeFlpString(byte[] buffer, int length)
    {
        if (length <= 0) return string.Empty;

        // FL Studio 12+ stores many text events in UTF-16 LE
        if (length >= 2 && buffer[1] == 0 && (length == 2 || length >= 4 && buffer[3] == 0))
        {
            return CleanString(Encoding.Unicode.GetString(buffer, 0, length));
        }

        return CleanString(Encoding.UTF8.GetString(buffer, 0, length));
    }

    private static bool HasAudioExtension(string s)
    {
        var ext = Path.GetExtension(s);
        return ext.Equals(".wav", StringComparison.OrdinalIgnoreCase) ||
               ext.Equals(".mp3", StringComparison.OrdinalIgnoreCase) ||
               ext.Equals(".flac", StringComparison.OrdinalIgnoreCase) ||
               ext.Equals(".ogg", StringComparison.OrdinalIgnoreCase) ||
               ext.Equals(".aiff", StringComparison.OrdinalIgnoreCase) ||
               ext.Equals(".aif", StringComparison.OrdinalIgnoreCase);
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
