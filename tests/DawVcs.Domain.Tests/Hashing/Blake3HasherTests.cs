using System.Text;

using DawVcs.Domain.Hashing;
using DawVcs.Domain.Tests.Common;

using FluentAssertions;

using Xunit;

namespace DawVcs.Domain.Tests.Hashing;

public class Blake3HasherTests
{
    [Fact]
    [Requirement("FR-OBJ-001")]
    public void Hash_EmptyInput_MatchesOfficialBlake3Vector()
    {
        // Arrange
        const string expectedEmptyHash = "af1349b9f5f9a1a6a0404dea36dcc9499bcb25c9adc112b7cc9a93cae41f3262";

        // Act
        var hash = Blake3ContentHasher.Hash(ReadOnlySpan<byte>.Empty);

        // Assert
        hash.ToString().Should().Be(expectedEmptyHash);
    }

    [Fact]
    [Requirement("FR-OBJ-001")]
    public void Hash_Utf8String_ProducesDeterministicHash()
    {
        // Arrange
        var data = Encoding.UTF8.GetBytes("DAWVC - DAW Version Control");

        // Act
        var hash1 = Blake3ContentHasher.Hash(data);
        var hash2 = Blake3ContentHasher.Hash(data);

        // Assert
        hash1.Should().Be(hash2);
        hash1.ToString().Should().HaveLength(64);
    }

    [Fact]
    [Requirement("FR-OBJ-003")]
    public async Task HashAsync_StreamingChunks_MatchesSingleBufferHash()
    {
        // Arrange: Generate 256 KB of pseudo-random data
        var random = new Random(42);
        var payload = new byte[256 * 1024];
        random.NextBytes(payload);

        var expectedHash = Blake3ContentHasher.Hash(payload);

        // Act: Stream through MemoryStream
        using var stream = new MemoryStream(payload);
        var streamedHash = await Blake3ContentHasher.HashAsync(stream);

        // Assert
        streamedHash.Should().Be(expectedHash);
    }

    [Fact]
    [Requirement("FR-OBJ-003")]
    public void Hasher_IncrementalUpdatesWithVaryingChunkSizes_MatchesBufferHash()
    {
        // Arrange
        var payload = Encoding.UTF8.GetBytes("Digital Audio Workstations require reliable content-addressed version control.");
        var expectedHash = Blake3ContentHasher.Hash(payload);

        using var hasher = new Blake3ContentHasher();

        // Act: Feed data in non-uniform small slices
        int offset = 0;
        int[] chunkSizes = [3, 7, 11, 1, 5, 20];
        int chunkIdx = 0;

        while (offset < payload.Length)
        {
            int size = Math.Min(chunkSizes[chunkIdx % chunkSizes.Length], payload.Length - offset);
            hasher.Update(payload.AsSpan(offset, size));
            offset += size;
            chunkIdx++;
        }

        var incrementalHash = hasher.FinalizeHash();

        // Assert
        incrementalHash.Should().Be(expectedHash);
    }

    [Fact]
    [Requirement("FR-OBJ-001")]
    public void Hasher_Reset_EnablesHasherReuse()
    {
        // Arrange
        using var hasher = new Blake3ContentHasher();
        var data1 = Encoding.UTF8.GetBytes("First payload");
        var data2 = Encoding.UTF8.GetBytes("Second payload");

        var expectedHash1 = Blake3ContentHasher.Hash(data1);
        var expectedHash2 = Blake3ContentHasher.Hash(data2);

        // Act
        hasher.Update(data1);
        var hash1 = hasher.FinalizeHash();

        hasher.Reset();
        hasher.Update(data2);
        var hash2 = hasher.FinalizeHash();

        // Assert
        hash1.Should().Be(expectedHash1);
        hash2.Should().Be(expectedHash2);
    }

    [Fact]
    [Requirement("FR-OBJ-001")]
    public void ContentHash_ParseAndToString_RoundTripsSuccessfully()
    {
        // Arrange
        const string hex = "af1349b9f5f9a1a6a0404dea36dcc9499bcb25c9adc112b7cc9a93cae41f3262";

        // Act
        var hash = ContentHash.Parse(hex);

        // Assert
        hash.ToString().Should().Be(hex);
        ContentHash.TryParse(hex, out var parsedHash).Should().BeTrue();
        parsedHash.Should().Be(hash);
    }

    [Theory]
    [InlineData("")]
    [InlineData("invalid-hex")]
    [InlineData("af1349b9f5f9a1a6")] // Too short
    [InlineData("zzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzz")] // Non-hex chars
    public void ContentHash_TryParse_RejectsInvalidInput(string invalidHex)
    {
        // Act
        var success = ContentHash.TryParse(invalidHex, out var hash);

        // Assert
        success.Should().BeFalse();
        hash.Should().Be(default);
    }
}
