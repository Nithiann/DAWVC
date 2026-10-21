using DawVcs.Application.Remotes;
using DawVcs.Domain.Artifacts;
using DawVcs.Domain.Common;
using DawVcs.Domain.Configuration;
using DawVcs.Domain.Hashing;
using DawVcs.Domain.Remotes;
using DawVcs.Domain.Repositories;
using DawVcs.Domain.Storage;

using FluentAssertions;

using NSubstitute;

using Xunit;

namespace DawVcs.Application.Tests.Remotes;

public sealed class FetchUseCaseTests
{
    private readonly IRepositoryContext _context = Substitute.For<IRepositoryContext>();
    private readonly IRemoteTransportFactory _transportFactory = Substitute.For<IRemoteTransportFactory>();
    private readonly IRemoteTransport _transport = Substitute.For<IRemoteTransport>();
    private readonly IQuarantineStore _quarantineStore = Substitute.For<IQuarantineStore>();
    private readonly FetchUseCase _useCase;

    private readonly RepositoryId _repoId = RepositoryId.New();
    private readonly RepositoryConfig _localConfig;

    public FetchUseCaseTests()
    {
        _localConfig = new RepositoryConfig(_repoId, "TestSong", new ArtifactPath("Song.flp"), BranchName.Main);
        _context.LoadConfig().Returns(_localConfig);

        _transportFactory.CreateTransport(Arg.Any<string>()).Returns(_transport);

        _useCase = new FetchUseCase(
            _ => _context,
            _transportFactory,
            _ => _quarantineStore);
    }

    [Fact]
    public async Task ExecuteAsync_UnconfiguredRemote_ThrowsRemoteException()
    {
        _context.GetRemote("unknown").Returns((RemoteConfig?)null);

        var request = new FetchRequest("C:\\repo", "unknown");
        var act = () => _useCase.ExecuteAsync(request);

        await act.Should().ThrowAsync<RemoteException>()
            .WithMessage("*No remote named 'unknown'*");
    }

    [Fact]
    public async Task ExecuteAsync_RepositoryIdMismatch_ThrowsRemoteException()
    {
        var otherRepoId = RepositoryId.New();
        _context.GetRemote("origin").Returns(new RemoteConfig(RemoteName.Origin, "https://remote.url"));
        _transport.HandshakeAsync(Arg.Any<CancellationToken>())
            .Returns(new RemoteHandshake(1, otherRepoId, "v0.1.0"));

        var request = new FetchRequest("C:\\repo", "origin");
        var act = () => _useCase.ExecuteAsync(request);

        await act.Should().ThrowAsync<RemoteException>()
            .WithMessage("*does not match local repositoryId*");
    }

    [Fact]
    public async Task ExecuteAsync_UnsupportedRemoteSchemaVersion_ThrowsRemoteException()
    {
        _context.GetRemote("origin").Returns(new RemoteConfig(RemoteName.Origin, "https://remote.url"));
        _transport.HandshakeAsync(Arg.Any<CancellationToken>())
            .Returns(new RemoteHandshake(999, _repoId, "v999.0.0"));

        var request = new FetchRequest("C:\\repo", "origin");
        var act = () => _useCase.ExecuteAsync(request);

        await act.Should().ThrowAsync<RemoteException>()
            .WithMessage("*newer than supported version*");
    }

    [Fact]
    public async Task ExecuteAsync_SpecificBranchNotFoundOnRemote_ThrowsRemoteException()
    {
        _context.GetRemote("origin").Returns(new RemoteConfig(RemoteName.Origin, "https://remote.url"));
        _transport.HandshakeAsync(Arg.Any<CancellationToken>())
            .Returns(new RemoteHandshake(1, _repoId, "v0.1.0"));

        _transport.ListRefsAsync(Arg.Any<CancellationToken>())
            .Returns(new List<RemoteRef> { new("refs/heads/main", CommitId.Parse(new string('a', 64))) });

        var request = new FetchRequest("C:\\repo", "origin", "feature-x");
        var act = () => _useCase.ExecuteAsync(request);

        await act.Should().ThrowAsync<RemoteException>()
            .WithMessage("*Remote branch 'feature-x' not found*");
    }

    [Fact]
    public async Task ExecuteAsync_DryRun_ReturnsObjectCountsWithoutUpdatingRefs()
    {
        _context.GetRemote("origin").Returns(new RemoteConfig(RemoteName.Origin, "https://remote.url"));
        _transport.HandshakeAsync(Arg.Any<CancellationToken>())
            .Returns(new RemoteHandshake(1, _repoId, "v0.1.0"));

        var commit = CommitId.Parse(new string('a', 64));
        _transport.ListRefsAsync(Arg.Any<CancellationToken>())
            .Returns(new List<RemoteRef> { new("refs/heads/main", commit) });

        // Local object store already has commit
        _context.ObjectStore.Exists(commit.Value).Returns(true);

        var request = new FetchRequest("C:\\repo", "origin", DryRun: true);
        var result = await _useCase.ExecuteAsync(request);

        result.IsDryRun.Should().BeTrue();
        result.ObjectsFetched.Should().Be(0);
        await _context.DidNotReceiveWithAnyArgs().UpdateRemoteTrackingCommitAsync(default!, default, default, default);
        _quarantineStore.Received(1).DiscardSession();
    }
}
