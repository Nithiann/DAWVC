using System.Diagnostics;
using System.Text.Json;

using DawVcs.Application.Branches;
using DawVcs.Application.Checkouts;
using DawVcs.Application.Commits;
using DawVcs.Application.Common;
using DawVcs.Application.Dependencies;
using DawVcs.Application.Diagnostics;
using DawVcs.Application.Operations;
using DawVcs.Application.Recovery;
using DawVcs.Application.Remotes;
using DawVcs.Application.Repositories;
using DawVcs.Application.Scanning;
using DawVcs.Application.Staging;
using DawVcs.Domain.Artifacts;
using DawVcs.Domain.Common;
using DawVcs.Domain.Configuration;
using DawVcs.Domain.Remotes;
using DawVcs.Domain.Repositories;
using DawVcs.Infrastructure.Remotes;
using DawVcs.Infrastructure.Repositories;

using FluentAssertions;

using Xunit;

namespace DawVcs.EndToEndTests;

public sealed class RepositoryOperationTests : IDisposable
{
    private readonly string _parent = Path.Combine(Path.GetTempPath(), "dawvc-operations-" + Guid.NewGuid().ToString("N"));
    private string Root => Path.Combine(_parent, "song");
    private static FileSystemRepositoryContext Context(string root) => new(root);

    public RepositoryOperationTests()
    {
        Directory.CreateDirectory(Root);
        using var context = Context(Root);
        context.SaveConfig(new RepositoryConfig(RepositoryId.New(), "Song", new ArtifactPath("Song.flp")));
        context.SetCurrentBranch(BranchName.Main);
        File.WriteAllText(Path.Combine(Root, "Song.flp"), "synthetic project");
        File.WriteAllText(Path.Combine(Root, "sample.wav"), "sample");
    }

    [Theory]
    [InlineData("add")]
    [InlineData("commit")]
    [InlineData("status")]
    [InlineData("doctor")]
    [InlineData("checkout")]
    [InlineData("switch")]
    [InlineData("merge")]
    [InlineData("fetch")]
    [InlineData("pull")]
    [InlineData("push")]
    [InlineData("recover")]
    [InlineData("branch")]
    [InlineData("remote")]
    [InlineData("init")]
    [InlineData("bind")]
    [InlineData("track")]
    public async Task BusyPublicEntry_RefusesBeforeContextOrMetadataWrites(string command)
    {
        using var owner = RepositoryOperation.Enter(Root);
        var before = Snapshot();
        IRepositoryContext Unexpected(string _) => throw new InvalidOperationException("A busy operation must not open a context.");
        var transport = new DefaultRemoteTransportFactory();
        var fetch = new FetchUseCase(Unexpected, transport, _ => throw new InvalidOperationException("Busy operation must not open quarantine."));
        var checkout = new CheckoutUseCase(Unexpected);
        async Task Attempt()
        {
            switch (command)
            {
                case "add": await new AddUseCase(Unexpected).ExecuteAsync(new AddRequest(Root, ["sample.wav"])); break;
                case "commit": await new CommitUseCase(Unexpected).ExecuteAsync(new CommitRequest(Root, "busy")); break;
                case "status": await new StatusUseCase(Unexpected).ExecuteAsync(new StatusRequest(Root)); break;
                case "doctor": await new DoctorUseCase(Unexpected).ExecuteAsync(new DoctorRequest(Root)); break;
                case "checkout": await checkout.ExecuteAsync(new CheckoutRequest(Root, "main")); break;
                case "switch": await new SwitchUseCase(Unexpected).ExecuteAsync(new SwitchRequest(Root, "main")); break;
                case "merge": await new MergeUseCase(Unexpected, checkout).ExecuteAsync(new MergeRequest(Root, "incoming", MergeSnapshotChoice.Local, "busy")); break;
                case "fetch": await fetch.ExecuteAsync(new FetchRequest(Root)); break;
                case "pull": await new PullUseCase(Unexpected, fetch, checkout).ExecuteAsync(new PullRequest(Root)); break;
                case "push": await new PushUseCase(Unexpected, transport).ExecuteAsync(new PushRequest(Root)); break;
                case "recover": await new WorkspaceRecoveryUseCase(Unexpected).RollbackAsync(Root); break;
                case "branch": await new BranchUseCase(Unexpected).CreateAsync(new BranchCreateRequest(Root, "other")); break;
                case "init": await new InitRepositoryUseCase(Unexpected).ExecuteAsync(new InitRequest(Root)); break;
                case "bind": await new BindDependencyUseCase(Unexpected).ExecuteAsync(new BindDependencyRequest(Root, "sample", "sample.wav")); break;
                case "track": new RemoteUseCase(Unexpected).TrackRemote(new TrackRemoteRequest(Root, "backup", "published")); break;
                case "remote": new RemoteUseCase(Unexpected).AddRemote(new RemoteAddRequest(Root, "origin", _parent)); break;
            }
        }
        await FluentActions.Awaiting(Attempt).Should().ThrowAsync<RepositoryBusyException>();
        Snapshot().Should().BeEquivalentTo(before);
        OperationFailure.FromException(new RepositoryBusyException("busy")).Category.Should().Be(OperationFailureCategory.RepositoryBusy);
    }

