using DawVcs.Application.Checkouts;
using DawVcs.Application.Remotes;
using DawVcs.Domain.Artifacts;
using DawVcs.Domain.Common;
using DawVcs.Domain.Configuration;
using DawVcs.Domain.Remotes;
using DawVcs.Domain.Repositories;

using FluentAssertions;

using NSubstitute;

using Xunit;

namespace DawVcs.Application.Tests.Remotes;

public sealed class CloneUseCaseTests : IDisposable
{
    private readonly string _tempDir;
    private readonly IRepositoryContext _context = Substitute.For<IRepositoryContext>();
    private readonly IRemoteTransportFactory _transportFactory = Substitute.For<IRemoteTransportFactory>();
    private readonly IRemoteTransport _transport = Substitute.For<IRemoteTransport>();
    private readonly FetchUseCase _fetchUseCase;
    private readonly CheckoutUseCase _checkoutUseCase;
    private readonly CloneUseCase _useCase;

    private readonly RepositoryId _repoId = RepositoryId.New();
    private readonly CommitId _headCommit = CommitId.Parse(new string('c', 64));

    public CloneUseCaseTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "dawvc_clone_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);

        _transportFactory.CreateTransport(Arg.Any<string>()).Returns(_transport);
        _transport.HandshakeAsync(Arg.Any<CancellationToken>())
            .Returns(new RemoteHandshake(1, _repoId, "v0.1.0"));

        var quarantineStore = Substitute.For<IQuarantineStore>();
        _fetchUseCase = Substitute.For<FetchUseCase>(
            (Func<string, IRepositoryContext>)(_ => _context),
            _transportFactory,
            (Func<string, IQuarantineStore>)(_ => quarantineStore),
            null);

        _fetchUseCase.ExecuteAsync(Arg.Any<FetchRequest>(), Arg.Any<CancellationToken>())
            .Returns(new FetchResult("origin", Array.Empty<UpdatedRemoteRef>(), 5, 1024, false));

        _checkoutUseCase = Substitute.For<CheckoutUseCase>(
            (Func<string, IRepositoryContext>)(_ => _context),
            null);

        _checkoutUseCase.ExecuteFastForwardAsync(Arg.Any<FastForwardTarget>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(new CheckoutResult(_headCommit, SnapshotId.Parse(new string('a', 64)), "C:\\repo", new List<string> { "Song.flp" }));

        _context.GetRemoteTrackingCommit("origin", BranchName.Main).Returns(_headCommit);

        _useCase = new CloneUseCase(
            _ => _context,
            _transportFactory,
            _fetchUseCase,
            _checkoutUseCase,
            doctorUseCase: null);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            try { Directory.Delete(_tempDir, recursive: true); } catch { }
        }
    }

    [Fact]
    public async Task ExecuteAsync_TargetNotEmptyWithoutForce_ThrowsInvalidOperationException()
    {
        var populatedDir = Path.Combine(_tempDir, "populated");
        Directory.CreateDirectory(populatedDir);
        File.WriteAllText(Path.Combine(populatedDir, "existing.txt"), "hello");

        var request = new CloneRequest(@"\\NAS\Music\Song.dawvc", populatedDir, Force: false);
        var act = () => _useCase.ExecuteAsync(request);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*already exists and is not an empty directory*");
    }

    [Fact]
    public async Task ExecuteAsync_RemoteHandshakeNewerSchema_ThrowsRemoteException()
    {
        _transport.HandshakeAsync(Arg.Any<CancellationToken>())
            .Returns(new RemoteHandshake(999, _repoId, "v999.0"));

        var targetDir = Path.Combine(_tempDir, "new_clone");
        var request = new CloneRequest(@"\\NAS\Music\Song.dawvc", targetDir);
        var act = () => _useCase.ExecuteAsync(request);

        await act.Should().ThrowAsync<RemoteException>()
            .WithMessage("*newer than supported version*");
    }

    [Fact]
    public async Task ExecuteAsync_ValidClone_InitializesAndMaterializes()
    {
        var targetDir = Path.Combine(_tempDir, "valid_clone");
        var request = new CloneRequest(@"\\NAS\Music\Song.dawvc", targetDir);

        var result = await _useCase.ExecuteAsync(request);

        result.TargetDirectory.Should().Be(targetDir);
        result.DefaultBranch.Should().Be(BranchName.Main);
        result.HeadCommit.Should().Be(_headCommit);

        _context.Received(1).SaveRemote(Arg.Is<RemoteConfig>(r => r.Name.Value == "origin" && r.Url == @"\\NAS\Music\Song.dawvc"));
        _context.Received(1).SetUpstream(BranchName.Main, "origin", BranchName.Main);
        await _fetchUseCase.Received(1).ExecuteAsync(Arg.Is<FetchRequest>(f => f.RemoteName == "origin"), Arg.Any<CancellationToken>());
        await _checkoutUseCase.Received(1).ExecuteFastForwardAsync(Arg.Is<FastForwardTarget>(t => t.NewCommitId == _headCommit), true, Arg.Any<CancellationToken>());
    }
}
