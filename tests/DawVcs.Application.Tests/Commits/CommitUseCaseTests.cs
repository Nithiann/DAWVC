using System.Text.Json;

using DawVcs.Adapters.Abstractions;
using DawVcs.Application.Adapters;
using DawVcs.Application.Commits;
using DawVcs.Domain.Artifacts;
using DawVcs.Domain.Common;
using DawVcs.Domain.Configuration;
using DawVcs.Domain.Dependencies;
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
    private readonly string _externalDir;
    private readonly IRepositoryContext _context;
    private readonly IObjectStore _objectStore;
    private readonly RepositoryConfig _config;

    public CommitUseCaseTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), "dawvc_commit_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testDir);

        _externalDir = Path.Combine(Path.GetTempPath(), "dawvc_ext_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_externalDir);

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

        if (Directory.Exists(_externalDir))
        {
            try { Directory.Delete(_externalDir, recursive: true); } catch { }
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

    [Fact]
    public async Task Commit_WithExternalAudioSample_AutoBundlesIntoObjectStoreAndRegistersInSnapshot()
    {
        var songPath = Path.Combine(_testDir, "Song.flp");
        var songBytes = new byte[] { 10, 20, 30, 40 };
        await File.WriteAllBytesAsync(songPath, songBytes);

        var externalSamplePath = Path.Combine(_externalDir, "Kick.wav");
        var sampleBytes = new byte[] { 50, 60, 70, 80 };
        await File.WriteAllBytesAsync(externalSamplePath, sampleBytes);

        var mockAdapter = Substitute.For<IDawAdapter>();
        mockAdapter.DawName.Returns("FL Studio");
        mockAdapter.SupportedExtensions.Returns([".flp"]);

        var detectResult = ProjectDetectionResult.Valid("FL Studio", "2024.1", 1.0, metadata: new Dictionary<string, string>
        {
            ["SamplePaths"] = externalSamplePath
        });
        mockAdapter.DetectAsync(Arg.Any<ArtifactReadContext>(), Arg.Any<CancellationToken>())
            .Returns(detectResult);

        var adapterRegistry = Substitute.For<IDawAdapterRegistry>();
        adapterRegistry.FindAdapterForExtension(".flp").Returns(mockAdapter);

        _objectStore.WriteBlobAsync(Arg.Any<Stream>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                var stream = callInfo.Arg<Stream>();
                using var ms = new MemoryStream();
                stream.CopyTo(ms);
                return Blake3ContentHasher.Hash(ms.ToArray());
            });

        _context.GetBranchCommit(BranchName.Main).Returns((CommitId?)null);

        ReadOnlyMemory<byte> capturedSnapshotBytes = default;
        await _objectStore.WriteObjectAsync(
            ObjectType.Snapshot,
            Arg.Do<ReadOnlyMemory<byte>>(b => capturedSnapshotBytes = b),
            Arg.Any<CancellationToken>());

        var useCase = new CommitUseCase(_ => _context, adapterRegistry);
        var result = await useCase.ExecuteAsync(new CommitRequest(_testDir, "Commit with sample", Consolidate: false));

        result.Should().NotBeNull();
        result.IsComplete.Should().BeTrue();

        // ObjectStore should have received blob write for both Song.flp and Kick.wav
        await _objectStore.Received(2).WriteBlobAsync(Arg.Any<Stream>(), Arg.Any<CancellationToken>());

        // Inspect captured snapshot
        using var doc = JsonDocument.Parse(capturedSnapshotBytes);
        var entries = doc.RootElement.GetProperty("entries").EnumerateArray().ToList();
        entries.Should().Contain(e => e.GetProperty("path").GetString() == "Song.flp" && e.GetProperty("role").GetInt32() == (int)ArtifactRole.PrimaryProjectFile);
        entries.Should().Contain(e => e.GetProperty("path").GetString() == "Samples/Kick.wav" && e.GetProperty("role").GetInt32() == (int)ArtifactRole.ProjectAsset);

        var dependencies = doc.RootElement.GetProperty("dependencies").EnumerateArray().ToList();
        dependencies.Should().Contain(d =>
            d.GetProperty("name").GetString() == "Kick.wav" &&
            d.GetProperty("relativePath").GetString() == "Samples/Kick.wav" &&
            d.GetProperty("isMissing").GetBoolean() == false &&
            d.GetProperty("portability").GetInt32() == (int)PortabilityMode.Bundle);

        // Local workspace should not have Samples/Kick.wav when Consolidate is false
        File.Exists(Path.Combine(_testDir, "Samples", "Kick.wav")).Should().BeFalse();
    }

    [Fact]
    public async Task Commit_WithConsolidateOption_CopiesExternalSampleIntoWorkspaceSamplesDirectory()
    {
        var songPath = Path.Combine(_testDir, "Song.flp");
        var songBytes = new byte[] { 10, 20, 30, 40 };
        await File.WriteAllBytesAsync(songPath, songBytes);

        var externalSamplePath = Path.Combine(_externalDir, "Snare.wav");
        var sampleBytes = new byte[] { 99, 88, 77 };
        await File.WriteAllBytesAsync(externalSamplePath, sampleBytes);

        var mockAdapter = Substitute.For<IDawAdapter>();
        mockAdapter.DawName.Returns("FL Studio");
        mockAdapter.SupportedExtensions.Returns([".flp"]);

        var detectResult = ProjectDetectionResult.Valid("FL Studio", "2024.1", 1.0, metadata: new Dictionary<string, string>
        {
            ["SamplePaths"] = externalSamplePath
        });
        mockAdapter.DetectAsync(Arg.Any<ArtifactReadContext>(), Arg.Any<CancellationToken>())
            .Returns(detectResult);

        var adapterRegistry = Substitute.For<IDawAdapterRegistry>();
        adapterRegistry.FindAdapterForExtension(".flp").Returns(mockAdapter);

        _objectStore.WriteBlobAsync(Arg.Any<Stream>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                var stream = callInfo.Arg<Stream>();
                using var ms = new MemoryStream();
                stream.CopyTo(ms);
                return Blake3ContentHasher.Hash(ms.ToArray());
            });

        _context.GetBranchCommit(BranchName.Main).Returns((CommitId?)null);

        var useCase = new CommitUseCase(_ => _context, adapterRegistry);
        var result = await useCase.ExecuteAsync(new CommitRequest(_testDir, "Consolidate commit", Consolidate: true));

        result.Should().NotBeNull();
        result.IsComplete.Should().BeTrue();

        // Workspace should now contain Samples/Snare.wav with matching bytes
        var consolidatedPath = Path.Combine(_testDir, "Samples", "Snare.wav");
        File.Exists(consolidatedPath).Should().BeTrue();
        (await File.ReadAllBytesAsync(consolidatedPath)).Should().Equal(sampleBytes);
    }
}