    [Fact]
    public async Task ExplicitNestedCapability_AllowsNestedWorkButDoesNotEscapeThroughCallbacks()
    {
        var stages = new List<OperationStage>();
        var progress = new InlineProgress(value =>
        {
            stages.Add(value.Stage);
            var callback = () => Task.Run(() => new AddUseCase(Context).ExecuteAsync(new AddRequest(Root, ["sample.wav"]))).GetAwaiter().GetResult();
            callback.Should().Throw<RepositoryBusyException>();
        });
        await new CommitUseCase(Context).ExecuteAsync(new CommitRequest(Root, "initial"), CancellationToken.None, progress);
        stages.Should().Contain(OperationStage.Completed);
        await new AddUseCase(Context).ExecuteAsync(new AddRequest(Root, ["sample.wav"]));
        using var owner = RepositoryOperation.Enter(Root);
        await new StatusUseCase(Context).ExecuteAsync(new StatusRequest(Root) { Operation = owner });
        var wrong = () => RepositoryOperation.Enter(_parent, owner);
        wrong.Should().Throw<ArgumentException>();
        JsonSerializer.Serialize(new StatusRequest(Root) { Operation = owner }).Should().NotContain("Operation");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ChildProcessHolder_RefusesCliThenReleasesOnExitOrKill(bool kill)
    {
        using var holder = Start(typeof(DawVcs.RecoveryCrashHost.Program).Assembly.Location, "hold-operation", Root);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        try
        {
            (await holder.StandardOutput.ReadLineAsync(timeout.Token)).Should().Be("operation-held");
            var before = Snapshot();
            using var cli = Start(typeof(DawVcs.Cli.Program).Assembly.Location, "add", "sample.wav", "--dir", Root);
            var output = cli.StandardOutput.ReadToEndAsync(timeout.Token);
            var error = cli.StandardError.ReadToEndAsync(timeout.Token);
            await cli.WaitForExitAsync(timeout.Token);
            cli.ExitCode.Should().Be(11, await output + await error);
            Snapshot().Should().BeEquivalentTo(before);
            // Another working copy is independent, even with the same repository identity.
            var second = Path.Combine(_parent, "second");
            Directory.CreateDirectory(Path.Combine(second, ".dawvc"));
            File.Copy(Path.Combine(Root, "dawvc.yaml"), Path.Combine(second, "dawvc.yaml"));
            using (RepositoryOperation.Enter(second)) { }
            if (kill) holder.Kill(true);
            else { await holder.StandardInput.WriteLineAsync(); await holder.StandardInput.FlushAsync(timeout.Token); }
            await holder.WaitForExitAsync(timeout.Token);
            await AfterProcessExitAsync(holder,
                () => new AddUseCase(Context).ExecuteAsync(new AddRequest(Root, ["sample.wav"])), timeout.Token);
            File.Exists(Path.Combine(Root, ".dawvc", "operation.lock")).Should().BeTrue();
        }
        finally { if (!holder.HasExited) { holder.Kill(true); await holder.WaitForExitAsync(CancellationToken.None); } }
    }

    [Fact]
    public async Task FailedOrCancelledOperation_ReleasesOwnershipWithoutDeletingLock()
    {
        await FluentActions.Awaiting(() => new AddUseCase(Context).ExecuteAsync(new AddRequest(Root, ["missing.wav"])))
            .Should().ThrowAsync<FileNotFoundException>();
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await FluentActions.Awaiting(() => new AddUseCase(Context).ExecuteAsync(new AddRequest(Root), cancelled.Token))
            .Should().ThrowAsync<OperationCanceledException>();
        await new AddUseCase(Context).ExecuteAsync(new AddRequest(Root, ["sample.wav"]));
        var parent = RepositoryOperation.Enter(Root);
        var nested = RepositoryOperation.Enter(Root, parent);
        parent.Dispose();
        var independent = () => RepositoryOperation.Enter(Root);
        independent.Should().Throw<RepositoryBusyException>();
        nested.Dispose();
        using var fresh = RepositoryOperation.Enter(Root);
        var stale = () => RepositoryOperation.Enter(Root, parent);
        stale.Should().Throw<ObjectDisposedException>();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CrossProcessCreationLease_CoordinatesBareAndWorkingTargets(bool existingEmpty)
    {
        var target = Path.Combine(_parent, "provision");
        if (existingEmpty) Directory.CreateDirectory(target);
        using var holder = Start(typeof(DawVcs.RecoveryCrashHost.Program).Assembly.Location, "hold-creation", target);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        try
        {
            (await holder.StandardOutput.ReadLineAsync(timeout.Token)).Should().Be("operation-held");
            var attempt = () => RepositoryOperation.Enter(target, create: true);
            attempt.Should().Throw<RepositoryBusyException>();
            if (existingEmpty) Directory.GetFileSystemEntries(target).Should().BeEmpty();
            else Directory.Exists(target).Should().BeFalse();
            holder.Kill(true);
            await holder.WaitForExitAsync(timeout.Token);
            using var released = await AfterProcessExitAsync(holder,
                () => Task.FromResult(RepositoryOperation.Enter(target, create: true)), timeout.Token);
            RepositoryOperation.OnlyCreationScaffold(target).Should().BeTrue();
        }
        finally { if (!holder.HasExited) { holder.Kill(true); await holder.WaitForExitAsync(CancellationToken.None); } }
    }

    [Fact]
    public async Task IncomingPush_HoldsDestinationLeaseThroughSessionAndRejectsAnotherWriter()
    {
        var bare = Path.Combine(_parent, "bare");
        Directory.CreateDirectory(Path.Combine(bare, "objects"));
        Directory.CreateDirectory(Path.Combine(bare, "refs", "heads"));
        File.Copy(Path.Combine(Root, "dawvc.yaml"), Path.Combine(bare, "dawvc.yaml"));
        using var transport = new FileSystemRemoteTransport(bare);
        using (var held = RepositoryOperation.Enter(bare, remoteMetadata: bare))
            await FluentActions.Awaiting(() => transport.BeginPushAsync(BranchName.Main, null)).Should().ThrowAsync<RemoteException>();
        using (var session = await transport.BeginPushAsync(BranchName.Main, null))
        {
            var competitor = () => RepositoryOperation.Enter(bare, remoteMetadata: bare);
            competitor.Should().Throw<RepositoryBusyException>();
            await FluentActions.Awaiting(() => transport.BeginPushAsync(BranchName.Main, null)).Should().ThrowAsync<RemoteException>();
        }
        using var released = RepositoryOperation.Enter(bare, remoteMetadata: bare);
        Directory.GetDirectories(Path.Combine(bare, "incoming")).Should().BeEmpty();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CreationLease_CoordinatesBareAndWorkingProvisioning(bool existingEmpty)
    {
        var target = Path.Combine(_parent, "new");
        if (existingEmpty) Directory.CreateDirectory(target);
        using (var bare = RepositoryOperation.Enter(target, create: true, bareCreation: true))
        {
            var working = () => RepositoryOperation.Enter(target, create: true);
            working.Should().Throw<RepositoryBusyException>();
        }
        using var next = RepositoryOperation.Enter(target, create: true);
        RepositoryOperation.OnlyCreationScaffold(target).Should().BeTrue();
    }

    [Fact]
    public void UnknownLockContents_ArePreservedAndFailedAcquisitionDoesNotLeakOwnership()
    {
        var path = Path.Combine(Root, ".dawvc", "operation.lock");
        File.WriteAllText(path, "unknown coordination protocol");
        var attempt = () => RepositoryOperation.Enter(Root);
        attempt.Should().Throw<IOException>();
        attempt.Should().Throw<IOException>();
        File.ReadAllText(path).Should().Be("unknown coordination protocol");
    }

    [Fact]
    public void CanonicalPathAliases_UseTheSameOwnershipDomain()
    {
        using var owner = RepositoryOperation.Enter(Root);
        var alias = () => RepositoryOperation.Enter(Path.Combine(Root, "..", "song", "."));
        alias.Should().Throw<RepositoryBusyException>();
    }

    private static async Task<T> AfterProcessExitAsync<T>(Process holder, Func<Task<T>> action, CancellationToken cancellationToken)
    {
        holder.HasExited.Should().BeTrue("readiness polling is only allowed after the owning process has exited");
        var elapsed = Stopwatch.StartNew();
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try { return await action(); }
            catch (RepositoryBusyException error) when (OperatingSystem.IsWindows()
                && error.InnerException is IOException io && (io.HResult & 0xffff) is 32 or 33
                && elapsed.Elapsed < TimeSpan.FromSeconds(5))
            {
                // A signaled Windows process exit does not guarantee immediate file reopenability.
                // Retry only native sharing/lock violations, never in-process ownership or other failures.
                await Task.Delay(25, cancellationToken);
            }
        }
    }

    private Dictionary<string, string> Snapshot() => Directory.GetFiles(Root, "*", SearchOption.AllDirectories)
        .Where(path => Path.GetFileName(path) != "operation.lock")
        .ToDictionary(path => Path.GetRelativePath(Root, path), path => Convert.ToHexString(File.ReadAllBytes(path)));
    private static Process Start(string assembly, params string[] args)
    {
        var start = new ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet")
        { UseShellExecute = false, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add(assembly);
        foreach (var arg in args) start.ArgumentList.Add(arg);
        return Process.Start(start)!;
    }
    private sealed class InlineProgress(Action<OperationProgress> action) : IProgress<OperationProgress>
    { public void Report(OperationProgress value) => action(value); }
    public void Dispose() { if (Directory.Exists(_parent)) Directory.Delete(_parent, true); }
}
