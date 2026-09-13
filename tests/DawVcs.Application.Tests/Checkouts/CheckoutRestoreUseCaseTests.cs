using DawVcs.Application.Checkouts;
using DawVcs.Domain.Artifacts;
using DawVcs.Domain.Common;
using DawVcs.Domain.Entities;
using DawVcs.Domain.Hashing;
using DawVcs.Domain.Repositories;
using DawVcs.Domain.Storage;

using FluentAssertions;

using NSubstitute;

using Xunit;

namespace DawVcs.Application.Tests.Checkouts;

public sealed class CheckoutRestoreUseCaseTests : IDisposable
{
    private readonly string _testRestoreDir;
    private readonly IRepositoryContext _context;
    private readonly IObjectStore _objectStore;

    public CheckoutRestoreUseCaseTests()
    {
        _testRestoreDir = Path.Combine(Path.GetTempPath(), "dawvc_restore_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testRestoreDir);

        _objectStore = Substitute.For<IObjectStore>();
        _context = Substitute.For<IRepositoryContext>();
        _context.ObjectStore.Returns(_objectStore);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testRestoreDir))
        {
            try { Directory.Delete(_testRestoreDir, recursive: true); } catch { }
        }
    }

    [Fact]
    public async Task CheckoutRestore_WhenReferenceUnknown_ThrowsInvalidOperationException()
    {
        _context.ResolveReference("unknown").Returns((CommitId?)null);

        var useCase = new CheckoutRestoreUseCase(_ => _context);
        var act = async () => await useCase.ExecuteAsync(new CheckoutRestoreRequest("dummyDir", "unknown", _testRestoreDir));

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*could not be resolved*");
    }

    [Fact]
    public async Task CheckoutRestore_WhenValid_MaterializesFilesByteExact()
    {
        var content = new byte[] { 42, 43, 44, 45, 46 };
        var hash = Blake3ContentHasher.Hash(content);
        var blobId = new BlobId(hash);

        var entry = ArtifactEntry.Create(new ArtifactPath("Track.flp"), hash, content.Length);
        var snapshot = new ProjectSnapshot(new ProjectArtifact("FL Studio", new SingleFileArtifact(entry)), DateTimeOffset.UtcNow);
        var commit = new Commit(Array.Empty<CommitId>(), snapshot.Id, "Producer", DateTimeOffset.UtcNow, "Init");

        _context.ResolveReference("main").Returns(commit.Id);
        _context.LoadCommitAsync(commit.Id, Arg.Any<CancellationToken>()).Returns(commit);
        _context.LoadSnapshotAsync(snapshot.Id, Arg.Any<CancellationToken>()).Returns(snapshot);

        _objectStore.OpenPayloadStreamAsync(blobId.Value, Arg.Any<CancellationToken>())
            .Returns(_ => new MemoryStream(content));

        var useCase = new CheckoutRestoreUseCase(_ => _context);
        var result = await useCase.ExecuteAsync(new CheckoutRestoreRequest("dummyDir", "main", _testRestoreDir));

        result.Should().NotBeNull();
        result.CommitId.Should().Be(commit.Id);
        result.RestoredFiles.Should().Contain("Track.flp");

        var restoredFile = Path.Combine(_testRestoreDir, "Track.flp");
        File.Exists(restoredFile).Should().BeTrue();
        var restoredBytes = await File.ReadAllBytesAsync(restoredFile);
        restoredBytes.Should().Equal(content);
    }

    [Fact]
    public async Task CheckoutRestore_WhenCorruptedPayload_ThrowsAndCleansUp()
    {
        var expectedHash = new ContentHash(new byte[32]);
        var blobId = new BlobId(expectedHash);

        var entry = ArtifactEntry.Create(new ArtifactPath("Track.flp"), expectedHash, 4);
        var snapshot = new ProjectSnapshot(new ProjectArtifact("FL Studio", new SingleFileArtifact(entry)), DateTimeOffset.UtcNow);
        var commit = new Commit(Array.Empty<CommitId>(), snapshot.Id, "Producer", DateTimeOffset.UtcNow, "Init");

        _context.ResolveReference("main").Returns(commit.Id);
        _context.LoadCommitAsync(commit.Id, Arg.Any<CancellationToken>()).Returns(commit);
        _context.LoadSnapshotAsync(snapshot.Id, Arg.Any<CancellationToken>()).Returns(snapshot);

        // Corrupt stream returned (different bytes)
        _objectStore.OpenPayloadStreamAsync(blobId.Value, Arg.Any<CancellationToken>())
            .Returns(_ => new MemoryStream(new byte[] { 9, 9, 9, 9 }));

        var useCase = new CheckoutRestoreUseCase(_ => _context);
        var act = async () => await useCase.ExecuteAsync(new CheckoutRestoreRequest("dummyDir", "main", _testRestoreDir));

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Integrity verification failed*");

        // Staging and target file should not exist
        File.Exists(Path.Combine(_testRestoreDir, "Track.flp")).Should().BeFalse();
    }
}
