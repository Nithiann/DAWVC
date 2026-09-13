using System.Buffers.Binary;
using System.Text;

namespace DawVcs.Adapters.FLStudio.Tests.Fixtures;

/// <summary>
/// Genereert deterministische binaire FLP-streams voor de fixturematrix (IMP-0511).
/// </summary>
public static class FlpFixtureGenerator
{
    private static readonly byte[] FlhdMagic = [0x46, 0x4C, 0x68, 0x64]; // ASCII 'FLhd'
    private static readonly byte[] FldtMagic = [0x46, 0x4C, 0x64, 0x74]; // ASCII 'FLdt'

    public static byte[] CreateValidFlp(
        string version = "25.2.5.5319",
        ushort channelCount = 10,
        ushort ppq = 96,
        string? title = null,
        double? tempoBpm = null,
        IEnumerable<string>? samplePaths = null,
        IEnumerable<string>? pluginNames = null)
    {
        using var dataStream = new MemoryStream();

        // Event 199: Version string (ASCII)
        if (!string.IsNullOrEmpty(version))
        {
            WriteVariableEvent(dataStream, 199, Encoding.ASCII.GetBytes(version));
        }

        // Event 201: Title string (UTF-8 or UTF-16)
        if (!string.IsNullOrEmpty(title))
        {
            WriteVariableEvent(dataStream, 201, Encoding.UTF8.GetBytes(title));
        }

        // Event 156: Fine tempo (DWORD in micro-BPM: bpm * 1000)
        if (tempoBpm.HasValue)
        {
            dataStream.WriteByte(156);
            uint rawTempo = (uint)Math.Round(tempoBpm.Value * 1000.0);
            var tempoBytes = new byte[4];
            BinaryPrimitives.WriteUInt32LittleEndian(tempoBytes, rawTempo);
            dataStream.Write(tempoBytes);
        }

        // Event 203: Sample paths
        if (samplePaths != null)
        {
            foreach (var sample in samplePaths)
            {
                WriteVariableEvent(dataStream, 203, Encoding.UTF8.GetBytes(sample));
            }
        }

        // Event 214: Plugin names
        if (pluginNames != null)
        {
            foreach (var plugin in pluginNames)
            {
                WriteVariableEvent(dataStream, 214, Encoding.UTF8.GetBytes(plugin));
            }
        }

        var dataBytes = dataStream.ToArray();

        using var flpStream = new MemoryStream();

        // 1. Write FLhd Header (14 bytes)
        flpStream.Write(FlhdMagic);
        var headerLength = new byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(headerLength, 6);
        flpStream.Write(headerLength);

        var formatBytes = new byte[2];
        BinaryPrimitives.WriteUInt16LittleEndian(formatBytes, 0); // Format 0
        flpStream.Write(formatBytes);

        var channelBytes = new byte[2];
        BinaryPrimitives.WriteUInt16LittleEndian(channelBytes, channelCount);
        flpStream.Write(channelBytes);

        var ppqBytes = new byte[2];
        BinaryPrimitives.WriteUInt16LittleEndian(ppqBytes, ppq);
        flpStream.Write(ppqBytes);

        // 2. Write FLdt Data Chunk (8 bytes + dataBytes)
        flpStream.Write(FldtMagic);
        var dataLength = new byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(dataLength, (uint)dataBytes.Length);
        flpStream.Write(dataLength);

        flpStream.Write(dataBytes);

        return flpStream.ToArray();
    }

    public static byte[] CreateTruncatedFlp(int length)
    {
        var full = CreateValidFlp();
        int safeLength = Math.Clamp(length, 0, full.Length);
        var truncated = new byte[safeLength];
        Array.Copy(full, truncated, safeLength);
        return truncated;
    }

    public static byte[] CreateInvalidSignatureFlp()
    {
        var valid = CreateValidFlp();
        var corrupt = (byte[])valid.Clone();
        // Replace 'FLhd' with 'RIFF'
        corrupt[0] = 0x52; // 'R'
        corrupt[1] = 0x49; // 'I'
        corrupt[2] = 0x46; // 'F'
        corrupt[3] = 0x46; // 'F'
        return corrupt;
    }

    public static byte[] CreateSuspiciousFlp()
    {
        // Valid header, but data chunk claims 5000 bytes while stream truncates at 50 bytes
        using var stream = new MemoryStream();
        stream.Write(FlhdMagic);
        var hLen = new byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(hLen, 6);
        stream.Write(hLen);
        stream.Write(new byte[6]); // format, channels, ppq

        stream.Write(FldtMagic);
        var dLen = new byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(dLen, 5000); // Claims 5000 bytes
        stream.Write(dLen);

        // Write a short version event then cut off
        WriteVariableEvent(stream, 199, Encoding.ASCII.GetBytes("25.2.5.5319"));
        return stream.ToArray();
    }

    private static void WriteVariableEvent(Stream stream, byte eventId, byte[] payload)
    {
        stream.WriteByte(eventId);
        WriteLeb128(stream, payload.Length);
        stream.Write(payload);
    }

    private static void WriteLeb128(Stream stream, int value)
    {
        uint v = (uint)value;
        while (v >= 0x80)
        {
            stream.WriteByte((byte)((v & 0x7F) | 0x80));
            v >>= 7;
        }
        stream.WriteByte((byte)(v & 0x7F));
    }
}
