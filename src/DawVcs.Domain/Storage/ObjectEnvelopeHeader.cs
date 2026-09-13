using System.Buffers.Binary;

using DawVcs.Domain.Hashing;

namespace DawVcs.Domain.Storage;

public enum ObjectType : ushort
{
    Blob = 1,
    Tree = 2,
    Snapshot = 3,
    Commit = 4
}

public enum CompressionMode : byte
{
    None = 0
}

/// <summary>
/// Immutable 56-byte header representing the canonical object envelope (v1).
/// </summary>
public readonly record struct ObjectEnvelopeHeader
{
    public const int HeaderSize = 56;
    public const ushort CurrentVersion = 1;
    public static readonly byte[] ExpectedMagic = [0x44, 0x57, 0x56, 0x43]; // ASCII 'DWVC'

    public ushort EnvelopeVersion { get; init; }
    public ObjectType ObjectType { get; init; }
    public CompressionMode CompressionMode { get; init; }
    public byte Flags { get; init; }
    public ulong PayloadLength { get; init; }
    public ContentHash PayloadHash { get; init; }

    public ObjectEnvelopeHeader(
        ObjectType objectType,
        ulong payloadLength,
        ContentHash payloadHash,
        ushort envelopeVersion = CurrentVersion,
        CompressionMode compressionMode = CompressionMode.None,
        byte flags = 0)
    {
        EnvelopeVersion = envelopeVersion;
        ObjectType = objectType;
        CompressionMode = compressionMode;
        Flags = flags;
        PayloadLength = payloadLength;
        PayloadHash = payloadHash;
    }

    /// <summary>
    /// Serializes the header into the destination 56-byte buffer.
    /// </summary>
    public void WriteTo(Span<byte> destination)
    {
        if (destination.Length < HeaderSize)
        {
            throw new ArgumentException($"Destination buffer must be at least {HeaderSize} bytes.", nameof(destination));
        }

        // 0..3: Magic
        ExpectedMagic.CopyTo(destination[..4]);

        // 4..5: Version (uint16 little endian)
        BinaryPrimitives.WriteUInt16LittleEndian(destination.Slice(4, 2), EnvelopeVersion);

        // 6..7: ObjectType (uint16 little endian)
        BinaryPrimitives.WriteUInt16LittleEndian(destination.Slice(6, 2), (ushort)ObjectType);

        // 8: CompressionMode
        destination[8] = (byte)CompressionMode;

        // 9: Flags
        destination[9] = Flags;

        // 10..15: Reserved padding (6 bytes zeros)
        destination.Slice(10, 6).Clear();

        // 16..23: PayloadLength (uint64 little endian)
        BinaryPrimitives.WriteUInt64LittleEndian(destination.Slice(16, 8), PayloadLength);

        // 24..55: PayloadHash (32 bytes BLAKE3)
        PayloadHash.AsSpan().CopyTo(destination.Slice(24, 32));
    }

    /// <summary>
    /// Deserializes and validates a 56-byte header from the source span.
    /// </summary>
    public static ObjectEnvelopeHeader ReadFrom(ReadOnlySpan<byte> source)
    {
        if (source.Length < HeaderSize)
        {
            throw new InvalidEnvelopeException($"Header is truncated. Expected {HeaderSize} bytes but got {source.Length}.");
        }

        // Magic check
        if (!source[..4].SequenceEqual(ExpectedMagic))
        {
            throw new InvalidMagicBytesException($"Invalid magic bytes: 0x{Convert.ToHexString(source[..4])}. Expected 'DWVC'.");
        }

        var version = BinaryPrimitives.ReadUInt16LittleEndian(source.Slice(4, 2));
        if (version != CurrentVersion)
        {
            throw new UnsupportedEnvelopeVersionException($"Unsupported envelope version: {version}. Expected: {CurrentVersion}.");
        }

        var objectTypeRaw = BinaryPrimitives.ReadUInt16LittleEndian(source.Slice(6, 2));
        if (!Enum.IsDefined(typeof(ObjectType), objectTypeRaw))
        {
            throw new InvalidEnvelopeException($"Unknown object type: {objectTypeRaw}.");
        }
        var objectType = (ObjectType)objectTypeRaw;

        var compressionRaw = source[8];
        if (compressionRaw != (byte)CompressionMode.None)
        {
            throw new UnsupportedCompressionModeException($"Unsupported compression mode: {compressionRaw}. Only None (0) is supported in v0.1.");
        }
        var compressionMode = (CompressionMode)compressionRaw;

        var flags = source[9];
        var payloadLength = BinaryPrimitives.ReadUInt64LittleEndian(source.Slice(16, 8));
        var payloadHash = new ContentHash(source.Slice(24, 32));

        return new ObjectEnvelopeHeader(
            objectType: objectType,
            payloadLength: payloadLength,
            payloadHash: payloadHash,
            envelopeVersion: version,
            compressionMode: compressionMode,
            flags: flags);
    }
}
