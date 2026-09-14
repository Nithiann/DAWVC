using DawVcs.Application.Scanning;
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

namespace DawVcs.Application.Tests.Scanning;

public sealed class StatusUseCaseTests : IDisposable
{
    private readonly string _testDir;
    private readonly IRepositoryContext _context;
    private readonly IStagingIndex _stagingIndex;
    private readonly RepositoryConfig _config;

    public StatusUseCaseTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), "dawvc_status_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testDir);

        _stagingIndex = Substitute.For<IStagingIndex>();
        _context = Substitute.For<IRepositoryContext>();
        _context.RootPath.Returns(_testDir);
        _context.StagingIndex.Returns(_stagingIndex);

        _config = new RepositoryConfig(RepositoryId.New(), "StatusSong", new ArtifactPath("Main.flp"), BranchName.Main);
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
    public async Task Status_WithUntrackedAndModified_CategorizesCorrectly()
    {
        // 1. Setup tracked Main.flp on disk with different content than HEAD
        var flpFile = Path.Combine(_testDir, "Main.flp");
        await File.WriteAllBytesAsync(flpFile, [1, 2, 3, 4]); // modified

        var flpOldHash = Blake3ContentHasher.Hash([9, 9, 9]);
        var headEntry = ArtifactEntry.Create(new ArtifactPath("Main.flp"), flpOldHash, 3, ArtifactRole.PrimaryProjectFile);
        var headSnapshot = new ProjectSnapshot(new ProjectArtifact("FL Studio", new SingleFileArtifact(headEntry)), DateTimeOffset.UtcNow);
        var headCommit = new Commit(Array.Empty<CommitId>(), headSnapshot.Id, "Author", DateTimeOffset.UtcNow, "Init");

        _context.GetBranchCommit(BranchName.Main).Returns(headCommit.Id);
        _context.LoadCommitAsync(headCommit.Id, Arg.Any<CancellationToken>()).Returns(headCommit);
        _context.LoadSnapshotAsync(headSnapshot.Id, Arg.Any<CancellationToken>()).Returns(headSnapshot);

        // 2. Setup untracked sample on disk
        var sampleFile = Path.Combine(_testDir, "Sample.wav");
        await File.WriteAllBytesAsync(sampleFile, [5, 6, 7]);

        _stagingIndex.GetStagedEntriesAsync(Arg.Any<CancellationToken>()).Returns(Array.Empty<ArtifactEntry>());

        var useCase = new StatusUseCase(_ => _context);
        var result = await useCase.ExecuteAsync(new StatusRequest(_testDir));

        result.Status.Modified.Should().HaveCount(1);
        result.Status.Modified[0].Path.Value.Should().Be("Main.flp");

        result.Status.Untracked.Should().HaveCount(1);
        result.Status.Untracked[0].Path.Value.Should().Be("Sample.wav");

        result.Status.Staged.Should().BeEmpty();
    }

    [Fact]
    public async Task Status_RenameDetection_MatchesIdenticalHashOnRenamedPath()
    {
        // Sample content
        var sampleBytes = new byte[] { 100, 101, 102, 103 };
        var sampleHash = Blake3ContentHasher.Hash(sampleBytes);

        var flpBytes = new byte[] { 1, 2, 3 };
        var flpHash = Blake3ContentHasher.Hash(flpBytes);
        await File.WriteAllBytesAsync(Path.Combine(_testDir, "Main.flp"), flpBytes);

        // On disk: sample was renamed from OldSample.wav to NewSample.wav
        await File.WriteAllBytesAsync(Path.Combine(_testDir, "NewSample.wav"), sampleBytes);

        var flpEntry = ArtifactEntry.Create(new ArtifactPath("Main.flp"), flpHash, flpBytes.Length, ArtifactRole.PrimaryProjectFile);
        var oldSampleEntry = ArtifactEntry.Create(new ArtifactPath("OldSample.wav"), sampleHash, sampleBytes.Length, ArtifactRole.ProjectAsset);
        var headSnapshot = new ProjectSnapshot(new ProjectArtifact("FL Studio", new DirectoryArtifact(new[] { flpEntry, oldSampleEntry })), DateTimeOffset.UtcNow);
        var headCommit = new Commit(Array.Empty<CommitId>(), headSnapshot.Id, "Author", DateTimeOffset.UtcNow, "Init");

        _context.GetBranchCommit(BranchName.Main).Returns(headCommit.Id);
        _context.LoadCommitAsync(headCommit.Id, Arg.Any<CancellationToken>()).Returns(headCommit);
        _context.LoadSnapshotAsync(headSnapshot.Id, Arg.Any<CancellationToken>()).Returns(headSnapshot);
        _stagingIndex.GetStagedEntriesAsync(Arg.Any<CancellationToken>()).Returns(Array.Empty<ArtifactEntry>());

        var useCase = new StatusUseCase(_ => _context);
        var result = await useCase.ExecuteAsync(new StatusRequest(_testDir));

        result.Status.Renamed.Should().HaveCount(1);
        result.Status.Renamed[0].OldPath?.Value.Should().Be("OldSample.wav");
        result.Status.Renamed[0].Path.Value.Should().Be("NewSample.wav");
        result.Status.Untracked.Should().BeEmpty();
        result.Status.Missing.Should().BeEmpty();
    }
}
