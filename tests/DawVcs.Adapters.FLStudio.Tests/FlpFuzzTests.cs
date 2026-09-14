using System.Buffers.Binary;
using System.Text;

using DawVcs.Adapters.FLStudio;

using FluentAssertions;

using Xunit;

namespace DawVcs.Adapters.FLStudio.Tests;

public sealed class FlpFuzzTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(7)]
    [InlineData(13)] // Less than 14-byte FLhd
    public async Task InspectAsync_WhenInputTooShort_ThrowsInvalidFlpException(int byteCount)
    {
        var buffer = new byte[byteCount];
        using var ms = new MemoryStream(buffer);

        var act = () => FlpBinaryReader.InspectAsync(ms);

        await act.Should().ThrowAsync<InvalidFlpException>();
    }

    [Fact]
    public async Task InspectAsync_WhenRandomBytes_TerminatesGracefully()
    {
        var rng = new Random(42);
        for (int i = 0; i < 25; i++)
        {
            var randomBytes = new byte[rng.Next(10, 5000)];
            rng.NextBytes(randomBytes);

            using var ms = new MemoryStream(randomBytes);
            try
            {
                var result = await FlpBinaryReader.InspectAsync(ms);
                result.Should().NotBeNull();
            }
            catch (InvalidFlpException)
            {
                // Expected and safe
            }
        }
    }

    [Fact]
    public async Task InspectAsync_WhenDeclaredChunkSizeExceedsStream_HandlesSuspiciousGracefully()
    {
        // Valid FLhd (14 bytes)
        using var ms = new MemoryStream();
        ms.Write([0x46, 0x4C, 0x68, 0x64, 0x06, 0x00, 0x00, 0x00, 0x00, 0x00, 0x0A, 0x00, 0x60, 0x00]);

        // FLdt chunk claiming 1 GB of data, but only 4 bytes provided
        ms.Write([0x46, 0x4C, 0x64, 0x74]);
        var lenBytes = new byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(lenBytes, 1024 * 1024 * 1024); // 1 GB
        ms.Write(lenBytes);
        ms.Write([0xC7, 0x02, 0x41, 0x42]); // small event

        ms.Position = 0;
        var result = await FlpBinaryReader.InspectAsync(ms);

        result.Should().NotBeNull();
        result.IsSuspicious.Should().BeTrue();
    }

    [Fact]
    public async Task InspectAsync_WhenLeb128ExceedsMaxShift_TerminatesWithoutOverflow()
    {
        using var ms = new MemoryStream();
        ms.Write([0x46, 0x4C, 0x68, 0x64, 0x06, 0x00, 0x00, 0x00, 0x00, 0x00, 0x0A, 0x00, 0x60, 0x00]);
        ms.Write([0x46, 0x4C, 0x64, 0x74]);
        ms.Write([0x10, 0x00, 0x00, 0x00]); // 16 bytes

        // Event >= 192 with malformed infinite high bits in LEB128
        ms.WriteByte(199);
        for (int i = 0; i < 10; i++)
        {
            ms.WriteByte(0xFF); // continuation bit set forever
        }

        ms.Position = 0;
        var result = await FlpBinaryReader.InspectAsync(ms);

        result.Should().NotBeNull();
        result.IsSuspicious.Should().BeTrue();
    }

    [Fact]
    public async Task DetectAsync_WithMalformedFlp_ReturnsInvalidOrUnsupportedWithoutUnhandledException()
    {
        var adapter = new FLStudioAdapter();
        var corruptData = new byte[] { 0xDE, 0xAD, 0xBE, 0xEF, 0x01, 0x02, 0x03, 0x04 };

        using var ms = new MemoryStream(corruptData);
        var result = await adapter.DetectAsync(ms);

        result.IsSupported.Should().BeFalse();
        result.Status.Should().BeOneOf(
            DawVcs.Adapters.Abstractions.ProjectDetectionStatus.Invalid,
            DawVcs.Adapters.Abstractions.ProjectDetectionStatus.Unsupported,
            DawVcs.Adapters.Abstractions.ProjectDetectionStatus.Suspicious);
    }
}
