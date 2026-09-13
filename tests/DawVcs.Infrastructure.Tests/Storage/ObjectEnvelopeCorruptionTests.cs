using System.Text;

using DawVcs.Domain.Hashing;
using DawVcs.Domain.Storage;
using DawVcs.Infrastructure.Storage;

using FluentAssertions;

using Xunit;

namespace DawVcs.Infrastructure.Tests.Storage;

public class ObjectEnvelopeCorruptionTests
{
    [Fact]
    [Trait("Requirement", "FR-OBJ-006")]
    public async Task WriteAndRead_ValidPayload_RoundTripsSuccessfully()
    {
        // Arrange
        var originalPayload = Encoding.UTF8.GetBytes("Immutable project snapshot data for DAWVC.");
        using var sourcePayloadStream = new MemoryStream(originalPayload);
        using var envelopeStream = new MemoryStream();

        // Act: Write envelope
        var writtenHeader = await ObjectEnvelopeWriter.WriteEnvelopeStreamingAsync(
            envelopeStream,
            sourcePayloadStream,
            ObjectType.Blob);

        // Reset position to read
        envelopeStream.Position = 0;

        // Act: Read header & verify payload
        var readHeader = await ObjectEnvelopeReader.ReadHeaderAsync(envelopeStream);
        var readPayload = await ObjectEnvelopeReader.ReadPayloadToMemoryAndVerifyAsync(envelopeStream, readHeader);

        // Assert
        readHeader.Should().Be(writtenHeader);
        readHeader.PayloadLength.Should().Be((ulong)originalPayload.Length);
        readHeader.ObjectType.Should().Be(ObjectType.Blob);
        readHeader.CompressionMode.Should().Be(CompressionMode.None);
        readPayload.Should().Equal(originalPayload);
    }

    [Fact]
    [Trait("Requirement", "FR-OBJ-006")]
    public async Task ReadHeader_TruncatedHeader_ThrowsInvalidEnvelopeException()
    {
        // Arrange: Header is shorter than 56 bytes
        var truncatedBytes = new byte[30];
        Array.Copy(ObjectEnvelopeHeader.ExpectedMagic, truncatedBytes, 4);
        using var stream = new MemoryStream(truncatedBytes);

        // Act
        var act = async () => await ObjectEnvelopeReader.ReadHeaderAsync(stream);

        // Assert
        await act.Should().ThrowAsync<InvalidEnvelopeException>()
            .WithMessage("*truncated*");
    }

    [Fact]
    [Trait("Requirement", "FR-OBJ-006")]
    public async Task ReadHeader_InvalidMagicBytes_ThrowsInvalidMagicBytesException()
    {
        // Arrange: 56 bytes with bad magic
        var badHeader = new byte[ObjectEnvelopeHeader.HeaderSize];
        Encoding.ASCII.GetBytes("BAD!").CopyTo(badHeader, 0);
        using var stream = new MemoryStream(badHeader);

        // Act
        var act = async () => await ObjectEnvelopeReader.ReadHeaderAsync(stream);

        // Assert
        await act.Should().ThrowAsync<InvalidMagicBytesException>()
            .WithMessage("*Invalid magic bytes*");
    }

    [Fact]
    [Trait("Requirement", "FR-OBJ-006")]
    public async Task ReadHeader_UnsupportedVersion_ThrowsUnsupportedEnvelopeVersionException()
    {
        // Arrange: Create valid header with version 2
        var header = new ObjectEnvelopeHeader(
            ObjectType.Blob,
            100,
            ContentHash.Empty,
            envelopeVersion: 2);

        var headerBytes = new byte[ObjectEnvelopeHeader.HeaderSize];
        header.WriteTo(headerBytes);
        using var stream = new MemoryStream(headerBytes);

        // Act
        var act = async () => await ObjectEnvelopeReader.ReadHeaderAsync(stream);

        // Assert
        await act.Should().ThrowAsync<UnsupportedEnvelopeVersionException>()
            .WithMessage("*Unsupported envelope version: 2*");
    }

    [Fact]
    [Trait("Requirement", "FR-OBJ-007")]
    public async Task ReadHeader_UnsupportedCompressionMode_ThrowsUnsupportedCompressionModeException()
    {
        // Arrange: Byte 8 set to 1 (e.g. Deflate/Zstd)
        var header = new ObjectEnvelopeHeader(ObjectType.Blob, 100, ContentHash.Empty);
        var headerBytes = new byte[ObjectEnvelopeHeader.HeaderSize];
        header.WriteTo(headerBytes);
        headerBytes[8] = 1; // CompressionMode != None
        using var stream = new MemoryStream(headerBytes);

        // Act
        var act = async () => await ObjectEnvelopeReader.ReadHeaderAsync(stream);

        // Assert
        await act.Should().ThrowAsync<UnsupportedCompressionModeException>()
            .WithMessage("*Unsupported compression mode*");
    }

    [Fact]
    [Trait("Requirement", "FR-OBJ-006")]
    public async Task ReadPayload_PrematureEndOfStream_ThrowsPayloadLengthMismatchException()
    {
        // Arrange: Header declares 100 bytes payload, but stream only contains 10 bytes
        var header = new ObjectEnvelopeHeader(ObjectType.Blob, 100, ContentHash.Empty);
        var envelopeBytes = new byte[ObjectEnvelopeHeader.HeaderSize + 10];
        header.WriteTo(envelopeBytes);
        using var stream = new MemoryStream(envelopeBytes);

        var readHeader = await ObjectEnvelopeReader.ReadHeaderAsync(stream);

        // Act
        var act = async () => await ObjectEnvelopeReader.ReadPayloadToMemoryAndVerifyAsync(stream, readHeader);

        // Assert
        await act.Should().ThrowAsync<PayloadLengthMismatchException>()
            .WithMessage("*Payload ended prematurely*");
    }

    [Fact]
    [Trait("Requirement", "FR-OBJ-001")]
    [Trait("Requirement", "FR-OBJ-006")]
    public async Task ReadPayload_CorruptedPayloadBytes_ThrowsPayloadHashMismatchException()
    {
        // Arrange
        var original = Encoding.UTF8.GetBytes("Vital audio project configuration.");
        using var sourceStream = new MemoryStream(original);
        using var envelopeStream = new MemoryStream();

        await ObjectEnvelopeWriter.WriteEnvelopeStreamingAsync(envelopeStream, sourceStream, ObjectType.Commit);

        // Corrupt 1 byte in payload (payload starts at index 56)
        var buffer = envelopeStream.ToArray();
        buffer[ObjectEnvelopeHeader.HeaderSize + 2] ^= 0xFF; // Flip bits

        using var corruptedStream = new MemoryStream(buffer);
        var readHeader = await ObjectEnvelopeReader.ReadHeaderAsync(corruptedStream);

        // Act
        var act = async () => await ObjectEnvelopeReader.ReadPayloadToMemoryAndVerifyAsync(corruptedStream, readHeader);

        // Assert
        await act.Should().ThrowAsync<PayloadHashMismatchException>()
            .WithMessage("*Payload hash mismatch*");
    }
}
