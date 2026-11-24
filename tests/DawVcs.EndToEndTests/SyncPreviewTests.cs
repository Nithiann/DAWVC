using System.Diagnostics;
using System.Text;
using System.Text.Json;

using DawVcs.Application.Checkouts;
using DawVcs.Application.Commits;
using DawVcs.Application.Common;
using DawVcs.Application.Recovery;
using DawVcs.Application.Remotes;
using DawVcs.Domain.Artifacts;
using DawVcs.Domain.Common;
using DawVcs.Domain.Configuration;
using DawVcs.Domain.Entities;
using DawVcs.Domain.Remotes;
using DawVcs.Domain.Repositories;
using DawVcs.Domain.Storage;
using DawVcs.Infrastructure.Configuration;
using DawVcs.Infrastructure.Remotes;
using DawVcs.Infrastructure.Remotes.Ssh;
using DawVcs.Infrastructure.Repositories;
using DawVcs.Infrastructure.Storage;

using FluentAssertions;

using Xunit;

namespace DawVcs.EndToEndTests;

public sealed class SyncPreviewTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "dawvc-preview-" + Guid.NewGuid().ToString("N"));
    private readonly TestFactory _factory;
    private string Source => Path.Combine(_root, "source");
    private string Remote => Path.Combine(_root, "remote");
    private static FileSystemRepositoryContext Context(string path) => new(path);

    public SyncPreviewTests()
    {
        using var context = Context(Source);
        var config = new RepositoryConfig(RepositoryId.New(), "Preview song", new ArtifactPath("Song.flp"));
        context.SaveConfig(config);
        context.SetCurrentBranch(BranchName.Main);
        Directory.CreateDirectory(Path.Combine(Remote, "objects"));
        Directory.CreateDirectory(Path.Combine(Remote, "refs", "heads"));
        File.WriteAllText(Path.Combine(Remote, "dawvc.yaml"), YamlRepositoryConfigManager.ToYaml(config));
        var policy = Path.Combine(_root, "serve.json");
        File.WriteAllText(policy, JsonSerializer.Serialize(new ServeConfiguration
        {
            Repositories = new Dictionary<string, HostedRepository> { ["song"] = new() { Path = Remote, AllowPush = true } }
        }));
        _factory = new TestFactory(policy);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task FreshPullPreview_UsesNewRemoteTipWithMissingOrStaleTracking_AndWritesNothing(bool ssh, bool removeTracking)
    {
        var (local, first) = await PrepareAsync(ssh);
        var second = await CommitAsync("second", first);
        await PublishAsync();
        if (removeTracking) File.Delete(Path.Combine(local, ".dawvc", "refs", "remotes", "backup", "main"));
        RemoveTemporaryDirectories(local);
        var stale = Path.Combine(local, ".dawvc", "incoming", "keep-old-session");
        Directory.CreateDirectory(stale);
        File.WriteAllText(Path.Combine(stale, "evidence"), "preserve");
        Directory.SetLastWriteTimeUtc(stale, DateTime.UtcNow.AddDays(-2));
        var before = Snapshot(local);
        var remoteBefore = Snapshot(Remote);
        var progress = new RecordingProgress();
        var preview = await Pull().ExecuteAsync(new PullRequest(local, DryRun: true), default, progress);
        preview.NewCommit.Should().Be(second);
        preview.State.Should().Be(SyncAncestryState.FastForward);
        preview.ObjectsFetched.Should().Be(3);
        preview.IsDryRun.Should().BeTrue();
        Snapshot(local).Should().BeEquivalentTo(before);
        Snapshot(Remote).Should().BeEquivalentTo(remoteBefore);
        progress.Reports.Count(p => p.Stage == OperationStage.Completed).Should().Be(1);
        progress.Reports.Should().NotContain(p => p.Stage == OperationStage.PromotingObjects || p.Stage == OperationStage.UpdatingReferences || p.Stage == OperationStage.StagingFiles);
        var actual = await Pull().ExecuteAsync(new PullRequest(local));
        actual.State.Should().Be(preview.State);
        actual.NewCommit.Should().Be(preview.NewCommit);
        File.ReadAllText(Path.Combine(local, "Song.flp")).Should().Be("second");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FetchPreview_ReportsPlannedRefsWithoutPromotingOrUpdatingThem(bool ssh)
    {
        var (local, first) = await PrepareAsync(ssh);
        var second = await CommitAsync("second", first);
        await PublishAsync();
        RemoveTemporaryDirectories(local);
        var before = Snapshot(local);
        var preview = await Fetch().ExecuteAsync(new FetchRequest(local, "backup", "main", true));
        preview.UpdatedRefs.Should().ContainSingle().Which.NewCommit.Should().Be(second);
        preview.UpdatedRefs[0].OldCommit.Should().Be(first);
        preview.Preview!.Parents[second].Should().Contain(first);
        preview.ObjectsFetched.Should().Be(3);
        Snapshot(local).Should().BeEquivalentTo(before);
        using var context = Context(local);
        context.GetRemoteTrackingCommit("backup", BranchName.Main).Should().Be(first);
        context.ObjectStore.Exists(second.Value).Should().BeFalse();
    }

    [Fact]
    public async Task Preview_DoesNotAccessDirtyWorkspaceOrCorruptStagingIndex()
    {
        var (local, first) = await PrepareAsync(false);
        await CommitAsync("second", first);
        await PublishAsync();
        File.WriteAllText(Path.Combine(local, "Song.flp"), "unsaved local work");
        File.WriteAllText(Path.Combine(local, ".dawvc", "index.db"), "not a database");
        RemoveTemporaryDirectories(local);
        var before = Snapshot(local);
        var result = await Pull().ExecuteAsync(new PullRequest(local, Force: true, DryRun: true));
        result.State.Should().Be(SyncAncestryState.FastForward);
        result.RecoveryDirectory.Should().BeNull();
        Snapshot(local).Should().BeEquivalentTo(before);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CorruptMetadata_IsRejectedWithoutWritingLocalState(bool ssh)
    {
        var (local, first) = await PrepareAsync(ssh);
        var second = await CommitAsync("second", first);
        await PublishAsync();
        var hex = second.ToString();
        var path = Path.Combine(Remote, "objects", hex[..2], hex[2..]);
        var bytes = File.ReadAllBytes(path); bytes[^1] ^= 1; File.WriteAllBytes(path, bytes);
        RemoveTemporaryDirectories(local);
        var before = Snapshot(local);
        await Assert.ThrowsAnyAsync<InvalidEnvelopeException>(() => Pull().ExecuteAsync(new PullRequest(local, DryRun: true)));
        Snapshot(local).Should().BeEquivalentTo(before);
    }

    [Fact]
    public async Task CancellationDuringPreview_DoesNotLeaveQuarantineOrChangeFiles()
    {
        var (local, first) = await PrepareAsync(false);
        await CommitAsync("second", first);
        await PublishAsync();
        RemoveTemporaryDirectories(local);
        var before = Snapshot(local);
        using var cancelled = new CancellationTokenSource();
        var progress = new RecordingProgress(p => { if (p.Stage == OperationStage.TraversingHistory) cancelled.Cancel(); });
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Pull().ExecuteAsync(new PullRequest(local, DryRun: true), cancelled.Token, progress));
        Snapshot(local).Should().BeEquivalentTo(before);
        progress.Reports.Should().NotContain(p => p.Stage == OperationStage.Completed);
    }

    [Fact]
    public async Task Preview_SnapshotDoesNotReserveRemoteTip_ForLaterActualPull()
    {
        var (local, first) = await PrepareAsync(false);
        var second = await CommitAsync("second", first);
        await PublishAsync();
        var preview = await Pull().ExecuteAsync(new PullRequest(local, DryRun: true));
        var third = await CommitAsync("third", second);
        await PublishAsync();
        preview.NewCommit.Should().Be(second);
        (await Pull().ExecuteAsync(new PullRequest(local))).NewCommit.Should().Be(third);
        File.ReadAllText(Path.Combine(local, "Song.flp")).Should().Be("third");
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Preview_ClassifiesUpToDateAndLocalAheadWithoutWriting(bool ssh, bool ahead)
    {
        var (local, first) = await PrepareAsync(ssh);
        if (ahead) await CommitAtAsync(local, "local second", first);
        RemoveTemporaryDirectories(local);
        var before = Snapshot(local);
        var result = await Pull().ExecuteAsync(new PullRequest(local, DryRun: true));
        result.State.Should().Be(ahead ? SyncAncestryState.LocalAhead : SyncAncestryState.UpToDate);
        result.ObjectsFetched.Should().Be(0);
        Snapshot(local).Should().BeEquivalentTo(before);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DivergedPreview_IsRejectedWithoutCachingFetchedMetadata(bool ssh)
    {
        var (local, first) = await PrepareAsync(ssh);
        await CommitAtAsync(local, "local second", first);
        await CommitAsync("remote second", first);
        await PublishAsync();
        RemoveTemporaryDirectories(local);
        var before = Snapshot(local);
        await Assert.ThrowsAsync<RejectedNonFastForwardException>(() => Pull().ExecuteAsync(new PullRequest(local, DryRun: true)));
        Snapshot(local).Should().BeEquivalentTo(before);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UnbornLocalPreview_DoesNotCreateObjectsOrAnIndex(bool ssh)
    {
        var url = ssh ? "dawvc://test@host/song" : Remote;
        using (var source = Context(Source)) source.SaveRemote(new RemoteConfig(new RemoteName("backup"), url));
        var first = await CommitAsync("first");
        await PublishAsync();
        var local = Path.Combine(_root, "unborn");
        using (var context = Context(local))
        {
            using var source = Context(Source);
            context.SaveConfig(source.LoadConfig());
            context.SetCurrentBranch(BranchName.Main);
            context.SaveRemote(new RemoteConfig(new RemoteName("backup"), url));
            context.SetUpstream(BranchName.Main, "backup", BranchName.Main);
        }
        var before = Snapshot(local);
        var result = await Pull().ExecuteAsync(new PullRequest(local, DryRun: true));
        result.NewCommit.Should().Be(first);
        result.State.Should().Be(SyncAncestryState.FastForward);
        Directory.Exists(Path.Combine(local, ".dawvc", "objects")).Should().BeFalse();
        File.Exists(Path.Combine(local, ".dawvc", "index.db")).Should().BeFalse();
        Snapshot(local).Should().BeEquivalentTo(before);
    }

    [Fact]
    public async Task CliPreviews_ShowFreshTipAndPlannedRefsWithoutWrites()
    {
        var (local, first) = await PrepareAsync(false);
        var second = await CommitAsync("second", first);
        await PublishAsync();
        RemoveTemporaryDirectories(local);
        var before = Snapshot(local);
        foreach (var command in new[] { "fetch", "pull" })
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet")
                { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, RedirectStandardInput = true }
            };
            process.StartInfo.ArgumentList.Add(typeof(DawVcs.Cli.Program).Assembly.Location);
            process.StartInfo.ArgumentList.Add(command);
            if (command == "fetch") process.StartInfo.ArgumentList.Add("backup");
            process.StartInfo.ArgumentList.Add("--dry-run");
            process.StartInfo.ArgumentList.Add("--dir"); process.StartInfo.ArgumentList.Add(local);
            process.Start().Should().BeTrue();
            var output = process.StandardOutput.ReadToEndAsync();
            var error = process.StandardError.ReadToEndAsync();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            try { await process.WaitForExitAsync(timeout.Token); }
            finally { if (!process.HasExited) { process.Kill(true); process.WaitForExit(); } }
            process.ExitCode.Should().Be(0, await error);
            (await output).Should().Contain(second.ToString()[..8]);
            if (command == "fetch") (await output).Should().Contain("Would update");
            Snapshot(local).Should().BeEquivalentTo(before);
        }
    }

    [Fact]
    public async Task KilledMappedPull_RecoversActiveBranchWithoutRevertingFetchedTracking()
    {
        var (local, first) = await PrepareAsync(false);
        var second = await CommitAsync("second", first);
        await PublishAsync();
        var mix = BranchName.Parse("mix");
        using (var context = Context(local))
        {
            context.CreateBranch(mix, first);
            context.SetCurrentBranch(mix);
        }
        new RemoteUseCase(Context).TrackRemote(new TrackRemoteRequest(local, "backup", "main"));
        var start = new ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet")
        { UseShellExecute = false, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var arg in new[] { typeof(DawVcs.RecoveryCrashHost.Program).Assembly.Location, "pull", local, "refs-updated" })
            start.ArgumentList.Add(arg);
        using var child = Process.Start(start)!;
        var output = child.StandardOutput.ReadToEndAsync();
        var error = child.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        try { await child.WaitForExitAsync(timeout.Token); }
        finally { if (!child.HasExited) { child.Kill(true); await child.WaitForExitAsync(CancellationToken.None); } }
        child.ExitCode.Should().NotBe(0, await output + await error);
        File.ReadAllText(Path.Combine(local, ".dawvc", "killed-at")).Should().Be("refs-updated");
        (await WorkspaceRecoveryUseCase.InspectAsync(local)).Should().ContainSingle().Which.State.Should().Be("Prepared");
        (await new WorkspaceRecoveryUseCase(Context).RollbackAsync(local)).RolledBack.Should().Be(1);
        using var inspect = Context(local);
        inspect.GetCurrentBranch().Should().Be(mix);
        inspect.GetBranchCommit(mix).Should().Be(first);
        inspect.GetBranchCommit(BranchName.Main).Should().Be(first);
        inspect.GetRemoteTrackingCommit("backup", BranchName.Main).Should().Be(second);
        File.ReadAllText(Path.Combine(local, "Song.flp")).Should().Be("first");
        WorkspaceRecoveryGuard.HasPending(local).Should().BeFalse();
    }

    [Fact]
    public async Task PushPreview_DoesNotCreateOperationLockOrStagingIndex()
    {
        using (var context = Context(Source)) context.SaveRemote(new RemoteConfig(new RemoteName("backup"), Remote));
        await CommitAsync("first");
        var before = Snapshot(Source);
        var hostBefore = Snapshot(Remote);
        var preview = await new PushUseCase(Context, _factory).ExecuteAsync(new PushRequest(Source, "backup", DryRun: true));
        preview.IsDryRun.Should().BeTrue();
        Snapshot(Source).Should().BeEquivalentTo(before);
        Snapshot(Remote).Should().BeEquivalentTo(hostBefore);
        File.Exists(Path.Combine(Source, ".dawvc", "operation.lock")).Should().BeFalse();
        File.Exists(Path.Combine(Source, ".dawvc", "index.db")).Should().BeFalse();
        await PublishAsync();
    }

    [Theory]
    [InlineData(false, MergeSnapshotChoice.Local)]
    [InlineData(false, MergeSnapshotChoice.Incoming)]
    [InlineData(true, MergeSnapshotChoice.Local)]
    [InlineData(true, MergeSnapshotChoice.Incoming)]
    public async Task MappedPullPreviewAndPublication_FollowMergedSecondParentAndPreserveOtherLocalBranch(bool ssh, MergeSnapshotChoice choice)
    {
        var (local, first) = await PrepareAsync(ssh);
        var left = await CommitAsync("left", first);
        File.WriteAllText(Path.Combine(Source, "Song.flp"), "left");
        await PublishAsync();
        var right = await CommitAtAsync(local, "right", first);
        File.WriteAllText(Path.Combine(local, "Song.flp"), "right");
        await Fetch().ExecuteAsync(new FetchRequest(local, "backup"));
        var merged = await new MergeUseCase(Context, new CheckoutUseCase(Context)).ExecuteAsync(
            new MergeRequest(local, "backup/main", choice, "combine histories"));
        merged.LocalParent.Should().Be(right);
        merged.IncomingParent.Should().Be(left);
        await new PushUseCase(Context, _factory).ExecuteAsync(new PushRequest(local));
        var mix = BranchName.Parse("mix");
        using (var context = Context(Source))
        {
            context.CreateBranch(mix, left);
            context.SetCurrentBranch(mix);
        }
        new RemoteUseCase(Context).TrackRemote(new TrackRemoteRequest(Source, "backup", "main"));
        var before = Snapshot(Source);
        var hostBefore = Snapshot(Remote);
        var preview = await Pull().ExecuteAsync(new PullRequest(Source, DryRun: true));
        preview.State.Should().Be(SyncAncestryState.FastForward);
        preview.Branch.Should().Be(mix);
        preview.NewCommit.Should().Be(merged.CommitId);
        Snapshot(Source).Should().BeEquivalentTo(before);
        Snapshot(Remote).Should().BeEquivalentTo(hostBefore);
        var actual = await Pull().ExecuteAsync(new PullRequest(Source));
        actual.NewCommit.Should().Be(preview.NewCommit);
        using var inspect = Context(Source);
        inspect.GetCurrentBranch().Should().Be(mix);
        inspect.GetBranchCommit(mix).Should().Be(merged.CommitId);
        inspect.GetBranchCommit(BranchName.Main).Should().Be(left);
        File.ReadAllText(Path.Combine(Source, "Song.flp")).Should().Be(choice == MergeSnapshotChoice.Local ? "right" : "left");
    }

    private FetchUseCase Fetch() => new(Context, _factory, p => new QuarantineObjectStore(p));
    private PullUseCase Pull() => new(Context, Fetch(), new CheckoutUseCase(Context));
    private Task<PushResult> PublishAsync() => new PushUseCase(Context, _factory).ExecuteAsync(new PushRequest(Source, "backup"));

    private async Task<(string Local, CommitId First)> PrepareAsync(bool ssh)
    {
        var url = ssh ? "dawvc://test@host/song" : Remote;
        using (var context = Context(Source)) context.SaveRemote(new RemoteConfig(new RemoteName("backup"), url));
        var first = await CommitAsync("first");
        await PublishAsync();
        var local = Path.Combine(_root, "clone");
        await new CloneUseCase(Context, _factory, Fetch(), new CheckoutUseCase(Context)).ExecuteAsync(new CloneRequest(url, local));
        var remotes = new RemoteUseCase(Context);
        remotes.AddRemote(new RemoteAddRequest(local, "backup", url));
        remotes.TrackRemote(new TrackRemoteRequest(local, "backup", "main"));
        // Establish old backup tracking without involving the new preview path.
        await Fetch().ExecuteAsync(new FetchRequest(local, "backup"));
        return (local, first);
    }

    private Task<CommitId> CommitAsync(string text, CommitId? parent = null) => CommitAtAsync(Source, text, parent);

    private static async Task<CommitId> CommitAtAsync(string root, string text, CommitId? parent = null)
    {
        using var context = Context(root);
        using var payload = new MemoryStream(Encoding.UTF8.GetBytes(text));
        var hash = await context.ObjectStore.WriteBlobAsync(payload);
        var snapshot = new ProjectSnapshot(ProjectArtifact.CreateSingleFile("Synthetic", new ArtifactPath("Song.flp"), hash, payload.Length), DateTimeOffset.UtcNow);
        await context.ObjectStore.WriteObjectAsync(ObjectType.Snapshot, snapshot.ToCanonicalBytes());
        var commit = new Commit(parent.HasValue ? [parent.Value] : [], snapshot.Id, "test", DateTimeOffset.UtcNow, text);
        await context.ObjectStore.WriteObjectAsync(ObjectType.Commit, commit.ToCanonicalBytes());
        await context.UpdateBranchCommitAsync(BranchName.Main, commit.Id);
        return commit.Id;
    }

    private static void RemoveTemporaryDirectories(string local)
    {
        var temporary = Path.Combine(local, ".dawvc", "objects", ".tmp");
        if (Directory.Exists(temporary)) Directory.Delete(temporary, true);
    }

    // Include directories and mtimes, not only file bytes: even empty .tmp recreation must fail.
    private static Dictionary<string, string> Snapshot(string root) => Directory.EnumerateFileSystemEntries(root, "*", SearchOption.AllDirectories)
        .ToDictionary(p => Path.GetRelativePath(root, p), p => Directory.Exists(p)
            ? "directory:" + Directory.GetLastWriteTimeUtc(p).Ticks
            : File.GetLastWriteTimeUtc(p).Ticks + ":" + Convert.ToBase64String(File.ReadAllBytes(p)));

    private sealed class RecordingProgress(Action<OperationProgress>? callback = null) : IProgress<OperationProgress>
    {
        public List<OperationProgress> Reports { get; } = [];
        public void Report(OperationProgress value) { Reports.Add(value); callback?.Invoke(value); }
    }

    private sealed class TestFactory(string policy) : IRemoteTransportFactory
    {
        public IRemoteTransport CreateTransport(string url)
        {
            if (!url.StartsWith("dawvc://", StringComparison.Ordinal)) return new DefaultRemoteTransportFactory().CreateTransport(url);
            return new SshRemoteTransport(url, () =>
            {
                var start = new ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet")
                { UseShellExecute = false, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
                start.ArgumentList.Add(typeof(DawVcs.Cli.Program).Assembly.Location);
                start.ArgumentList.Add("serve"); start.ArgumentList.Add("--config"); start.ArgumentList.Add(policy);
                return start;
            });
        }
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);
}
