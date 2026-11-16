using Avalonia.Headless.XUnit;

using DawVcs.Application.Common;
using DawVcs.Desktop.Services;

using FluentAssertions;

using Microsoft.Extensions.DependencyInjection;

using Xunit;

namespace DawVcs.Desktop.Tests;

public sealed class OperationCoordinatorTests
{
    [Fact]
    public async Task Switch_CancelsWaitsAndDiscardsLateResultsAndProgress()
    {
        using var repositories = new TestRepositories();
        var first = await repositories.CreateAsync("first", TestContext.Current.CancellationToken);
        var second = await repositories.CreateAsync("second", TestContext.Current.CancellationToken);
        var dispatcher = new TestDispatcher();
        await using var coordinator = new OperationCoordinator(new EngineScopeFactory(), dispatcher);
        (await coordinator.OpenAsync(first)).Should().BeTrue();
        var oldId = coordinator.SessionId;
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken observed = default;
        var delivered = false;
        var oldWork = coordinator.RunAsync(async (_, _, progress, token) =>
        {
            observed = token;
            progress.Report(new OperationProgress(OperationStage.HashingFiles, 1, 2));
            entered.SetResult();
            await finish.Task; // Simulates noninterruptible backend work that finishes late.
            return "old";
        }, _ => delivered = true, TestContext.Current.CancellationToken);
        await entered.Task.WaitAsync(TestContext.Current.CancellationToken);
        var switching = coordinator.OpenAsync(second);
        observed.IsCancellationRequested.Should().BeTrue();
        switching.IsCompleted.Should().BeFalse();
        coordinator.RepositoryPath.Should().BeNull();
        finish.SetResult();
        (await oldWork).Should().BeFalse();
        (await switching).Should().BeTrue();
        dispatcher.Drain();
        delivered.Should().BeFalse();
        coordinator.RepositoryPath.Should().Be(second);
        coordinator.SessionId!.Value.Should().NotBe(oldId!.Value);
        coordinator.Progress.Should().BeNull();
    }

    [AvaloniaFact]
    public async Task WorkAndSynchronousPrefix_RunOffCallerThreadAndRemainSerialized()
    {
        using var repositories = new TestRepositories();
        var root = await repositories.CreateAsync("repo", TestContext.Current.CancellationToken);
        await using var coordinator = new OperationCoordinator(new EngineScopeFactory(), new TestDispatcher());
        await coordinator.OpenAsync(root);
        var caller = Environment.CurrentManagedThreadId;
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var firstThread = 0;
        var secondStarted = false;
        var first = coordinator.RunAsync(async (_, _, _, _) =>
        {
            firstThread = Environment.CurrentManagedThreadId;
            entered.SetResult();
            await release.Task;
            return 1;
        }, _ => { }, TestContext.Current.CancellationToken);
        await entered.Task.WaitAsync(TestContext.Current.CancellationToken);
        var second = coordinator.RunAsync((_, _, _, _) => { secondStarted = true; return Task.FromResult(2); }, _ => { }, TestContext.Current.CancellationToken);
        secondStarted.Should().BeFalse();
        coordinator.IsBusy.Should().BeTrue();
        release.SetResult();
        await Task.WhenAll(first, second);
        firstThread.Should().NotBe(caller);
        secondStarted.Should().BeTrue();
        coordinator.IsBusy.Should().BeFalse();
    }

    [Fact]
    public async Task CancellationAndDisposal_KeepBusyUntilBackendStops()
    {
        using var repositories = new TestRepositories();
        var root = await repositories.CreateAsync("repo", TestContext.Current.CancellationToken);
        var dispatcher = new TestDispatcher();
        var coordinator = new OperationCoordinator(new EngineScopeFactory(), dispatcher);
        await coordinator.OpenAsync(root);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var work = coordinator.RunAsync(async (_, _, progress, _) =>
        {
            for (int i = 0; i < 10_000; i++) progress.Report(new OperationProgress(OperationStage.HashingFiles, i));
            entered.SetResult();
            await release.Task;
            return 1;
        }, _ => throw new InvalidOperationException("Cancelled results must not be published"), TestContext.Current.CancellationToken);
        await entered.Task.WaitAsync(TestContext.Current.CancellationToken);
        dispatcher.Posted.Should().ContainSingle();
        coordinator.Cancel();
        dispatcher.Drain();
        coordinator.IsBusy.Should().BeTrue();
        coordinator.IsCancelling.Should().BeTrue();
        var disposal = coordinator.DisposeAsync().AsTask();
        disposal.IsCompleted.Should().BeFalse();
        release.SetResult();
        (await work).Should().BeFalse();
        await disposal;
        coordinator.IsBusy.Should().BeFalse();
        coordinator.SessionId.Should().BeNull();
        await coordinator.DisposeAsync();
    }

