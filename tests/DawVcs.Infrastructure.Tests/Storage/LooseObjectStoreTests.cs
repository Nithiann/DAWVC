using System.Text;

using DawVcs.Domain.Hashing;
using DawVcs.Domain.Storage;
using DawVcs.Infrastructure.Storage;

using FluentAssertions;

using Xunit;

namespace DawVcs.Infrastructure.Tests.Storage;

public sealed class LooseObjectStoreTests : IDisposable
{
    private readonly string _testRepoRoot;
    private readonly LooseObjectStore _store;

    public LooseObjectStoreTests()
    {
        _testRepoRoot = Path.Combine(Path.GetTempPath(), "dawvc_objstore_tests_" + Guid.NewGuid().ToString("N"));
        _store = new LooseObjectStore(_testRepoRoot);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_testRepoRoot))
            {
                Directory.Delete(_testRepoRoot, recursive: true);
            }
        }
        catch
        {
            // Best effort cleanup
        }

        GC.SuppressFinalize(this);
    }

    [Fact]
    [Trait("Requirement", "FR-OBJ-001")]
    [Trait("Requirement", "FR-OBJ-003")]
    public async Task WriteBlobAsync_StoresBlobWithEnvelopedHeaderAndPartitionedPath()
    {
        // Arrange: 100 KB payload
        var payload = new byte[100 * 1024];
        new Random(42).NextBytes(payload);
        var expectedHash = Blake3ContentHasher.Hash(payload);

        // Act
        using var stream = new MemoryStream(payload);
        var returnedHash = await _store.WriteBlobAsync(stream);

        // Assert
        returnedHash.Should().Be(expectedHash);
        _store.Exists(expectedHash).Should().BeTrue();

        // Verify prefix partitioning: objects/xx/yyy...
        var expectedPath = _store.GetObjectPath(expectedHash);
        File.Exists(expectedPath).Should().BeTrue();

        var hex = expectedHash.ToString();
        var expectedPrefix = hex[..2];
        expectedPath.Should().Contain(Path.Combine(".dawvc", "objects", expectedPrefix));

        // Verify header
        var header = await _store.ReadHeaderAsync(expectedHash);
        header.ObjectType.Should().Be(ObjectType.Blob);
        header.PayloadLength.Should().Be((ulong)payload.Length);
        header.PayloadHash.Should().Be(expectedHash);

        // Verify payload read back
        var readPayload = await _store.ReadObjectPayloadAsync(expectedHash);
        readPayload.Should().Equal(payload);
    }

    [Fact]
    [Trait("Requirement", "FR-OBJ-002")]
    public async Task WriteBlobAsync_DuplicateContent_DeduplicatesAndReusesExistingFile()
    {
        // Arrange
        var payload = Encoding.UTF8.GetBytes("Identical project sample data for deduplication.");
        using var stream1 = new MemoryStream(payload);
        using var stream2 = new MemoryStream(payload);

        // Act
        var hash1 = await _store.WriteBlobAsync(stream1);
        var hash2 = await _store.WriteBlobAsync(stream2);

        // Assert
        hash1.Should().Be(hash2);

        // Only one file should exist in the partitioned directory
        var path = _store.GetObjectPath(hash1);
        var partitionDir = Path.GetDirectoryName(path)!;
        Directory.GetFiles(partitionDir).Should().ContainSingle();
    }

    [Fact]
    [Trait("Requirement", "FR-OBJ-006")]
    public async Task WriteObjectAsync_WritesMetadataObject_WithCorrectObjectType()
    {
        // Arrange
        var commitJson = Encoding.UTF8.GetBytes("{\"message\":\"Initial commit\",\"author\":\"Producer\"}");

        // Act
        var commitHash = await _store.WriteObjectAsync(ObjectType.Commit, commitJson);

        // Assert
        _store.Exists(commitHash).Should().BeTrue();

        var header = await _store.ReadHeaderAsync(commitHash);
        header.ObjectType.Should().Be(ObjectType.Commit);
        header.PayloadLength.Should().Be((ulong)commitJson.Length);
        header.PayloadHash.Should().Be(commitHash);

        var readBytes = await _store.ReadObjectPayloadAsync(commitHash);
        readBytes.Should().Equal(commitJson);
    }

    [Fact]
    [Trait("Requirement", "FR-OBJ-003")]
    public async Task OpenPayloadStreamAsync_StreamsPayloadAccurately()
    {
        // Arrange
        var payload = Encoding.UTF8.GetBytes("Streaming audio asset payload slice.");
        using var sourceStream = new MemoryStream(payload);
        var hash = await _store.WriteBlobAsync(sourceStream);

        // Act
        await using var payloadStream = await _store.OpenPayloadStreamAsync(hash);
        using var destination = new MemoryStream();
        await payloadStream.CopyToAsync(destination);

        // Assert
        destination.ToArray().Should().Equal(payload);
        payloadStream.Length.Should().Be(payload.Length);
    }

    [Fact]
    [Trait("Requirement", "FR-OBJ-010")]
    public async Task WriteBlobAsync_FaultInjection_CrashBeforePublish_LeavesNoCorruptObject()
    {
        // Arrange
        var storeWithFault = new LooseObjectStore(
            _testRepoRoot,
            onBeforeAtomicPublish: tempPath =>
            {
                throw new InvalidOperationException("Simulated process kill right before atomic rename!");
            });

        var payload = Encoding.UTF8.GetBytes("Data destined to be aborted.");
        using var stream = new MemoryStream(payload);

        // Act
        var act = async () => await storeWithFault.WriteBlobAsync(stream);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>();

        // Target object must not exist
        var expectedHash = Blake3ContentHasher.Hash(payload);
        _store.Exists(expectedHash).Should().BeFalse();

        // Temp directory must be clean
        var tempDir = Path.Combine(_testRepoRoot, ".dawvc", "objects", ".tmp");
        if (Directory.Exists(tempDir))
        {
            Directory.GetFiles(tempDir).Should().BeEmpty();
        }
    }
}
