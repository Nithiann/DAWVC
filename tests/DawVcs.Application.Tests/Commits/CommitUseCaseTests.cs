using DawVcs.Application.Commits;
using DawVcs.Domain.Artifacts;
using DawVcs.Domain.Common;
using DawVcs.Domain.Configuration;
using DawVcs.Domain.Entities;
using DawVcs.Domain.Hashing;
using DawVcs.Domain.Repositories;
using DawVcs.Domain.Storage;

using FluentAssertions;

using NSubstitute;

using Xunit;

namespace DawVcs.Application.Tests.Commits;

public sealed class CommitUseCaseTests : IDisposable
{
    private readonly string _testDir;
    private readonly IRepositoryContext _context;
    private readonly IObjectStore _objectStore;
    private readonly RepositoryConfig _config;

    public CommitUseCaseTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), "dawvc_commit_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testDir);

        _objectStore = Substitute.For<IObjectStore>();
        _context = Substitute.For<IRepositoryContext>();
        _context.RootPath.Returns(_testDir);
        _context.ObjectStore.Returns(_objectStore);

        _config = new RepositoryConfig(RepositoryId.New(), "TestSong", new ArtifactPath("Song.flp"), BranchName.Main);
        _context.LoadConfig().Returns(_config);
        _context.GetCurrentBranch().Returns(BranchName.Main);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDir))
        {
            try { Directory.Delete(_testDir, recursive: true); } catch { }
        }
    }

    [Fact]
    public async Task Commit_WhenPrimaryArtifactMissing_ThrowsFileNotFoundException()
    {
        var useCase = new CommitUseCase(_ => _context);
        var act = async () => await useCase.ExecuteAsync(new CommitRequest(_testDir, "Initial commit"));

        await act.Should().ThrowAsync<FileNotFoundException>();
    }

    [Fact]
    public async Task Commit_ValidFile_StoresBlobSnapshotCommitAndUpdatesBranch()
    {
        var songPath = Path.Combine(_testDir, "Song.flp");
        var fileBytes = new byte[] { 10, 20, 30, 40 };
        await File.WriteAllBytesAsync(songPath, fileBytes);

        var blobHash = Blake3ContentHasher.Hash(fileBytes);
        _objectStore.WriteBlobAsync(Arg.Any<Stream>(), Arg.Any<CancellationToken>())
            .Returns(blobHash);

        _context.GetBranchCommit(BranchName.Main).Returns((CommitId?)null);

        var useCase = new CommitUseCase(_ => _context);
        var result = await useCase.ExecuteAsync(new CommitRequest(_testDir, "Initial commit", "TestProducer"));

        result.Should().NotBeNull();
        result.Message.Should().Be("Initial commit");
        result.Author.Should().Be("TestProducer");
        result.Branch.Should().Be(BranchName.Main);

        await _objectStore.Received(1).WriteObjectAsync(ObjectType.Snapshot, Arg.Any<ReadOnlyMemory<byte>>(), Arg.Any<CancellationToken>());
        await _objectStore.Received(1).WriteObjectAsync(ObjectType.Commit, Arg.Any<ReadOnlyMemory<byte>>(), Arg.Any<CancellationToken>());
        await _context.Received(1).UpdateBranchCommitAsync(BranchName.Main, result.CommitId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Commit_WhenWorkingTreeIsClean_ThrowsInvalidOperationException()
    {
        var songPath = Path.Combine(_testDir, "Song.flp");
        var fileBytes = new byte[] { 1, 2, 3 };
        await File.WriteAllBytesAsync(songPath, fileBytes);

        var blobHash = Blake3ContentHasher.Hash(fileBytes);
        _objectStore.WriteBlobAsync(Arg.Any<Stream>(), Arg.Any<CancellationToken>())
            .Returns(blobHash);

        var projectArtifact = ProjectArtifact.CreateSingleFile("FL Studio", _config.PrimaryArtifact, blobHash, fileBytes.Length);
        var parentSnapshot = new ProjectSnapshot(projectArtifact, DateTimeOffset.UtcNow);

        var parentCommit = new Commit(
            Array.Empty<CommitId>(),
            parentSnapshot.Id,
            "Producer",
            DateTimeOffset.UtcNow.AddMinutes(-5),
            "First");

        _context.GetBranchCommit(BranchName.Main).Returns(parentCommit.Id);
        _context.LoadCommitAsync(parentCommit.Id, Arg.Any<CancellationToken>()).Returns(parentCommit);
        _context.LoadSnapshotAsync(parentSnapshot.Id, Arg.Any<CancellationToken>()).Returns(parentSnapshot);

        var useCase = new CommitUseCase(_ => _context);
        var act = async () => await useCase.ExecuteAsync(new CommitRequest(_testDir, "Identical commit"));

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*working tree clean*");
    }
}
