using System.Diagnostics;
using System.Text;
using System.Text.Json;

using DawVcs.Application.Checkouts;
using DawVcs.Application.Commits;
using DawVcs.Application.Common;
using DawVcs.Application.Diagnostics;
using DawVcs.Application.Exceptions;
using DawVcs.Application.Remotes;
using DawVcs.Application.Scanning;
using DawVcs.Domain.Artifacts;
using DawVcs.Domain.Common;
using DawVcs.Domain.Configuration;
using DawVcs.Domain.Entities;
using DawVcs.Domain.Remotes;
using DawVcs.Domain.Repositories;
using DawVcs.Domain.Serialization;
using DawVcs.Domain.Storage;
using DawVcs.Infrastructure.Remotes;
using DawVcs.Infrastructure.Remotes.Ssh;
using DawVcs.Infrastructure.Repositories;
using DawVcs.Infrastructure.Storage;

using FluentAssertions;

using Xunit;

namespace DawVcs.EndToEndTests;

public sealed class ExplicitMergeTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "dawvc-merge-" + Guid.NewGuid().ToString("N"));
    private string Local => Path.Combine(_root, "local");
    private string Other => Path.Combine(_root, "other");
    private string Remote => Path.Combine(_root, "remote");
    private string Policy => Path.Combine(_root, "serve.json");
    private static FileSystemRepositoryContext Context(string path) => new(path);
    private LocalProcessFactory Factory => new(Policy);
    private FetchUseCase Fetch => new(Context, Factory, p => new QuarantineObjectStore(p));
    private static MergeUseCase Merge => new(Context, new CheckoutUseCase(Context));

    public ExplicitMergeTests()
    {
        Directory.CreateDirectory(Local);
        using var context = Context(Local);
        context.SaveConfig(new RepositoryConfig(RepositoryId.New(), "Merge song", new ArtifactPath("Song.flp")));
        context.SetCurrentBranch(BranchName.Main);
        File.WriteAllText(Policy, JsonSerializer.Serialize(new ServeConfiguration
        {
            Repositories = new Dictionary<string, HostedRepository> { ["song"] = new() { Path = Remote, AllowPush = true } }
        }));
    }

    [Theory]
    [InlineData(MergeSnapshotChoice.Local, false)]
    [InlineData(MergeSnapshotChoice.Incoming, false)]
    [InlineData(MergeSnapshotChoice.Local, true)]
    [InlineData(MergeSnapshotChoice.Incoming, true)]
    public async Task Divergence_ResolvesWholeSnapshotAndResumesFastForwardCollaboration(MergeSnapshotChoice choice, bool ssh)
    {
        var (local, incoming) = await DivergeAsync(ssh);
        var progress = new RecordingProgress();
        var result = await Merge.ExecuteAsync(new MergeRequest(Local, "origin/main", choice, "Choose project version", "producer"), default, progress);
        using var context = Context(Local);
        var merged = (await context.LoadCommitAsync(result.CommitId))!;
        merged.Parents.Should().Equal(local, incoming);
        merged.Message.Should().Be("Choose project version");
        merged.Author.Should().Be("producer");
        var selected = (await context.LoadCommitAsync(choice == MergeSnapshotChoice.Local ? local : incoming))!;
        merged.SnapshotId.Should().Be(selected.SnapshotId);
        result.SnapshotId.Should().Be(selected.SnapshotId);
        context.GetBranchCommit(BranchName.Main).Should().Be(result.CommitId);
        context.GetRemoteTrackingCommit("origin", BranchName.Main).Should().Be(incoming);
        var label = choice == MergeSnapshotChoice.Local ? "local" : "incoming";
        File.ReadAllText(Path.Combine(Local, "Song.flp")).Should().Be(label);
        File.ReadAllText(Path.Combine(Local, "Samples", label + ".wav")).Should().Be(label + " sample");
        File.Exists(Path.Combine(Local, "Samples", (label == "local" ? "incoming" : "local") + ".wav")).Should().BeFalse();
        (await new StatusUseCase(Context).ExecuteAsync(new StatusRequest(Local))).Status.HasWorkingTreeModifications.Should().BeFalse();
        progress.Reports.Count(p => p.Stage == OperationStage.Completed).Should().Be(1);
        (await new FsckUseCase(Context).ExecuteAsync(new FsckRequest(Local))).IsHealthy.Should().BeTrue();
        (await new PushUseCase(Context, Factory).ExecuteAsync(new PushRequest(Local))).NewCommit.Should().Be(result.CommitId);
        (await new PullUseCase(Context, Fetch, new CheckoutUseCase(Context)).ExecuteAsync(new PullRequest(Other))).NewCommit.Should().Be(result.CommitId);
        File.ReadAllText(Path.Combine(Other, "Song.flp")).Should().Be(label);
        // Both original tips still restore independently after merge publication.
        foreach (var tip in new[] { local, incoming })
        {
            var copy = Path.Combine(_root, tip.ToString()[..8]);
            await new CheckoutUseCase(Context).ExecuteAsync(new CheckoutRequest(Local, tip.ToString(), copy));
            File.ReadAllText(Path.Combine(copy, "Song.flp")).Should().Be(tip == local ? "local" : "incoming");
        }
    }

    [Theory]
    [InlineData("modified")]
    [InlineData("missing")]
    [InlineData("staged")]
    [InlineData("collision")]
    public async Task DirtyOrCollidingWork_BlocksWithoutChangingHistoryOrUserFiles(string kind)
    {
        var (local, _) = await DivergeAsync();
        if (kind == "modified") File.WriteAllText(Path.Combine(Local, "Song.flp"), "unsaved work");
        if (kind == "missing") File.Delete(Path.Combine(Local, "Song.flp"));
        if (kind == "staged")
        {
            using var context = Context(Local);
            using var bytes = new MemoryStream(Encoding.UTF8.GetBytes("staged"));
            var hash = await context.ObjectStore.WriteBlobAsync(bytes);
            await context.StagingIndex.StageEntryAsync(ArtifactEntry.Create(new ArtifactPath("new.wav"), hash, bytes.Length));
        }
        if (kind == "collision") File.WriteAllText(Path.Combine(Local, "Samples", "incoming.wav"), "untracked recording");
        var before = Workspace();
        await FluentActions.Awaiting(() => Merge.ExecuteAsync(Request())).Should().ThrowAsync<DirtyWorkspaceException>();
        Workspace().Should().BeEquivalentTo(before);
        using var after = Context(Local);
        after.GetBranchCommit(BranchName.Main).Should().Be(local);
        if (kind == "staged") (await after.StagingIndex.GetStagedEntriesAsync()).Should().ContainSingle();
    }

    [Theory]
    [InlineData(OperationStage.WritingObjects)]
    [InlineData(OperationStage.PublishingFiles)]
    public async Task CancellationBeforePublication_PreservesWorkspaceAndBranch(OperationStage stage)
    {
        var (local, _) = await DivergeAsync();
        var before = Workspace();
        using var cancellation = new CancellationTokenSource();
        var progress = new RecordingProgress(p => { if (p.Stage == stage) cancellation.Cancel(); });
        await FluentActions.Awaiting(() => Merge.ExecuteAsync(Request(), cancellation.Token, progress))
            .Should().ThrowAsync<OperationCanceledException>();
        Workspace().Should().BeEquivalentTo(before);
        using var context = Context(Local);
        context.GetBranchCommit(BranchName.Main).Should().Be(local);
        progress.Reports.Should().NotContain(p => p.Stage == OperationStage.Completed);
    }

    [Theory]
    [InlineData(OperationStage.WritingObjects)]
    [InlineData(OperationStage.PublishingFiles)]
    public async Task BranchSwitchDuringOperation_IsDetectedBeforeWorkspacePublication(OperationStage stage)
    {
        var (local, _) = await DivergeAsync();
        using (var context = Context(Local)) context.CreateBranch(BranchName.Parse("other"), local);
        var before = Workspace();
        var progress = new RecordingProgress(p =>
        {
            if (p.Stage != stage) return;
            using var context = Context(Local);
            context.SetCurrentBranch(BranchName.Parse("other"));
        });
        await FluentActions.Awaiting(() => Merge.ExecuteAsync(Request(), default, progress))
            .Should().ThrowAsync<RepositoryStateChangedException>();
        Workspace().Should().BeEquivalentTo(before);
        using var after = Context(Local);
        after.GetBranchCommit(BranchName.Main).Should().Be(local);
        after.GetCurrentBranch().Value.Should().Be("other");
    }

    [Theory]
    [InlineData("local-blob")]
    [InlineData("incoming-blob")]
    [InlineData("incoming-snapshot")]
    [InlineData("ancestor")]
    public async Task CorruptionOnEitherSide_RefusesEvenWhenOtherSnapshotWasChosen(string kind)
    {
        var (local, incoming) = await DivergeAsync();
        using (var context = Context(Local))
        {
            var tip = (await context.LoadCommitAsync(kind == "local-blob" ? local : incoming))!;
            var snapshot = (await context.LoadSnapshotAsync(tip.SnapshotId))!;
            var hash = kind switch
            {
                "incoming-snapshot" => tip.SnapshotId.Value,
                "ancestor" => tip.Parents[0].Value,
                _ => snapshot.Project.Root.GetEntries()[0].Blob.Value
            };
            File.WriteAllBytes(((LooseObjectStore)context.ObjectStore).GetObjectPath(hash), [0, 1, 2]);
        }
        var before = Workspace();
        await FluentActions.Awaiting(() => Merge.ExecuteAsync(Request(MergeSnapshotChoice.Local))).Should().ThrowAsync<InvalidEnvelopeException>();
        Workspace().Should().BeEquivalentTo(before);
        using var after = Context(Local);
        after.GetBranchCommit(BranchName.Main).Should().Be(local);
    }

    [Theory]
    [InlineData("same")]
    [InlineData("ancestor")]
    [InlineData("descendant")]
    [InlineData("unrelated")]
    [InlineData("missing")]
    public async Task NonDivergedUnrelatedOrMissingReference_DoesNotCreateAMerge(string kind)
    {
        var first = await CommitAtAsync(Local, "base");
        var reference = first;
        if (kind == "ancestor") await CommitAtAsync(Local, "local", first);
        if (kind is "descendant" or "unrelated")
        {
            reference = await CommitAtAsync(Local, "incoming", kind == "unrelated" ? null : first);
            using var context = Context(Local);
            await context.UpdateBranchCommitAsync(BranchName.Main, first);
        }
        var before = Workspace();
        var request = new MergeRequest(Local, kind == "missing" ? "not-found" : reference.ToString(), MergeSnapshotChoice.Incoming, "merge");
        await FluentActions.Awaiting(() => Merge.ExecuteAsync(request)).Should().ThrowAsync<ArgumentException>();
        Workspace().Should().BeEquivalentTo(before);
    }

    [Fact]
    public async Task LocalBranchReferenceAndUnrelatedUntrackedFile_AreSupported()
    {
        var (_, incoming) = await DivergeAsync();
        using (var context = Context(Local)) context.CreateBranch(BranchName.Parse("incoming-version"), incoming);
        File.WriteAllText(Path.Combine(Local, "notes.txt"), "keep");
        var result = await Merge.ExecuteAsync(Request() with { Reference = "incoming-version" });
        result.IncomingParent.Should().Be(incoming);
        File.ReadAllText(Path.Combine(Local, "notes.txt")).Should().Be("keep");
    }

    [Fact]
    public async Task Cli_RequiresAnExplicitChoiceAndCreatesTwoParentCommit()
    {
        var (local, incoming) = await DivergeAsync();
        var invalid = await CliAsync("merge", "origin/main", "--take", "invalid", "-m", "merge", "--dir", Local);
        invalid.Code.Should().Be(7, invalid.Output);
        var missing = await CliAsync("merge", "origin/main", "-m", "merge", "--dir", Local);
        missing.Code.Should().NotBe(0);
        var result = await CliAsync("merge", "origin/main", "--take", "incoming", "-m", "selected incoming", "--dir", Local);
        result.Code.Should().Be(0, result.Output);
        result.Output.Should().MatchRegex(@"both\s+parent\s+histories\s+are\s+preserved");
        using var context = Context(Local);
        var merged = (await context.LoadCommitAsync(context.GetBranchCommit(BranchName.Main)!.Value))!;
        merged.Parents.Should().Equal(local, incoming);
        File.ReadAllText(Path.Combine(Local, "Song.flp")).Should().Be("incoming");
    }

    [Theory]
    [InlineData("commit-schema")]
    [InlineData("snapshot-schema")]
    [InlineData("snapshot-container")]
    [InlineData("snapshot-single-shape")]
    [InlineData("snapshot-aggregate")]
    [InlineData("wrong-type")]
    [InlineData("oversized")]
    public async Task InvalidMetadataShapes_AreRejectedBeforePublication(string kind)
    {
        var (local, incoming) = await DivergeAsync();
        using (var context = Context(Local))
        {
            var tip = (await context.LoadCommitAsync(incoming))!;
            byte[] bytes;
            var type = ObjectType.Commit;
            if (kind.StartsWith("snapshot-", StringComparison.Ordinal))
            {
                var snapshot = (await context.LoadSnapshotAsync(tip.SnapshotId))!;
                var dto = ObjectPayloadSerializer.ReadSnapshotDto(snapshot.ToCanonicalBytes())!;
                if (kind == "snapshot-schema") dto.SchemaVersion = 2;
                if (kind == "snapshot-container") dto.ContainerKind = 999;
                if (kind == "snapshot-single-shape") dto.ContainerKind = (int)ArtifactKind.SingleFile;
                if (kind == "snapshot-aggregate") dto.AggregateHash = new string('0', 64);
                var hash = await context.ObjectStore.WriteObjectAsync(ObjectType.Snapshot, CanonicalJsonSerializer.SerializeCanonical(dto));
                bytes = new Commit(tip.Parents, new SnapshotId(hash), tip.Author, tip.Timestamp, tip.Message).ToCanonicalBytes();
            }
            else if (kind == "oversized") bytes = new byte[2 * 1024 * 1024 + 1];
            else
            {
                bytes = (tip with { SchemaVersion = kind == "commit-schema" ? 2 : 1 }).ToCanonicalBytes();
                if (kind == "wrong-type") type = ObjectType.Blob;
            }
            // Avoid the valid existing payload's hash/type in the wrong-type case.
            if (kind == "wrong-type") bytes = Encoding.UTF8.GetBytes("not commit metadata");
            var stored = await context.ObjectStore.WriteObjectAsync(type, bytes);
            await context.UpdateRemoteTrackingCommitAsync("origin", BranchName.Main, new CommitId(stored));
        }
        var before = Workspace();
        await FluentActions.Awaiting(() => Merge.ExecuteAsync(Request())).Should().ThrowAsync<InvalidEnvelopeException>();
        Workspace().Should().BeEquivalentTo(before);
        using var after = Context(Local);
        after.GetBranchCommit(BranchName.Main).Should().Be(local);
    }

    [Fact]
    public async Task TipChangeDuringCheckout_IsDetectedBeforePublication()
    {
        var (local, incoming) = await DivergeAsync();
        var before = Workspace();
        var progress = new RecordingProgress(p =>
        {
            if (p.Stage != OperationStage.PublishingFiles) return;
            using var context = Context(Local);
            context.UpdateBranchCommitAsync(BranchName.Main, incoming).GetAwaiter().GetResult();
        });
        await FluentActions.Awaiting(() => Merge.ExecuteAsync(Request(), default, progress))
            .Should().ThrowAsync<RepositoryStateChangedException>();
        Workspace().Should().BeEquivalentTo(before);
        using var after = Context(Local);
        after.GetBranchCommit(BranchName.Main).Should().Be(incoming);
        after.GetBranchCommit(BranchName.Main).Should().NotBe(local);
    }

    [Fact]
    public async Task UnbornBranch_IsRejectedWithoutCreatingHistory()
    {
        await FluentActions.Awaiting(() => Merge.ExecuteAsync(Request())).Should().ThrowAsync<ArgumentException>();
        using var context = Context(Local);
        context.GetBranchCommit(BranchName.Main).Should().BeNull();
    }

    [Fact]
    public async Task MissingTipBlob_IsRejectedEvenWhenLocalIsSelected()
    {
        var (local, incoming) = await DivergeAsync();
        using (var context = Context(Local))
        {
            var commit = (await context.LoadCommitAsync(incoming))!;
            var snapshot = (await context.LoadSnapshotAsync(commit.SnapshotId))!;
            File.Delete(((LooseObjectStore)context.ObjectStore).GetObjectPath(snapshot.Project.Root.GetEntries()[0].Blob.Value));
        }
        var before = Workspace();
        await FluentActions.Awaiting(() => Merge.ExecuteAsync(Request(MergeSnapshotChoice.Local))).Should().ThrowAsync<FileNotFoundException>();
        Workspace().Should().BeEquivalentTo(before);
        using var after = Context(Local);
        after.GetBranchCommit(BranchName.Main).Should().Be(local);
    }

    [Fact]
    public async Task RepeatedMergeOfSameIncomingTip_IsRejectedWithoutRewritingMerge()
    {
        await DivergeAsync();
        var first = await Merge.ExecuteAsync(Request());
        var before = Workspace();
        await FluentActions.Awaiting(() => Merge.ExecuteAsync(Request())).Should().ThrowAsync<ArgumentException>();
        Workspace().Should().BeEquivalentTo(before);
        using var context = Context(Local);
        context.GetBranchCommit(BranchName.Main).Should().Be(first.CommitId);
    }

    [Theory]
    [InlineData(".dawvc/HEAD")]
    [InlineData("dawvc.yaml")]
    [InlineData(".git/config")]
    public async Task ReservedMetadataArtifacts_AreRejected(string path)
    {
        var (local, incoming) = await DivergeAsync();
        using (var context = Context(Local))
        {
            var tip = (await context.LoadCommitAsync(incoming))!;
            var snapshot = (await context.LoadSnapshotAsync(tip.SnapshotId))!;
            var entries = snapshot.Project.Root.GetEntries().ToList();
            var blob = entries[0];
            entries.Add(ArtifactEntry.Create(new ArtifactPath(path), blob.Hash, blob.Size));
            var unsafeSnapshot = new ProjectSnapshot(new ProjectArtifact("Synthetic", new DirectoryArtifact(entries)), DateTimeOffset.UtcNow);
            await context.ObjectStore.WriteObjectAsync(ObjectType.Snapshot, unsafeSnapshot.ToCanonicalBytes());
            var invalid = new Commit(tip.Parents, unsafeSnapshot.Id, tip.Author, tip.Timestamp, tip.Message);
            await context.ObjectStore.WriteObjectAsync(ObjectType.Commit, invalid.ToCanonicalBytes());
            await context.UpdateRemoteTrackingCommitAsync("origin", BranchName.Main, invalid.Id);
        }
        var before = Workspace();
        var metadata = File.ReadAllBytes(Path.Combine(Local, ".dawvc", "HEAD"));
        await FluentActions.Awaiting(() => Merge.ExecuteAsync(Request())).Should().ThrowAsync<PathSecurityException>();
        Workspace().Should().BeEquivalentTo(before);
        File.ReadAllBytes(Path.Combine(Local, ".dawvc", "HEAD")).Should().Equal(metadata);
        using var after = Context(Local);
        after.GetBranchCommit(BranchName.Main).Should().Be(local);
    }

    [Fact]
    public async Task LinuxSymlinkIntroducedAtPublication_CannotRedirectAssetWrites()
    {
        if (OperatingSystem.IsWindows()) return; // Windows link creation needs separate privileges.
        var (local, _) = await DivergeAsync();
        var outside = Path.Combine(_root, "outside");
        Directory.CreateDirectory(outside);
        var samples = Path.Combine(Local, "Samples");
        var saved = Path.Combine(_root, "saved-samples");
        var progress = new RecordingProgress(p =>
        {
            if (p.Stage != OperationStage.PublishingFiles) return;
            Directory.Move(samples, saved);
            Directory.CreateSymbolicLink(samples, outside);
        });
        try
        {
            await FluentActions.Awaiting(() => Merge.ExecuteAsync(Request(), default, progress))
                .Should().ThrowAsync<PathSecurityException>();
            Directory.EnumerateFileSystemEntries(outside).Should().BeEmpty();
            File.ReadAllText(Path.Combine(Local, "Song.flp")).Should().Be("local");
            using var context = Context(Local);
            context.GetBranchCommit(BranchName.Main).Should().Be(local);
        }
        finally
        {
            if (new DirectoryInfo(samples).LinkTarget is not null) Directory.Delete(samples);
            if (Directory.Exists(saved)) Directory.Move(saved, samples);
        }
    }

    [Fact]
    public async Task OversizedNewCommitMessage_IsRejectedWithoutMovingTheBranch()
    {
        var (local, _) = await DivergeAsync();
        var before = Workspace();
        await FluentActions.Awaiting(() => Merge.ExecuteAsync(Request() with { Message = new string('x', 2 * 1024 * 1024) }))
            .Should().ThrowAsync<ArgumentException>();
        Workspace().Should().BeEquivalentTo(before);
        using var context = Context(Local);
        context.GetBranchCommit(BranchName.Main).Should().Be(local);
    }

    private MergeRequest Request(MergeSnapshotChoice choice = MergeSnapshotChoice.Incoming) => new(Local, "origin/main", choice, "Resolve versions");

    private async Task<(CommitId Local, CommitId Incoming)> DivergeAsync(bool ssh = false)
    {
        var first = await CommitAtAsync(Local, "base");
        await new BareRepositoryUseCase(Context, Factory, Fetch).ExecuteAsync(new BareRepositoryRequest(Local, Remote, true));
        var url = ssh ? "dawvc://test@host/song" : Remote;
        using (var context = Context(Local)) context.SaveRemote(new RemoteConfig(RemoteName.Origin, url));
        await new CloneUseCase(Context, Factory, Fetch, new CheckoutUseCase(Context)).ExecuteAsync(new CloneRequest(url, Other));
        var local = await CommitAtAsync(Local, "local", first);
        var incoming = await CommitAtAsync(Other, "incoming", first);
        await new PushUseCase(Context, Factory).ExecuteAsync(new PushRequest(Other));
        await Fetch.ExecuteAsync(new FetchRequest(Local));
        return (local, incoming);
    }

    private static async Task<CommitId> CommitAtAsync(string root, string label, CommitId? parent = null)
    {
        using var context = Context(root);
        var entries = new List<ArtifactEntry>();
        foreach (var (path, text) in new[] { ("Song.flp", label), ($"Samples/{label}.wav", label + " sample") })
        {
            using var bytes = new MemoryStream(Encoding.UTF8.GetBytes(text));
            var hash = await context.ObjectStore.WriteBlobAsync(bytes);
            entries.Add(ArtifactEntry.Create(new ArtifactPath(path), hash, bytes.Length, path.EndsWith(".flp", StringComparison.Ordinal) ? ArtifactRole.PrimaryProjectFile : ArtifactRole.ProjectAsset));
            var full = Path.Combine(root, path);
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            File.WriteAllText(full, text);
        }
        foreach (var file in Directory.GetFiles(Path.Combine(root, "Samples")))
            if (Path.GetFileName(file) != label + ".wav") File.Delete(file);
        var snapshot = new ProjectSnapshot(new ProjectArtifact("Synthetic", new DirectoryArtifact(entries)), DateTimeOffset.UtcNow,
            metadata: new Dictionary<string, string> { ["selected"] = label });
        await context.ObjectStore.WriteObjectAsync(ObjectType.Snapshot, snapshot.ToCanonicalBytes());
        var commit = new Commit(parent.HasValue ? [parent.Value] : [], snapshot.Id, "test", DateTimeOffset.UtcNow, label);
        await context.ObjectStore.WriteObjectAsync(ObjectType.Commit, commit.ToCanonicalBytes());
        await context.UpdateBranchCommitAsync(BranchName.Main, commit.Id);
        return commit.Id;
    }

    private Dictionary<string, string> Workspace() => Directory.GetFiles(Local, "*", SearchOption.AllDirectories)
        .Where(p => !Path.GetRelativePath(Local, p).StartsWith(".dawvc" + Path.DirectorySeparatorChar, StringComparison.Ordinal))
        .ToDictionary(p => Path.GetRelativePath(Local, p), p => Convert.ToBase64String(File.ReadAllBytes(p)));

    private static async Task<(int Code, string Output)> CliAsync(params string[] args)
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