    [Fact]
    public async Task InvalidRepository_DoesNotCreateMetadataAndClearsOldSession()
    {
        using var repositories = new TestRepositories();
        var valid = await repositories.CreateAsync("repo", TestContext.Current.CancellationToken);
        await using var coordinator = new OperationCoordinator(new EngineScopeFactory(), new TestDispatcher());
        await coordinator.OpenAsync(valid);
        var missing = Path.Combine(repositories.Root, "absent");
        (await coordinator.OpenAsync(missing)).Should().BeFalse();
        Directory.Exists(missing).Should().BeFalse();
        coordinator.SessionId.Should().BeNull();
        coordinator.Failure!.Category.Should().Be(OperationFailureCategory.Validation);
    }
    [Fact]
    public async Task LateCancel_DoesNotHideSuccessfullyPublishedBackendOutcome()
    {
        using var repositories = new TestRepositories();
        var root = await repositories.CreateAsync("repo", TestContext.Current.CancellationToken);
        await using var coordinator = new OperationCoordinator(new EngineScopeFactory(), new TestDispatcher());
        await coordinator.OpenAsync(root);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        string? delivered = null;
        var running = coordinator.RunAsync(async (_, _, _, _) =>
        {
            entered.SetResult();
            await release.Task;
            return "published-result";
        }, value => delivered = value, TestContext.Current.CancellationToken);
        await entered.Task.WaitAsync(TestContext.Current.CancellationToken);
        coordinator.Cancel();
        release.SetResult();
        (await running).Should().BeTrue();
        delivered.Should().Be("published-result");
        coordinator.Failure.Should().BeNull();
    }

    [Fact]
    public async Task Failure_DisposesScopeAndReleasesCoordinatorForNextOperation()
    {
        using var repositories = new TestRepositories();
        var root = await repositories.CreateAsync("repo", TestContext.Current.CancellationToken);
        var marker = new DisposalMarker();
        var factory = new EngineScopeFactory(services => services.AddSingleton(_ => marker));
        await using var coordinator = new OperationCoordinator(factory, new TestDispatcher());
        await coordinator.OpenAsync(root);
        var failure = new IOException("Private technical detail");
        await coordinator.RunAsync<int>((services, _, _, _) =>
        {
            _ = services.GetRequiredService<DisposalMarker>();
            throw failure;
        }, _ => { }, TestContext.Current.CancellationToken);
        marker.Disposed.Should().BeTrue();
        coordinator.Failure!.TechnicalException.Should().BeSameAs(failure);
        coordinator.Failure.Category.Should().Be(OperationFailureCategory.PermissionOrIoFailure);
        (await coordinator.RunAsync((_, _, _, _) => Task.FromResult(1), _ => { }, TestContext.Current.CancellationToken)).Should().BeTrue();
        coordinator.Failure.Should().BeNull();
    }

    [Fact]
    public async Task CancelledSwitch_DoesNotFinishWhilePreviousBackendStillRuns()
    {
        using var repositories = new TestRepositories();
        var first = await repositories.CreateAsync("one", TestContext.Current.CancellationToken);
        var second = await repositories.CreateAsync("two", TestContext.Current.CancellationToken);
        await using var coordinator = new OperationCoordinator(new EngineScopeFactory(), new TestDispatcher());
        await coordinator.OpenAsync(first);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var work = coordinator.RunAsync(async (_, _, _, _) => { entered.SetResult(); await release.Task; return 1; }, _ => { }, TestContext.Current.CancellationToken);
        await entered.Task.WaitAsync(TestContext.Current.CancellationToken);
        var switching = coordinator.OpenAsync(second);
        coordinator.Cancel();
        switching.IsCompleted.Should().BeFalse();
        coordinator.IsBusy.Should().BeTrue();
        release.SetResult();
        await work;
        (await switching).Should().BeFalse();
        coordinator.RepositoryPath.Should().BeNull();
        coordinator.Failure!.Category.Should().Be(OperationFailureCategory.Cancellation);
    }

    private sealed class DisposalMarker : IDisposable
    {
        public bool Disposed { get; private set; }
        public void Dispose() => Disposed = true;
    }

}
