using System.Diagnostics;
using System.Text;
using System.Text.Json;

using DawVcs.Application.Checkouts;
using DawVcs.Application.Remotes;
using DawVcs.Domain.Artifacts;
using DawVcs.Domain.Common;
using DawVcs.Domain.Configuration;
using DawVcs.Domain.Entities;
using DawVcs.Domain.Remotes;
using DawVcs.Domain.Storage;
using DawVcs.Infrastructure.Configuration;
using DawVcs.Infrastructure.Remotes;
using DawVcs.Infrastructure.Remotes.Ssh;
using DawVcs.Infrastructure.Repositories;
using DawVcs.Infrastructure.Storage;

using FluentAssertions;

using Xunit;

namespace DawVcs.EndToEndTests;

[Collection("PerformanceNonParallel")]
public sealed class SyncWorkflowTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "dawvc-upstream-" + Guid.NewGuid().ToString("N"));
    private readonly TestTransportFactory _factory;
    private string Local => Path.Combine(_root, "local");
    private string Remote => Path.Combine(_root, "remote");
    private const string SshUrl = "dawvc://test@host/song";
    private static FileSystemRepositoryContext Context(string path) => new(path);

    public SyncWorkflowTests()
    {
        using var context = Context(Local);
        var config = new RepositoryConfig(RepositoryId.New(), "Sync workflow", new ArtifactPath("Song.flp"));
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
        _factory = new TestTransportFactory(policy);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task PushAndPull_RespectBackupUpstreamEvenWithExplicitMatchingRemote(bool ssh, bool explicitRemote)
    {
        var (clone, first) = await PrepareAsync(ssh);
        var second = await CommitAsync("second", first);
        new RemoteUseCase(Context).TrackRemote(new TrackRemoteRequest(Local, "backup", "published"));
        var pushed = await new PushUseCase(Context, _factory).ExecuteAsync(new PushRequest(Local, explicitRemote ? "backup" : null));
        pushed.RemoteBranch.Value.Should().Be("published");
        ReadRemoteTip("main").Should().Be(first);

        var fetch = new FetchUseCase(Context, _factory, p => new QuarantineObjectStore(p));
        var pulled = await new PullUseCase(Context, fetch, new CheckoutUseCase(Context)).ExecuteAsync(
            new PullRequest(clone, explicitRemote ? "backup" : null));
        pulled.NewCommit.Should().Be(second);
        pulled.RemoteName.Should().Be("backup");
        pulled.Branch.Should().Be(BranchName.Main);
        File.ReadAllText(Path.Combine(clone, "Song.flp")).Should().Be("second");
        using var context = Context(clone);
        context.GetCurrentBranch().Should().Be(BranchName.Main);
        context.GetBranchCommit(BranchName.Main).Should().Be(second);
        context.GetBranchCommit(new BranchName("published")).Should().Be(first);
        context.GetRemoteTrackingCommit("backup", new BranchName("published")).Should().Be(second);
        context.GetRemote("origin").Should().BeNull("Pull must fetch backup, not an implicit origin");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExplicitPullBranch_UpdatesCurrentBranchAndPreservesOtherLocalBranch(bool ssh)
    {
        var (clone, first) = await PrepareAsync(ssh);
        var second = await CommitAsync("second", first);
        new RemoteUseCase(Context).TrackRemote(new TrackRemoteRequest(Local, "backup", "published"));
        await new PushUseCase(Context, _factory).ExecuteAsync(new PushRequest(Local));
        new RemoteUseCase(Context).TrackRemote(new TrackRemoteRequest(clone, "backup", "main"));
        var fetch = new FetchUseCase(Context, _factory, p => new QuarantineObjectStore(p));
        var result = await new PullUseCase(Context, fetch, new CheckoutUseCase(Context)).ExecuteAsync(new PullRequest(clone, "backup", "published"));
        result.Branch.Should().Be(BranchName.Main);
        using var context = Context(clone);
        context.GetBranchCommit(BranchName.Main).Should().Be(second);
        context.GetBranchCommit(new BranchName("published")).Should().Be(first);
        context.GetCurrentBranch().Should().Be(BranchName.Main);
    }

    [Fact]
    public async Task ExplicitDifferentRemote_UsesCurrentNameInsteadOfAnotherRemotesMapping()
    {
        var (clone, first) = await PrepareAsync(false);
        using (var context = Context(Local)) context.SaveRemote(new RemoteConfig(new RemoteName("other"), Remote));
        new RemoteUseCase(Context).TrackRemote(new TrackRemoteRequest(Local, "backup", "published"));
        var second = await CommitAsync("second", first);
        var result = await new PushUseCase(Context, _factory).ExecuteAsync(new PushRequest(Local, "other"));
        result.RemoteBranch.Should().Be(BranchName.Main);
        ReadRemoteTip("main").Should().Be(second);
        ReadRemoteTip("published").Should().Be(first);
        new RemoteUseCase(Context).AddRemote(new RemoteAddRequest(clone, "other", Remote));
        var fetch = new FetchUseCase(Context, _factory, p => new QuarantineObjectStore(p));
        var pulled = await new PullUseCase(Context, fetch, new CheckoutUseCase(Context)).ExecuteAsync(new PullRequest(clone, "other"));
        pulled.NewCommit.Should().Be(second);
        using var inspect = Context(clone);
        inspect.GetUpstream(BranchName.Main).Should().Be(("backup", new BranchName("published")));
    }

    [Fact]
    public async Task CliTrack_AllowsUnbornAndFutureBranchesWithoutContactingHost()
    {
        using (var context = Context(Local)) context.SaveRemote(new RemoteConfig(new RemoteName("backup"), Path.Combine(_root, "offline-host")));
        (await DawVcs.Cli.Program.Main(["remote", "track", "backup", "future/mix", "--dir", Local])).Should().Be(0);
        using var inspect = Context(Local);
        inspect.GetUpstream(BranchName.Main).Should().Be(("backup", new BranchName("future/mix")));
        inspect.GetRemote("backup")!.Url.Should().EndWith("offline-host");
        File.Exists(Path.Combine(Local, ".dawvc", "refs", "heads", "main")).Should().BeFalse();
    }

    [Fact]
    public async Task CliTrack_NamedLocalBranchPreservesHeadAndOtherUpstreams()
    {
        using (var context = Context(Local)) context.SaveRemote(new RemoteConfig(new RemoteName("backup"), Remote));
        var first = await CommitAsync("first");
        using (var context = Context(Local))
        {
            context.CreateBranch(new BranchName("mix"), first);
            context.SetUpstream(BranchName.Main, "backup", BranchName.Main);
        }
        (await DawVcs.Cli.Program.Main(["remote", "track", "backup", "published", "--local-branch", "mix", "--dir", Local])).Should().Be(0);
        using var inspect = Context(Local);
        inspect.GetCurrentBranch().Should().Be(BranchName.Main);
        inspect.GetUpstream(BranchName.Main).Should().Be(("backup", BranchName.Main));
        inspect.GetUpstream(new BranchName("mix")).Should().Be(("backup", new BranchName("published")));
        inspect.GetRemote("backup")!.Url.Should().Be(Remote);
    }

    [Fact]
    public async Task CliTrack_PendingRecoveryRefusesWithoutChangingUpstreamSettings()
    {
        using (var context = Context(Local)) context.SaveRemote(new RemoteConfig(new RemoteName("backup"), Remote));
        var path = Path.Combine(Local, ".dawvc", "remotes.json");
        var before = File.ReadAllBytes(path);
        Directory.CreateDirectory(Path.Combine(Local, ".dawvc", "transactions", "pending"));
        (await DawVcs.Cli.Program.Main(["remote", "track", "backup", "published", "--dir", Local])).Should().Be(10);
        File.ReadAllBytes(path).Should().Equal(before);
    }

    [Theory]
    [InlineData("missing", "main", null)]
    [InlineData("backup", "../invalid", null)]
    [InlineData("backup", "main", "missing")]
    public void InvalidTracking_PreservesExistingSettings(string remote, string branch, string? local)
    {
        using (var context = Context(Local)) context.SaveRemote(new RemoteConfig(new RemoteName("backup"), Remote));
        var path = Path.Combine(Local, ".dawvc", "remotes.json");
        var before = File.ReadAllBytes(path);
        Action configure = () => new RemoteUseCase(Context).TrackRemote(new TrackRemoteRequest(Local, remote, branch, local));
        configure.Should().Throw<ArgumentException>();
        File.ReadAllBytes(path).Should().Equal(before);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public async Task InvalidExplicitRemote_DoesNotSilentlyFallBackToUpstream(string invalid)
    {
        using (var context = Context(Local)) context.SaveRemote(new RemoteConfig(new RemoteName("backup"), Remote));
        await CommitAsync("first");
        new RemoteUseCase(Context).TrackRemote(new TrackRemoteRequest(Local, "backup", "published"));
        await Assert.ThrowsAsync<ArgumentException>(() => new PushUseCase(Context, _factory).ExecuteAsync(new PushRequest(Local, invalid)));
        var fetch = new FetchUseCase(Context, _factory, p => new QuarantineObjectStore(p));
        await Assert.ThrowsAsync<ArgumentException>(() => new PullUseCase(Context, fetch, new CheckoutUseCase(Context)).ExecuteAsync(new PullRequest(Local, invalid)));
        Directory.GetFileSystemEntries(Path.Combine(Remote, "refs", "heads")).Should().BeEmpty();
    }

    [Fact]
    public void PreCancelledTracking_DoesNotCreateAContext()
    {
        var remotes = new RemoteUseCase(_ => throw new InvalidOperationException("Must not create a context"));
        Action configure = () => remotes.TrackRemote(new TrackRemoteRequest(Local, "backup", "main"), new CancellationToken(true));
        configure.Should().Throw<OperationCanceledException>();
    }

    private async Task<(string Clone, CommitId First)> PrepareAsync(bool ssh)
    {
        var url = ssh ? SshUrl : Remote;
        using (var context = Context(Local)) context.SaveRemote(new RemoteConfig(new RemoteName("backup"), url));
        var first = await CommitAsync("first");
        var push = new PushUseCase(Context, _factory);
        await push.ExecuteAsync(new PushRequest(Local, "backup", "main"));
        using (var context = Context(Local)) context.CreateBranch(new BranchName("published"), first);
        await push.ExecuteAsync(new PushRequest(Local, "backup", "published"));
        var clone = Path.Combine(_root, "clone");
        var fetch = new FetchUseCase(Context, _factory, p => new QuarantineObjectStore(p));
        await new CloneUseCase(Context, _factory, fetch, new CheckoutUseCase(Context)).ExecuteAsync(new CloneRequest(url, clone));
        var remotes = new RemoteUseCase(Context);
        remotes.RemoveRemote(new RemoteRemoveRequest(clone, "origin"));
        remotes.AddRemote(new RemoteAddRequest(clone, "backup", url));
        remotes.TrackRemote(new TrackRemoteRequest(clone, "backup", "published"));
        using (var context = Context(clone)) context.CreateBranch(new BranchName("published"), first);
        return (clone, first);
    }

    private CommitId ReadRemoteTip(string branch) => CommitId.Parse(File.ReadAllText(Path.Combine(Remote, "refs", "heads", branch)).Trim());

    private async Task<CommitId> CommitAsync(string text, CommitId? parent = null)
    {
        using var context = Context(Local);
        using var payload = new MemoryStream(Encoding.UTF8.GetBytes(text));
        var hash = await context.ObjectStore.WriteBlobAsync(payload);
        var snapshot = new ProjectSnapshot(ProjectArtifact.CreateSingleFile("Synthetic", new ArtifactPath("Song.flp"), hash, payload.Length), DateTimeOffset.UtcNow);
        await context.ObjectStore.WriteObjectAsync(ObjectType.Snapshot, snapshot.ToCanonicalBytes());
        var commit = new Commit(parent.HasValue ? [parent.Value] : [], snapshot.Id, "test", DateTimeOffset.UtcNow, text);
        await context.ObjectStore.WriteObjectAsync(ObjectType.Commit, commit.ToCanonicalBytes());
        await context.UpdateBranchCommitAsync(BranchName.Main, commit.Id);
        return commit.Id;
    }

    private sealed class TestTransportFactory(string policy) : IRemoteTransportFactory
    {
        public IRemoteTransport CreateTransport(string url)
        {
            if (!url.StartsWith("dawvc://", StringComparison.Ordinal)) return new DefaultRemoteTransportFactory().CreateTransport(url);
            return new SshRemoteTransport(url, () =>
            {
                var start = new ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet")
                {
                    UseShellExecute = false,
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };
                start.ArgumentList.Add(typeof(DawVcs.Cli.Program).Assembly.Location);
                start.ArgumentList.Add("serve"); start.ArgumentList.Add("--config"); start.ArgumentList.Add(policy);
                return start;
            });
        }
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);
}
