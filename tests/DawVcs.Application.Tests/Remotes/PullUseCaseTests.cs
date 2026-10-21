using DawVcs.Application.Checkouts;
using DawVcs.Application.Diagnostics;
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

public sealed class PullUseCaseTests
{
    private readonly IRepositoryContext _context = Substitute.For<IRepositoryContext>();
    private readonly FetchUseCase _fetchUseCase;
    private readonly CheckoutUseCase _checkoutUseCase;
    private readonly AncestryService _ancestryService = Substitute.For<AncestryService>();
    private readonly PullUseCase _useCase;

    private readonly BranchName _main = BranchName.Main;
    private readonly CommitId _localCommit = CommitId.Parse(new string('1', 64));
    private readonly CommitId _remoteCommit = CommitId.Parse(new string('2', 64));

    public PullUseCaseTests()
    {
        var transportFactory = Substitute.For<IRemoteTransportFactory>();
        var quarantineStore = Substitute.For<IQuarantineStore>();
        _fetchUseCase = Substitute.For<FetchUseCase>(
            (Func<string, IRepositoryContext>)(_ => _context),
            transportFactory,
            (Func<string, IQuarantineStore>)(_ => quarantineStore),
            null);

        _fetchUseCase.ExecuteAsync(Arg.Any<FetchRequest>(), Arg.Any<CancellationToken>())
            .Returns(new FetchResult("origin", Array.Empty<UpdatedRemoteRef>(), 0, 0, false));

        _checkoutUseCase = Substitute.For<CheckoutUseCase>(
            (Func<string, IRepositoryContext>)(_ => _context),
            null);

        _context.GetCurrentBranch().Returns(_main);
        _context.GetUpstream(_main).Returns(("origin", _main));
        _context.GetBranchCommit(_main).Returns(_localCommit);
        _context.GetRemoteTrackingCommit("origin", _main).Returns(_remoteCommit);

        _useCase = new PullUseCase(
            _ => _context,
            _fetchUseCase,
            _checkoutUseCase,
            _ancestryService,
            doctorUseCase: null);
    }

    [Fact]
    public async Task ExecuteAsync_UpToDate_ReturnsAlreadyUpToDateWithoutCheckout()
    {
        _ancestryService.DetermineSyncStateAsync(_localCommit, _remoteCommit, _context, Arg.Any<CancellationToken>())
            .Returns(new SyncAncestryClassification(SyncAncestryState.UpToDate));

        var request = new PullRequest("C:\\repo");
        var result = await _useCase.ExecuteAsync(request);

        result.State.Should().Be(SyncAncestryState.UpToDate);
        result.Message.Should().Contain("Already up to date");
        await _checkoutUseCase.DidNotReceiveWithAnyArgs().ExecuteFastForwardAsync(default!, default, default);
    }

    [Fact]
    public async Task ExecuteAsync_LocalAhead_ReturnsLocalAheadWithoutCheckout()
    {
        _ancestryService.DetermineSyncStateAsync(_localCommit, _remoteCommit, _context, Arg.Any<CancellationToken>())
            .Returns(new SyncAncestryClassification(SyncAncestryState.LocalAhead, CommitsAhead: 3));

        var request = new PullRequest("C:\\repo");
        var result = await _useCase.ExecuteAsync(request);

        result.State.Should().Be(SyncAncestryState.LocalAhead);
        result.Message.Should().Contain("ahead of 'origin/main' by 3 commit(s)");
        await _checkoutUseCase.DidNotReceiveWithAnyArgs().ExecuteFastForwardAsync(default!, default, default);
    }

    [Fact]
    public async Task ExecuteAsync_Diverged_ThrowsRejectedNonFastForwardException()
    {
        _ancestryService.DetermineSyncStateAsync(_localCommit, _remoteCommit, _context, Arg.Any<CancellationToken>())
            .Returns(new SyncAncestryClassification(SyncAncestryState.Diverged));

        var request = new PullRequest("C:\\repo");
        var act = () => _useCase.ExecuteAsync(request);

        var ex = await act.Should().ThrowAsync<RejectedNonFastForwardException>();
        ex.Which.RemoteName.Should().Be("origin");
        ex.Which.BranchName.Should().Be("main");
        ex.Which.LocalCommit.Should().Be(_localCommit);
        ex.Which.RemoteCommit.Should().Be(_remoteCommit);
    }

    [Fact]
    public async Task ExecuteAsync_FastForwardDryRun_ReturnsSimulatedMessageWithoutCheckout()
    {
        _ancestryService.DetermineSyncStateAsync(_localCommit, _remoteCommit, _context, Arg.Any<CancellationToken>())
            .Returns(new SyncAncestryClassification(SyncAncestryState.FastForward, CommitsBehind: 2));

        var request = new PullRequest("C:\\repo", DryRun: true);
        var result = await _useCase.ExecuteAsync(request);

        result.IsDryRun.Should().BeTrue();
        result.State.Should().Be(SyncAncestryState.FastForward);
        result.Message.Should().Contain("Would fast-forward");
        await _checkoutUseCase.DidNotReceiveWithAnyArgs().ExecuteFastForwardAsync(default!, default, default);
    }
}
