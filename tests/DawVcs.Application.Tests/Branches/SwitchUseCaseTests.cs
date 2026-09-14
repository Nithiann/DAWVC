using DawVcs.Application.Branches;
using DawVcs.Application.Exceptions;
using DawVcs.Domain.Artifacts;
using DawVcs.Domain.Common;
using DawVcs.Domain.Entities;
using DawVcs.Domain.Hashing;
using DawVcs.Domain.Repositories;
using DawVcs.Domain.Storage;

using FluentAssertions;

using NSubstitute;

using Xunit;

namespace DawVcs.Application.Tests.Branches;

public sealed class SwitchUseCaseTests : IDisposable
{
    private readonly string _tempDir;
    private readonly IRepositoryContext _context;
    private readonly IObjectStore _objectStore;
    private readonly SwitchUseCase _useCase;

    public SwitchUseCaseTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "dawvc_switch_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);

        _objectStore = Substitute.For<IObjectStore>();
        _context = Substitute.For<IRepositoryContext>();
        _context.RootPath.Returns(_tempDir);
        _context.ObjectStore.Returns(_objectStore);

        _useCase = new SwitchUseCase(_ => _context);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, recursive: true);
            }
        }
        catch
        {
            // Best effort
        }

        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task Switch_WhenAlreadyOnTargetBranch_ReturnsAlreadyOnBranchWithoutModifyingHead()
    {
        var main = BranchName.Main;
        _context.GetCurrentBranch().Returns(main);
        _context.GetBranches().Returns([new BranchInfo(main, null, true)]);

        var result = await _useCase.ExecuteAsync(new SwitchRequest(_tempDir, "main"));

        result.AlreadyOnBranch.Should().BeTrue();
        result.Branch.Value.Should().Be("main");
        _context.DidNotReceive().SetCurrentBranch(Arg.Any<BranchName>());
    }

    [Fact]
    public async Task Switch_WhenBranchDoesNotExistAndCreateFalse_ThrowsInvalidOperationException()
    {
        _context.GetCurrentBranch().Returns(BranchName.Main);
        _context.GetBranches().Returns([new BranchInfo(BranchName.Main, null, true)]);

        var act = () => _useCase.ExecuteAsync(new SwitchRequest(_tempDir, "feature-x", CreateBranch: false));

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*does not exist*");
    }

    [Fact]
    public async Task Switch_WhenBranchAlreadyExistsAndCreateTrue_ThrowsInvalidOperationException()
    {
        var main = BranchName.Main;
        var feature = new BranchName("feature");
        _context.GetCurrentBranch().Returns(main);
        _context.GetBranches().Returns([
            new BranchInfo(main, null, true),
            new BranchInfo(feature, null, false)
        ]);

        var act = () => _useCase.ExecuteAsync(new SwitchRequest(_tempDir, "feature", CreateBranch: true));

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*already exists*");
    }

    [Fact]
    public async Task Switch_WhenCreateTrue_CreatesBranchAndSwitchesHead()
    {
        var main = BranchName.Main;
        var commitId = new CommitId(Blake3ContentHasher.Hash(new byte[] { 1 }));

        _context.GetCurrentBranch().Returns(main);
        _context.GetBranchCommit(main).Returns(commitId);
        _context.GetBranches().Returns([new BranchInfo(main, commitId, true)]);

        // Setup commit and snapshot for checkout
        var snapshotId = new SnapshotId(Blake3ContentHasher.Hash(new byte[] { 2 }));
        var commit = new Commit([], snapshotId, "Author", DateTimeOffset.UtcNow, "Initial", commitId);
        var project = ProjectArtifact.CreateSingleFile("FL Studio", new ArtifactPath("Track.flp"), Blake3ContentHasher.Hash(new byte[] { 3 }), 10);
        var snapshot = new ProjectSnapshot(project, DateTimeOffset.UtcNow, id: snapshotId);

        _context.LoadCommitAsync(commitId, Arg.Any<CancellationToken>()).Returns(commit);
        _context.LoadSnapshotAsync(snapshotId, Arg.Any<CancellationToken>()).Returns(snapshot);

        var result = await _useCase.ExecuteAsync(new SwitchRequest(_tempDir, "experiment", CreateBranch: true));

        result.CreatedNewBranch.Should().BeTrue();
        result.Branch.Value.Should().Be("experiment");
        _context.Received(1).CreateBranch(Arg.Is<BranchName>(b => b.Value == "experiment"), commitId);
        _context.Received(1).SetCurrentBranch(Arg.Is<BranchName>(b => b.Value == "experiment"));
    }
}
