using System.Buffers.Binary;
using System.Text;

using DawVcs.Adapters.Abstractions;
using DawVcs.Domain.Hashing;

using FluentAssertions;

using Xunit;

namespace DawVcs.Adapters.FLStudio.Tests;

public sealed class FLStudioAdapterFixtureTests
{
    private static string GetFixturePath()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "DawVcs.slnx")))
        {
            dir = dir.Parent;
        }

        var root = dir?.FullName ?? throw new InvalidOperationException("Could not find solution root.");
        var fixturePath = Path.Combine(root, "fixtures", "flstudio", "Nithiann & Mr. Unit - ID", "Nithiann & Mr. Unit - ID.flp");

        if (!File.Exists(fixturePath))
        {
            throw new FileNotFoundException($"Fixture not found at expected path: {fixturePath}");
        }

        return fixturePath;
    }

    [Fact]
    [Trait("Requirement", "FR-FLP-001")]
    [Trait("Requirement", "FR-FLP-003")]
    public async Task DetectAsync_RealFixture_ExtractsVersionAndHeaderMetadata()
    {
        // Arrange
        var fixturePath = GetFixturePath();
        var adapter = new FLStudioAdapter();

        await using var stream = new FileStream(fixturePath, FileMode.Open, FileAccess.Read, FileShare.Read);

        // Act
        var result = await adapter.DetectAsync(stream);

        // Assert
        result.Status.Should().Be(ProjectDetectionStatus.Valid);
        result.DawName.Should().Be("FL Studio");
        result.DetectedVersion.Should().Be("25.2.5.5319");
        result.Confidence.Should().Be(1.0);

        result.Metadata.Should().ContainKey("ChannelCount").WhoseValue.Should().Be("37");
        result.Metadata.Should().ContainKey("Ppq").WhoseValue.Should().Be("96");
        result.Findings.Should().NotBeEmpty();
    }

    [Fact]
    [Trait("Requirement", "FR-FLP-002")]
    public async Task DetectAsync_ReadOnlyProof_DoesNotMutateSourceBytesOrHash()
    {
        // Arrange
        var fixturePath = GetFixturePath();
        var adapter = new FLStudioAdapter();

        // 1. Calculate BLAKE3 hash before inspection
        ContentHash hashBefore;
        await using (var streamBefore = new FileStream(fixturePath, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            hashBefore = await Blake3ContentHasher.HashAsync(streamBefore);
        }

        // 2. Perform inspection
        await using (var inspectStream = new FileStream(fixturePath, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            var result = await adapter.DetectAsync(inspectStream);
            result.Status.Should().Be(ProjectDetectionStatus.Valid);
        }

        // 3. Calculate BLAKE3 hash after inspection
        ContentHash hashAfter;
        await using (var streamAfter = new FileStream(fixturePath, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            hashAfter = await Blake3ContentHasher.HashAsync(streamAfter);
        }

        // Assert: Hashes must be strictly identical
        hashAfter.Should().Be(hashBefore);
    }

    [Fact]
    [Trait("Requirement", "FR-FLP-006")]
    public async Task DetectAsync_TruncatedStream_ReturnsInvalidStatus()
    {
        // Arrange: Only 8 bytes (incomplete header)
        var truncated = new byte[] { 0x46, 0x4C, 0x68, 0x64, 0x06, 0x00, 0x00, 0x00 };
        using var stream = new MemoryStream(truncated);
        var adapter = new FLStudioAdapter();

        // Act
        var result = await adapter.DetectAsync(stream);

        // Assert
        result.Status.Should().Be(ProjectDetectionStatus.Invalid);
        result.Findings.Should().Contain(f => f.Contains("too short") || f.Contains("Invalid"));
    }

    [Fact]
    [Trait("Requirement", "FR-FLP-005")]
    public async Task DetectAsync_InvalidSignature_ReturnsInvalidStatus()
    {
        // Arrange: 14 bytes with wrong magic 'RIFF'
        var badMagic = new byte[14];
        Encoding.ASCII.GetBytes("RIFF").CopyTo(badMagic, 0);
        using var stream = new MemoryStream(badMagic);
        var adapter = new FLStudioAdapter();

        // Act
        var result = await adapter.DetectAsync(stream);

        // Assert
        result.Status.Should().Be(ProjectDetectionStatus.Invalid);
        result.Findings.Should().Contain(f => f.Contains("Invalid FLP magic signature"));
    }

    [Fact]
    [Trait("Requirement", "FR-FLP-004")]
    [Trait("Requirement", "FR-FLP-008")]
    public async Task DetectAsync_FutureVersion_ReturnsUnsupportedForOpaqueFallback()
    {
        // Arrange: Construct valid synthetic FLP with version 99.0.0
        using var ms = new MemoryStream();

        // 1. FLhd chunk (14 bytes)
        ms.Write("FLhd"u8);
        var lenBuf = new byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(lenBuf, 6);
        ms.Write(lenBuf);
        ms.Write([0x00, 0x00, 0x01, 0x00, 0x60, 0x00]); // format 0, 1 channel, 96 ppq

        // 2. FLdt chunk
        ms.Write("FLdt"u8);
        var versionBytes = Encoding.ASCII.GetBytes("99.0.0\0");
        // Event 199 (0xC7), length = versionBytes.Length
        var eventBytes = new byte[1 + 1 + versionBytes.Length];
        eventBytes[0] = 199;
        eventBytes[1] = (byte)versionBytes.Length;
        versionBytes.CopyTo(eventBytes, 2);

        BinaryPrimitives.WriteUInt32LittleEndian(lenBuf, (uint)eventBytes.Length);
        ms.Write(lenBuf);
        ms.Write(eventBytes);

        ms.Position = 0;
        var adapter = new FLStudioAdapter();

        // Act
        var result = await adapter.DetectAsync(ms);

        // Assert: Unrecognized future versions must safely degrade to Unsupported
        result.Status.Should().Be(ProjectDetectionStatus.Unsupported);
        result.DetectedVersion.Should().Be("99.0.0");
        result.Findings.Should().Contain(f => f.Contains("opaque"));
    }
}
