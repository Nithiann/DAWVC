using DawVcs.Domain.Hashing;
using DawVcs.Domain.Storage;
using DawVcs.Infrastructure.Storage;

using FluentAssertions;

using Xunit;

namespace DawVcs.Infrastructure.Tests.Storage;

public sealed class ObjectEnvelopeFuzzTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(20)]
    [InlineData(55)] // HeaderSize is 56
    public async Task ReadHeaderAsync_WhenTruncatedHeader_ThrowsInvalidEnvelopeException(int byteCount)
    {
        var truncated = new byte[byteCount];
        using var ms = new MemoryStream(truncated);

        var act = () => ObjectEnvelopeReader.ReadHeaderAsync(ms);

        await act.Should().ThrowAsync<InvalidEnvelopeException>()
            .WithMessage("*truncated*");
    }

    [Fact]
    public async Task ReadHeaderAsync_WhenInvalidMagicBytes_ThrowsInvalidMagicBytesException()
    {
        var headerBytes = new byte[56];
        headerBytes[0] = 0xAA; // Corrupt magic
        headerBytes[1] = 0xBB;
        headerBytes[2] = 0xCC;
        headerBytes[3] = 0xDD;

        using var ms = new MemoryStream(headerBytes);
        var act = () => ObjectEnvelopeReader.ReadHeaderAsync(ms);

        await act.Should().ThrowAsync<InvalidMagicBytesException>();
    }

    [Fact]
    public async Task ReadHeaderAsync_WhenUnsupportedVersion_ThrowsUnsupportedEnvelopeVersionException()
    {
        var headerBytes = new byte[56];
        // Valid magic: 'DWVC'
        ObjectEnvelopeHeader.ExpectedMagic.CopyTo(headerBytes.AsSpan(0, 4));
        headerBytes[4] = 99; // Version 99
        headerBytes[5] = 0;

        using var ms = new MemoryStream(headerBytes);
        var act = () => ObjectEnvelopeReader.ReadHeaderAsync(ms);

        await act.Should().ThrowAsync<UnsupportedEnvelopeVersionException>();
    }

    [Fact]
    public async Task ReadAndVerifyPayloadAsync_WhenPayloadModified_ThrowsPayloadHashMismatchException()
    {
        var originalPayload = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 };
        using var envelopeStream = new MemoryStream();

        // Write a valid envelope
        var header = await ObjectEnvelopeWriter.WriteEnvelopeStreamingAsync(
            envelopeStream,
            new MemoryStream(originalPayload),
            ObjectType.Blob);

        // Corrupt 1 byte in payload
        var buffer = envelopeStream.ToArray();
        buffer[^1] ^= 0xFF;

        using var corruptStream = new MemoryStream(buffer);
        var readHeader = await ObjectEnvelopeReader.ReadHeaderAsync(corruptStream);

        using var dest = new MemoryStream();
        var act = () => ObjectEnvelopeReader.ReadAndVerifyPayloadAsync(corruptStream, readHeader, dest);

        await act.Should().ThrowAsync<PayloadHashMismatchException>();
    }

    [Fact]
    public async Task ReadAndVerifyPayloadAsync_WhenPayloadTruncated_ThrowsPayloadLengthMismatchException()
    {
        var originalPayload = new byte[100];
        using var envelopeStream = new MemoryStream();

        var header = await ObjectEnvelopeWriter.WriteEnvelopeStreamingAsync(
            envelopeStream,
            new MemoryStream(originalPayload),
            ObjectType.Blob);

        // Truncate stream by 10 bytes
        var buffer = envelopeStream.ToArray();
        var truncated = buffer[..^10];

        using var corruptStream = new MemoryStream(truncated);
        var readHeader = await ObjectEnvelopeReader.ReadHeaderAsync(corruptStream);

        using var dest = new MemoryStream();
        var act = () => ObjectEnvelopeReader.ReadAndVerifyPayloadAsync(corruptStream, readHeader, dest);

        await act.Should().ThrowAsync<PayloadLengthMismatchException>();
    }
}
