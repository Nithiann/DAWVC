using System.Diagnostics;
using System.Text;
using System.Text.Json;

using DawVcs.Application.Checkouts;
using DawVcs.Application.Common;
using DawVcs.Application.Exceptions;
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

public sealed class BareProvisioningTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "dawvc-bare-" + Guid.NewGuid().ToString("N"));
    private readonly RepositoryConfig _config = new(RepositoryId.New(), "Bare song", new ArtifactPath("Song.flp"));
    private string Source => Path.Combine(_root, "source");
    private string Target => Path.Combine(_root, "host");
    private string Policy => Path.Combine(_root, "serve.json");
    private static FileSystemRepositoryContext Context(string path) => new(path);
    private LocalProcessFactory Factory => new LocalProcessFactory(Policy);
    private FetchUseCase Fetch => new(Context, Factory, path => new QuarantineObjectStore(path));
    private BareRepositoryUseCase Provision => new(Context, Factory, Fetch);
    private CloneUseCase Clone => new(Context, Factory, Fetch, new CheckoutUseCase(Context));
    private string SourceUrl(bool ssh) => ssh ? "dawvc://test@host/source" : Source;
    private string TargetUrl(bool ssh) => ssh ? "dawvc://test@host/host" : Target;

    public BareProvisioningTests()
    {
        Directory.CreateDirectory(Source);
        using var context = Context(Source);
        context.SaveConfig(_config);
        context.SetCurrentBranch(BranchName.Main);
        File.WriteAllText(Policy, JsonSerializer.Serialize(new ServeConfiguration
        {
            Repositories = new Dictionary<string, HostedRepository>
            {
                ["source"] = new() { Path = Source, AllowPush = false },
                ["host"] = new() { Path = Target, AllowPush = true }
            }
        }));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InitMatchingReceiver_FirstPushAndNormalCloneRestoreExactBytes(bool ssh)
    {
        var commit = await CommitAsync("first project");
        var before = Snapshot(Source);
        var progress = new RecordingProgress();
        var initialized = await Provision.ExecuteAsync(new BareRepositoryRequest(SourceUrl(ssh), Target), default, progress);
        initialized.Configuration.Should().BeEquivalentTo(_config);
        initialized.ObjectsFetched.Should().Be(0);
        initialized.HeadCommit.Should().BeNull();
        AssertBareLayout();
        Directory.GetFiles(Path.Combine(Target, "refs", "heads"), "*", SearchOption.AllDirectories).Should().BeEmpty();
        Snapshot(Source).Should().BeEquivalentTo(before);
        progress.Reports.Count(p => p.Stage == OperationStage.Completed).Should().Be(1);
        using (var context = Context(Source)) context.SaveRemote(new RemoteConfig(RemoteName.Origin, TargetUrl(ssh)));
        (await new PushUseCase(Context, Factory).ExecuteAsync(new PushRequest(Source))).NewCommit.Should().Be(commit);
        var copy = Path.Combine(_root, "collaborator");
        (await Clone.ExecuteAsync(new CloneRequest(TargetUrl(ssh), copy))).HeadCommit.Should().Be(commit);
        File.ReadAllText(Path.Combine(copy, "Song.flp")).Should().Be("first project");
        using var cloned = Context(copy);
        cloned.LoadConfig().RepositoryId.Should().Be(_config.RepositoryId);
        AssertNoStaging();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BareClone_PublishesAllBranchesWithoutWorkspaceOrIndex(bool ssh)
    {
        var first = await CommitAsync("first");
        var second = await CommitAsync("second", first);
        using (var context = Context(Source)) context.CreateBranch(BranchName.Parse("mix/alternative"), first);
        var before = Snapshot(Source);
        var progress = new RecordingProgress();
        var result = await Clone.ExecuteAsync(new CloneRequest(SourceUrl(ssh), Target, Bare: true), default, progress);
        result.HeadCommit.Should().Be(second);
        result.ObjectsFetched.Should().Be(6);
        result.MaterializedFiles.Should().BeEmpty();
        result.DoctorSummary.Should().BeNull();
        AssertBareLayout();
        File.ReadAllText(Path.Combine(Target, "refs", "heads", "main")).Trim().Should().Be(second.ToString());
        File.ReadAllText(Path.Combine(Target, "refs", "heads", "mix", "alternative")).Trim().Should().Be(first.ToString());
        File.ReadAllText(Path.Combine(Target, "HEAD")).Trim().Should().Be("ref: refs/heads/main");
        using var receiver = Factory.CreateTransport(TargetUrl(ssh));
        (await receiver.ListRefsAsync()).Should().HaveCount(2);
        Snapshot(Source).Should().BeEquivalentTo(before);
        progress.Reports.Count(p => p.Stage == OperationStage.Completed).Should().Be(1);
        var copy = Path.Combine(_root, "working-copy");
        await Clone.ExecuteAsync(new CloneRequest(TargetUrl(ssh), copy));
        File.ReadAllText(Path.Combine(copy, "Song.flp")).Should().Be("second");
        AssertNoStaging();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EmptySource_CanBeBareClonedAndClonedBack(bool ssh)
    {
        var result = await Clone.ExecuteAsync(new CloneRequest(SourceUrl(ssh), Target, Bare: true));
        result.HeadCommit.Should().BeNull();
        result.ObjectsFetched.Should().Be(0);
        AssertBareLayout();
        var copy = Path.Combine(_root, "working-copy");
        (await Clone.ExecuteAsync(new CloneRequest(TargetUrl(ssh), copy))).HeadCommit.Should().BeNull();
        using var context = Context(copy);
        context.GetUpstream(BranchName.Main).Should().Be(("origin", BranchName.Main));
        File.Exists(Path.Combine(copy, "Song.flp")).Should().BeFalse();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BareClone_CorruptionNeverPublishesAndPreservesEmptyDestination(bool ssh)
    {
        var commit = await CommitAsync("first");
        using (var context = Context(Source))
        {
            var store = (LooseObjectStore)context.ObjectStore;
            File.WriteAllBytes(store.GetObjectPath(commit.Value), [0, 1, 2]);
        }
        Directory.CreateDirectory(Target);
        var action = () => Clone.ExecuteAsync(new CloneRequest(SourceUrl(ssh), Target, Bare: true));
        await action.Should().ThrowAsync<InvalidEnvelopeException>();
        Directory.Exists(Target).Should().BeTrue();
        Directory.EnumerateFileSystemEntries(Target).Should().BeEmpty();
        AssertNoStaging();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CancellationBeforePublication_NeverExposesPartialRepository(bool copyHistory)
    {
        await CommitAsync("first");
        using var cancellation = new CancellationTokenSource();
        var progress = new RecordingProgress(value =>
        {
            // At least one staging directory exists; cancellation must clean it and not publish.
            if (value.Stage == OperationStage.OpeningRepository) cancellation.Cancel();
        });
        var action = () => Provision.ExecuteAsync(new BareRepositoryRequest(Source, Target, copyHistory), cancellation.Token, progress);
        await action.Should().ThrowAsync<OperationCanceledException>();
        Directory.Exists(Target).Should().BeFalse();
        progress.Reports.Should().NotContain(p => p.Stage == OperationStage.Completed);
        AssertNoStaging();
    }

    [Fact]
    public async Task CancellationAtFinalPublicationBoundary_PreservesDestination()
    {
        Directory.CreateDirectory(Target);
        using var cancellation = new CancellationTokenSource();
        var progress = new RecordingProgress(p => { if (p.Stage == OperationStage.UpdatingReferences) cancellation.Cancel(); });
        await FluentActions.Awaiting(() => Provision.ExecuteAsync(new BareRepositoryRequest(Source, Target), cancellation.Token, progress))
            .Should().ThrowAsync<OperationCanceledException>();
        Directory.EnumerateFileSystemEntries(Target).Should().BeEmpty();
        AssertNoStaging();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NonemptyDestination_IsNeverReinitialized(bool copyHistory)
    {
        Directory.CreateDirectory(Target);
        File.WriteAllText(Path.Combine(Target, "keep"), "unrelated user data");
        var before = Snapshot(Target);
        await FluentActions.Awaiting(() => Provision.ExecuteAsync(new BareRepositoryRequest(Source, Target, copyHistory)))
            .Should().ThrowAsync<ArgumentException>();
        Snapshot(Target).Should().BeEquivalentTo(before);
        AssertNoStaging();
    }

    [Fact]
    public async Task DestinationCreatedDuringPreparation_IsPreserved()
    {
        var progress = new RecordingProgress(p =>
        {
            if (p.Stage != OperationStage.UpdatingReferences) return;
            Directory.CreateDirectory(Target);
            File.WriteAllText(Path.Combine(Target, "concurrent"), "keep");
        });
        await FluentActions.Awaiting(() => Provision.ExecuteAsync(new BareRepositoryRequest(Source, Target), default, progress))
            .Should().ThrowAsync<ArgumentException>();
        File.ReadAllText(Path.Combine(Target, "concurrent")).Should().Be("keep");
        AssertNoStaging();
    }

    [Fact]
    public async Task ExistingFile_IsNotReplaced()
    {
        File.WriteAllText(Target, "keep");
        await FluentActions.Awaiting(() => Provision.ExecuteAsync(new BareRepositoryRequest(Source, Target)))
            .Should().ThrowAsync<ArgumentException>();
        File.ReadAllText(Target).Should().Be("keep");
    }

    [Fact]
    public async Task ForceBareClone_IsRejectedWithoutCreatingTarget()
    {
        await FluentActions.Awaiting(() => Clone.ExecuteAsync(new CloneRequest(Source, Target, Force: true, Bare: true)))
            .Should().ThrowAsync<ArgumentException>();
        Directory.Exists(Target).Should().BeFalse();
    }

    [Fact]
    public async Task SourceIdentityChangeDuringFetch_FailsWithoutPublishing()
    {
        var changed = false;
        var progress = new RecordingProgress(p =>
        {
            if (p.Stage != OperationStage.OpeningRepository || changed) return;
            changed = true;
            using var context = Context(Source);
            context.SaveConfig(_config with { RepositoryId = RepositoryId.New() });
        });
        await FluentActions.Awaiting(() => Provision.ExecuteAsync(new BareRepositoryRequest(Source, Target, true), default, progress))
            .Should().ThrowAsync<IncompatibleRemoteRepositoryException>();
        Directory.Exists(Target).Should().BeFalse();
        AssertNoStaging();
    }

    [Fact]
    public async Task LinuxSymlinkAncestryAndDanglingDestination_AreRejected()
    {
        if (OperatingSystem.IsWindows()) return; // Creating Windows links needs separate privileges.
        var link = Path.Combine(_root, "link");
        Directory.CreateSymbolicLink(link, Source);
        await FluentActions.Awaiting(() => Provision.ExecuteAsync(new BareRepositoryRequest(Source, Path.Combine(link, "host"))))
            .Should().ThrowAsync<PathSecurityException>();
        Directory.CreateSymbolicLink(Target, Path.Combine(_root, "missing"));
        await FluentActions.Awaiting(() => Provision.ExecuteAsync(new BareRepositoryRequest(Source, Target)))
            .Should().ThrowAsync<PathSecurityException>();
        Directory.Exists(Path.Combine(Source, "host")).Should().BeFalse();
    }

    [Theory]
    [InlineData("missing-source")]
    [InlineData("invalid-config")]
    public async Task InvalidSource_DoesNotCreateDestinationOrStaging(string kind)
    {
        var source = Source;
        if (kind == "missing-source") source += "-missing";
        else File.WriteAllText(Path.Combine(Source, "dawvc.yaml"), "invalid");
        await FluentActions.Awaiting(() => Provision.ExecuteAsync(new BareRepositoryRequest(source, Target)))
            .Should().ThrowAsync<RemoteException>();
        Directory.Exists(Target).Should().BeFalse();
        AssertNoStaging();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Cli_InitAndCloneBare_ExposeWorkingHostingLayout(bool clone)
    {
        var commit = await CommitAsync("cli source");
        var args = clone ? new[] { "clone", Source, Target, "--bare" }
            : new[] { "init", "--bare", "--from", Source, "--dir", Target };
        var result = await RunCliAsync(args);
        result.Code.Should().Be(0, result.Output);
        AssertBareLayout();
        File.Exists(Path.Combine(Target, "refs", "heads", "main")).Should().Be(clone);
        if (clone) File.ReadAllText(Path.Combine(Target, "refs", "heads", "main")).Trim().Should().Be(commit.ToString());
    }

    [Theory]
    [InlineData("missing-from")]
    [InlineData("from-without-bare")]
    [InlineData("override-name")]
    [InlineData("override-primary")]
    [InlineData("force-clone")]
    public async Task Cli_InvalidBareOptions_ReturnValidationAndDoNotCreateDestination(string kind)
    {
        var args = kind switch
        {
            "missing-from" => new[] { "init", "--bare", "--dir", Target },
            "from-without-bare" => new[] { "init", "--from", Source, "--dir", Target },
            "override-name" => new[] { "init", "--bare", "--from", Source, "--name", "different", "--dir", Target },
            "override-primary" => new[] { "init", "--bare", "--from", Source, "--primary", "other.flp", "--dir", Target },
            _ => new[] { "clone", Source, Target, "--bare", "--force" }
        };
        var result = await RunCliAsync(args);
        result.Code.Should().Be(7, result.Output);
        Directory.Exists(Target).Should().BeFalse();
        AssertNoStaging();
    }

    private static async Task<(int Code, string Output)> RunCliAsync(string[] args)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet")
            { UseShellExecute = false, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true }
        };
        process.StartInfo.ArgumentList.Add(typeof(DawVcs.Cli.Program).Assembly.Location);
        foreach (var arg in args) process.StartInfo.ArgumentList.Add(arg);
        process.Start().Should().BeTrue();
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        try { await process.WaitForExitAsync(timeout.Token); }
        finally { if (!process.HasExited) { process.Kill(true); process.WaitForExit(); } }
        return (process.ExitCode, await output + await error);
    }

    [Fact]
    public async Task NestedDefaultBranchAndPolicies_ArePreservedWithoutChoosingAnotherHead()
    {
        var commit = await CommitAsync("custom default");
        var branch = BranchName.Parse("release/main");
        var config = _config with { DefaultBranch = branch, Policies = new RepositoryPolicies { MissingBundledDependencies = "warn" } };
        using (var context = Context(Source))
        {
            context.SaveConfig(config);
            context.CreateBranch(branch, commit);
            context.SetCurrentBranch(branch);
        }
        var result = await Clone.ExecuteAsync(new CloneRequest(Source, Target, Bare: true));
        result.DefaultBranch.Should().Be(branch);
        result.HeadCommit.Should().Be(commit);
        YamlRepositoryConfigManager.ReadFromDirectory(Target).Should().BeEquivalentTo(config);
        File.ReadAllText(Path.Combine(Target, "HEAD")).Trim().Should().Be("ref: refs/heads/release/main");
        var copy = Path.Combine(_root, "custom-copy");
        await Clone.ExecuteAsync(new CloneRequest(Target, copy));
        File.ReadAllText(Path.Combine(copy, "Song.flp")).Should().Be("custom default");
    }

    [Fact]
    public async Task FileUriSourceAndExistingBareDestination_AreHandledSafely()
    {
        var source = new Uri(Source + Path.DirectorySeparatorChar).AbsoluteUri;
        await Provision.ExecuteAsync(new BareRepositoryRequest(source, Target));
        var before = Snapshot(Target);
        await FluentActions.Awaiting(() => Provision.ExecuteAsync(new BareRepositoryRequest(source, Target)))
            .Should().ThrowAsync<ArgumentException>();
        Snapshot(Target).Should().BeEquivalentTo(before);
        AssertNoStaging();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CancellationDuringObjectTransfer_CleansStagingAndPreservesTarget(bool ssh)
    {
        await CommitAsync("cancel me");
        Directory.CreateDirectory(Target);
        using var cancellation = new CancellationTokenSource();
        var progress = new RecordingProgress(p => { if (p.Stage == OperationStage.TransferringObjects) cancellation.Cancel(); });
        await FluentActions.Awaiting(() => Provision.ExecuteAsync(new BareRepositoryRequest(SourceUrl(ssh), Target, true), cancellation.Token, progress))
            .Should().ThrowAsync<OperationCanceledException>();
        Directory.EnumerateFileSystemEntries(Target).Should().BeEmpty();
        progress.Reports.Should().NotContain(p => p.Stage == OperationStage.Completed);
        AssertNoStaging();
    }

    [Fact]
    public async Task DestinationUrl_IsRejectedInsteadOfCreatingMisleadingLocalDirectories()
    {
        await FluentActions.Awaiting(() => Provision.ExecuteAsync(new BareRepositoryRequest(Source, "dawvc://host/new")))
            .Should().ThrowAsync<ArgumentException>();
        await FluentActions.Awaiting(() => Clone.ExecuteAsync(new CloneRequest(Source, "dawvc://host/new", Bare: true)))
            .Should().ThrowAsync<ArgumentException>();
        AssertNoStaging();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SourceLocalStateAndDirtyWorkingFiles_AreNeitherReadNorCopied(bool copyHistory)
    {
        await CommitAsync("committed project");
        File.WriteAllText(Path.Combine(Source, ".dawvc", "index.db"), "corrupt index must remain untouched");
        File.WriteAllText(Path.Combine(Source, ".dawvc", "bindings.json"), "private machine locations");
        File.WriteAllText(Path.Combine(Source, ".dawvc", "credentials"), "private sentinel");
        File.WriteAllText(Path.Combine(Source, "Song.flp"), "uncommitted work");
        var before = Snapshot(Source);
        await Provision.ExecuteAsync(new BareRepositoryRequest(Source, Target, copyHistory));
        AssertBareLayout();
        File.Exists(Path.Combine(Target, "bindings.json")).Should().BeFalse();
        File.Exists(Path.Combine(Target, "credentials")).Should().BeFalse();
        Snapshot(Source).Should().BeEquivalentTo(before);
        AssertNoStaging();
    }

    private void AssertBareLayout()
    {
        YamlRepositoryConfigManager.ReadFromDirectory(Target).Should().BeEquivalentTo(_config);
        Directory.Exists(Path.Combine(Target, "objects")).Should().BeTrue();
        File.Exists(Path.Combine(Target, "HEAD")).Should().BeTrue();
        Directory.Exists(Path.Combine(Target, ".dawvc")).Should().BeFalse();
        File.Exists(Path.Combine(Target, "Song.flp")).Should().BeFalse();
        Directory.GetFiles(Target, "index.db*", SearchOption.AllDirectories).Should().BeEmpty();
    }

    private void AssertNoStaging() => Directory.GetDirectories(_root, ".dawvc-provision-*").Should().BeEmpty();

    private async Task<CommitId> CommitAsync(string text, CommitId? parent = null)
    {
        using var context = Context(Source);
        using var bytes = new MemoryStream(Encoding.UTF8.GetBytes(text));
        var hash = await context.ObjectStore.WriteBlobAsync(bytes);
        var snapshot = new ProjectSnapshot(ProjectArtifact.CreateSingleFile("Synthetic", new ArtifactPath("Song.flp"), hash, bytes.Length), DateTimeOffset.UtcNow);
        await context.ObjectStore.WriteObjectAsync(ObjectType.Snapshot, snapshot.ToCanonicalBytes());
        var commit = new Commit(parent.HasValue ? [parent.Value] : [], snapshot.Id, "test", DateTimeOffset.UtcNow, text);
        await context.ObjectStore.WriteObjectAsync(ObjectType.Commit, commit.ToCanonicalBytes());
        await context.UpdateBranchCommitAsync(BranchName.Main, commit.Id);
        return commit.Id;
    }

    private static Dictionary<string, string> Snapshot(string root) => Directory.EnumerateFileSystemEntries(root, "*", SearchOption.AllDirectories)
        .ToDictionary(p => Path.GetRelativePath(root, p), p => Directory.Exists(p) ? "directory" : Convert.ToBase64String(File.ReadAllBytes(p)));

    private sealed class RecordingProgress(Action<OperationProgress>? callback = null) : IProgress<OperationProgress>
    {
        public List<OperationProgress> Reports { get; } = [];
        public void Report(OperationProgress value) { Reports.Add(value); callback?.Invoke(value); }
    }

    private sealed class LocalProcessFactory(string policy) : IRemoteTransportFactory
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
