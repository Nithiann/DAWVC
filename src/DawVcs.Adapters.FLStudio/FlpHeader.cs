using System.Buffers.Binary;

namespace DawVcs.Adapters.FLStudio;

/// <summary>
/// Parsed representation of the FL Studio project 'FLhd' chunk (14 bytes).
/// </summary>
public readonly record struct FlpHeader
{
    public const int ExpectedHeaderChunkSize = 14;
    public const uint ExpectedHeaderPayloadLength = 6;
    public static readonly byte[] ExpectedMagic = [0x46, 0x4C, 0x68, 0x64]; // ASCII 'FLhd'

    public ushort Format { get; init; }
    public ushort ChannelCount { get; init; }
    public ushort Ppq { get; init; }

    public FlpHeader(ushort format, ushort channelCount, ushort ppq)
    {
        Format = format;
        ChannelCount = channelCount;
        Ppq = ppq;
    }

    /// <summary>
    /// Attempts to parse the FLhd header from the first 14 bytes of an FLP stream.
    /// </summary>
    public static bool TryParse(ReadOnlySpan<byte> data, out FlpHeader header, out string? error)
    {
        header = default;
        error = null;

        if (data.Length < ExpectedHeaderChunkSize)
        {
            error = $"Stream is too short to contain a valid FLhd chunk. Need {ExpectedHeaderChunkSize} bytes, got {data.Length}.";
            return false;
        }

        if (!data[..4].SequenceEqual(ExpectedMagic))
        {
            error = $"Invalid FLP magic signature: 0x{Convert.ToHexString(data[..4])}. Expected 'FLhd'.";
            return false;
        }

        var headerPayloadLength = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(4, 4));
        if (headerPayloadLength != ExpectedHeaderPayloadLength)
        {
            error = $"Unexpected FLhd payload length: {headerPayloadLength}. Expected {ExpectedHeaderPayloadLength}.";
            return false;
        }

        var format = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(8, 2));
        var channels = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(10, 2));
        var ppq = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(12, 2));

        header = new FlpHeader(format, channels, ppq);
        return true;
    }
}
