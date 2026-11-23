using DawVcs.Application.Branches;
using DawVcs.Application.Commits;
using DawVcs.Domain.Common;
using DawVcs.Domain.Entities;
using DawVcs.Domain.Repositories;

using FluentAssertions;

using NSubstitute;

using Xunit;

namespace DawVcs.Application.Tests.Common;

public sealed class RepositoryContextOwnershipTests
{
    private static readonly CommitId HeadCommit = CommitId.Parse(new string('a', 64));
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task ListBranches_Success_DisposesEachFreshContextExactlyOnce()
    {
        var contexts = new List<TrackingContext>();
        var branches = new[] { new BranchInfo(BranchName.Main, null, true) };
        var useCase = new BranchUseCase(_ =>
        {
            var tracking = new TrackingContext();
            tracking.Context.GetBranches().Returns(_ =>
            {
                tracking.DisposeCount.Should().Be(0);
                return branches;
            });
            contexts.Add(tracking);
            return tracking.Context;
        });

        var first = await useCase.ListAsync("repo");
        var second = await useCase.ListAsync("repo");

        first.Should().Equal(branches);
        second.Should().Equal(branches);
        contexts.Should().HaveCount(2);
        contexts[0].Context.Should().NotBeSameAs(contexts[1].Context);
        contexts.Should().OnlyContain(context => context.DisposeCount == 1);
    }

    [Fact]
    public async Task Log_EmptyHeadEarlyReturn_DisposesContextExactlyOnce()
    {
        var tracking = new TrackingContext();
        tracking.Context.GetBranchCommit(BranchName.Main).Returns((CommitId?)null);
        var useCase = new LogUseCase(_ => tracking.Context);

        var result = await useCase.ExecuteAsync(new LogRequest("repo"));

        result.Entries.Should().BeEmpty();
        tracking.DisposeCount.Should().Be(1);
        await tracking.Context.DidNotReceiveWithAnyArgs().LoadCommitAsync(default, default);
    }

    [Fact]
    public async Task Log_LoadFails_DisposesContextAndPreservesException()
    {
        var tracking = new TrackingContext();
        var failure = new InvalidOperationException("Cannot load commit.");
        tracking.Context.LoadCommitAsync(HeadCommit, Arg.Any<CancellationToken>())
            .Returns(Task.FromException<Commit?>(failure));
        var useCase = new LogUseCase(_ => tracking.Context);

        var act = () => useCase.ExecuteAsync(new LogRequest("repo"));

        var assertion = await act.Should().ThrowAsync<InvalidOperationException>();
        assertion.Which.Should().BeSameAs(failure);
        tracking.DisposeCount.Should().Be(1);
    }

    [Fact]
    public async Task Log_CancelledAfterContextCreation_DisposesContextExactlyOnce()
    {
        using var cancellation = new CancellationTokenSource();
        var tracking = new TrackingContext();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        tracking.Context.LoadCommitAsync(HeadCommit, Arg.Any<CancellationToken>())
            .Returns(call => WaitForCancellationAsync(call.Arg<CancellationToken>()));
        var useCase = new LogUseCase(_ => tracking.Context);

        var operation = useCase.ExecuteAsync(new LogRequest("repo"), cancellation.Token);
        await entered.Task.WaitAsync(TestTimeout);
        operation.IsCompleted.Should().BeFalse();
        tracking.DisposeCount.Should().Be(0);

        cancellation.Cancel();
        var act = () => operation.WaitAsync(TestTimeout);

        var assertion = await act.Should().ThrowAsync<OperationCanceledException>();
        assertion.Which.CancellationToken.Should().Be(cancellation.Token);
        tracking.DisposeCount.Should().Be(1);

        async Task<Commit?> WaitForCancellationAsync(CancellationToken token)
        {
            tracking.DisposeCount.Should().Be(0);
            entered.SetResult();
            await Task.Delay(Timeout.Infinite, token);
            return null;
        }
    }

    [Fact]
    public async Task Log_PendingAwait_KeepsContextAliveUntilWorkCompletes()
    {
        var tracking = new TrackingContext();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var completion = new TaskCompletionSource<Commit?>(TaskCreationOptions.RunContinuationsAsynchronously);
        tracking.Context.LoadCommitAsync(HeadCommit, Arg.Any<CancellationToken>())
            .Returns(_ => LoadAfterReleaseAsync());
        var useCase = new LogUseCase(_ => tracking.Context);

        var operation = useCase.ExecuteAsync(new LogRequest("repo"));
        try
        {
            await entered.Task.WaitAsync(TestTimeout);
            operation.IsCompleted.Should().BeFalse();
            tracking.DisposeCount.Should().Be(0);
        }
        finally
        {
            completion.TrySetResult(null);
        }

        var result = await operation.WaitAsync(TestTimeout);

        result.Entries.Should().BeEmpty();
        tracking.DisposeCount.Should().Be(1);

        async Task<Commit?> LoadAfterReleaseAsync()
        {
            entered.SetResult();
            var commit = await completion.Task;
            tracking.DisposeCount.Should().Be(0);
            return commit;
        }
    }

    private sealed class TrackingContext
    {
        private int _disposeCount;

        public IRepositoryContext Context { get; } = Substitute.For<IRepositoryContext>();
        public int DisposeCount => Volatile.Read(ref _disposeCount);

        public TrackingContext()
        {
            Context.GetCurrentBranch().Returns(BranchName.Main);
            Context.GetBranchCommit(BranchName.Main).Returns(HeadCommit);
            Context.When(context => context.Dispose())
                .Do(_ => Interlocked.Increment(ref _disposeCount));
        }
    }
}
