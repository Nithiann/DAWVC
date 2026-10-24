using DawVcs.Application.Checkouts;
using DawVcs.Application.Common;
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

    [Theory]
    [InlineData(SyncAncestryState.UpToDate, false)]
    [InlineData(SyncAncestryState.LocalAhead, false)]
    [InlineData(SyncAncestryState.FastForward, true)]
    [InlineData(SyncAncestryState.FastForward, false)]
    public async Task ExecuteAsync_Progress_CompletesOnceAcrossAllSuccessPaths(SyncAncestryState state, bool dryRun)
    {
        ConfigureChildProgress();
        _ancestryService.DetermineSyncStateAsync(_localCommit, _remoteCommit, _context, Arg.Any<CancellationToken>())
            .Returns(new SyncAncestryClassification(state, CommitsBehind: 2));
        var progress = new RecordingRemoteProgress();

        var result = await _useCase.ExecuteAsync(new PullRequest("C:\\repo", DryRun: dryRun), default, progress);

        result.State.Should().Be(state);
        result.IsDryRun.Should().Be(dryRun);
        progress.Reports.Should().Contain(p => p.Stage == OperationStage.TransferringObjects);
        progress.Reports.Count(p => p.Stage == OperationStage.Completed).Should().Be(1);
        progress.Reports[^1].Stage.Should().Be(OperationStage.Completed);
        if (dryRun || state != SyncAncestryState.FastForward)
        {
            progress.Reports.Should().NotContain(p => p.Stage == OperationStage.PublishingFiles);
            await _checkoutUseCase.DidNotReceiveWithAnyArgs().ExecuteFastForwardAsync(default!, default, default, default);
        }
        else
        {
            progress.Reports.Should().Contain(p => p.Stage == OperationStage.PublishingFiles);
        }
        await _fetchUseCase.Received(1).ExecuteAsync(
            Arg.Is<FetchRequest>(r => r.DryRun == dryRun), Arg.Any<CancellationToken>(), Arg.Any<IProgress<OperationProgress>>());
    }

    [Fact]
    public async Task ExecuteAsync_ExplicitNullProgress_RemainsCompatible()
    {
        _ancestryService.DetermineSyncStateAsync(_localCommit, _remoteCommit, _context, Arg.Any<CancellationToken>())
            .Returns(new SyncAncestryClassification(SyncAncestryState.UpToDate));
        var request = new PullRequest("C:\\repo");
        var legacy = await _useCase.ExecuteAsync(request);
        var explicitNull = await _useCase.ExecuteAsync(request, default, null);
        explicitNull.Should().BeEquivalentTo(legacy);
    }

    [Fact]
    public async Task ExecuteAsync_DivergedProgress_DoesNotComplete()
    {
        ConfigureChildProgress();
        _ancestryService.DetermineSyncStateAsync(_localCommit, _remoteCommit, _context, Arg.Any<CancellationToken>())
            .Returns(new SyncAncestryClassification(SyncAncestryState.Diverged));
        var progress = new RecordingRemoteProgress();
        var act = () => _useCase.ExecuteAsync(new PullRequest("C:\\repo"), default, progress);
        await act.Should().ThrowAsync<RejectedNonFastForwardException>();
        progress.Reports.Should().NotContain(p => p.Stage == OperationStage.Completed);
    }

    [Fact]
    public async Task ExecuteAsync_NestedObserverFailure_PropagatesWithoutCompletion()
    {
        ConfigureChildProgress();
        var progress = new RecordingRemoteProgress { ThrowOn = OperationStage.TransferringObjects };
        var act = () => _useCase.ExecuteAsync(new PullRequest("C:\\repo"), default, progress);
        (await act.Should().ThrowAsync<InvalidOperationException>()).Which.Should().BeSameAs(progress.ObserverFailure);
        progress.Reports.Should().NotContain(p => p.Stage == OperationStage.Completed);
    }

    [Fact]
    public async Task ExecuteAsync_DoctorStageObserverFailure_IsNotSwallowed()
    {
        ConfigureChildProgress();
        _ancestryService.DetermineSyncStateAsync(_localCommit, _remoteCommit, _context, Arg.Any<CancellationToken>())
            .Returns(new SyncAncestryClassification(SyncAncestryState.FastForward));
        var doctor = new DoctorUseCase(_ => throw new InvalidOperationException("Doctor should not run"));
        var useCase = new PullUseCase(_ => _context, _fetchUseCase, _checkoutUseCase, _ancestryService, doctor);
        var progress = new RecordingRemoteProgress { ThrowOn = OperationStage.ProbingEnvironment };
        var act = () => useCase.ExecuteAsync(new PullRequest("C:\\repo"), default, progress);
        (await act.Should().ThrowAsync<InvalidOperationException>()).Which.Should().BeSameAs(progress.ObserverFailure);
        progress.Reports.Should().NotContain(p => p.Stage == OperationStage.Completed);
    }

    [Fact]
    public async Task ExecuteAsync_CancellationAfterCheckoutSuccess_StillCompletes()
    {
        ConfigureChildProgress();
        using var cancellation = new CancellationTokenSource();
        _ancestryService.DetermineSyncStateAsync(_localCommit, _remoteCommit, _context, Arg.Any<CancellationToken>())
            .Returns(new SyncAncestryClassification(SyncAncestryState.FastForward));
        _checkoutUseCase.ExecuteFastForwardAsync(Arg.Any<FastForwardTarget>(), Arg.Any<bool>(), Arg.Any<CancellationToken>(), Arg.Any<IProgress<OperationProgress>>())
            .Returns(call =>
            {
                cancellation.Cancel();
                call.Arg<IProgress<OperationProgress>>().Report(new OperationProgress(OperationStage.Completed));
                return new CheckoutResult(_remoteCommit, SnapshotId.Parse(new string('a', 64)), "C:\\repo", new List<string> { "Song.flp" });
            });
        var progress = new RecordingRemoteProgress();

        var result = await _useCase.ExecuteAsync(new PullRequest("C:\\repo"), cancellation.Token, progress);

        result.State.Should().Be(SyncAncestryState.FastForward);
        progress.Reports.Count(p => p.Stage == OperationStage.Completed).Should().Be(1);
    }

    private void ConfigureChildProgress()
    {
        _fetchUseCase.ExecuteAsync(Arg.Any<FetchRequest>(), Arg.Any<CancellationToken>(), Arg.Any<IProgress<OperationProgress>>())
            .Returns(call =>
            {
                var child = call.Arg<IProgress<OperationProgress>>();
                child.Report(new OperationProgress(OperationStage.TransferringObjects, 0, 0));
                child.Report(new OperationProgress(OperationStage.Completed));
                return new FetchResult("origin", Array.Empty<UpdatedRemoteRef>(), 0, 0, call.Arg<FetchRequest>().DryRun);
            });
        _checkoutUseCase.ExecuteFastForwardAsync(Arg.Any<FastForwardTarget>(), Arg.Any<bool>(), Arg.Any<CancellationToken>(), Arg.Any<IProgress<OperationProgress>>())
            .Returns(call =>
            {
                var child = call.Arg<IProgress<OperationProgress>>();
                child.Report(new OperationProgress(OperationStage.PublishingFiles, 1, 1));
                child.Report(new OperationProgress(OperationStage.Completed));
                return new CheckoutResult(_remoteCommit, SnapshotId.Parse(new string('a', 64)), "C:\\repo", new List<string> { "Song.flp" });
            });
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
