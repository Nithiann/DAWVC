using System.Text;

using DawVcs.Application.Common;
using DawVcs.Application.Remotes;
using DawVcs.Domain.Artifacts;
using DawVcs.Domain.Common;
using DawVcs.Domain.Configuration;
using DawVcs.Domain.Entities;
using DawVcs.Domain.Hashing;
using DawVcs.Domain.Remotes;
using DawVcs.Domain.Storage;
using DawVcs.Infrastructure.Configuration;
using DawVcs.Infrastructure.Remotes;
using DawVcs.Infrastructure.Repositories;

using FluentAssertions;

using Xunit;

namespace DawVcs.EndToEndTests;

[Collection("PerformanceNonParallel")]
public sealed class PushAcTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "dawvc-push-" + Guid.NewGuid().ToString("N"));
    private readonly RepositoryConfig _config = new(RepositoryId.New(), "Push song", new ArtifactPath("Song.flp"));
    private string Local => Path.Combine(_root, "local");
    private string Remote => Path.Combine(_root, "remote");
    private string RemoteRef => Path.Combine(Remote, "refs", "heads", "main");
    private static PushUseCase UseCase => new(p => new FileSystemRepositoryContext(p), new DefaultRemoteTransportFactory());

    public PushAcTests()
    {
        Directory.CreateDirectory(Local);
        using var context = new FileSystemRepositoryContext(Local);
        context.SaveConfig(_config);
        context.SetCurrentBranch(BranchName.Main);
        context.SaveRemote(new RemoteConfig(new RemoteName("origin"), Remote));
        Directory.CreateDirectory(Path.Combine(Remote, "objects"));
        Directory.CreateDirectory(Path.Combine(Remote, "refs", "heads"));
        File.WriteAllText(Path.Combine(Remote, "dawvc.yaml"), YamlRepositoryConfigManager.ToYaml(_config));
    }

    [Fact]
    public async Task Push_CreatesFastForwardsDeduplicatesAndClonesByteIdentically()
    {
        var first = await CommitAsync("first");
        var progress = new RecordingProgress();
        var created = await UseCase.ExecuteAsync(new PushRequest(Local), default, progress);
        created.ObjectsTransferred.Should().Be(3);
        File.ReadAllText(RemoteRef).Trim().Should().Be(first.ToString());
        progress.Reports[^1].Stage.Should().Be(OperationStage.Completed);
        progress.Reports.Count(p => p.Stage == OperationStage.Completed).Should().Be(1);
        var second = await CommitAsync("second", first);
        var updated = await UseCase.ExecuteAsync(new PushRequest(Local));
        updated.OldCommit.Should().Be(first);
        updated.NewCommit.Should().Be(second);
        updated.ObjectsTransferred.Should().Be(3);
        var unchanged = await UseCase.ExecuteAsync(new PushRequest(Local));
        unchanged.UpToDate.Should().BeTrue();
        unchanged.ObjectsTransferred.Should().Be(0);
        var clone = Path.Combine(_root, "clone");
        (await DawVcs.Cli.Program.Main(["clone", Remote, clone])).Should().Be(0);
        File.ReadAllText(Path.Combine(clone, "Song.flp")).Should().Be("second");
        Directory.Exists(Path.Combine(Remote, ".dawvc")).Should().BeFalse();
        (await DawVcs.Cli.Program.Main(["fsck", "--dir", clone])).Should().Be(0);
        AssertNoQuarantine();
    }

    [Fact]
    public async Task DryRun_PreservesEveryDestinationByteAndLocalTracking()
    {
        await CommitAsync("first");
        var before = ReadState(Remote);
        var result = await UseCase.ExecuteAsync(new PushRequest(Local, DryRun: true));
        result.ObjectsTransferred.Should().Be(3);
        result.IsDryRun.Should().BeTrue();
        ReadState(Remote).Should().BeEquivalentTo(before);
        using var context = new FileSystemRepositoryContext(Local);
        context.GetRemoteTrackingCommit("origin", BranchName.Main).Should().BeNull();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Push_RejectsRemoteAheadAndDivergence(bool diverged)
    {
        var first = await CommitAsync("first");
        await UseCase.ExecuteAsync(new PushRequest(Local));
        await CommitAsync("remote-ahead", first);
        await UseCase.ExecuteAsync(new PushRequest(Local));
        if (diverged) await CommitAsync("diverged", first);
        else { using var context = new FileSystemRepositoryContext(Local); await context.UpdateBranchCommitAsync(BranchName.Main, first); }
        var before = ReadState(Remote);
        await Assert.ThrowsAsync<PushNonFastForwardException>(() => UseCase.ExecuteAsync(new PushRequest(Local)));
        ReadState(Remote).Should().BeEquivalentTo(before);
        (await DawVcs.Cli.Program.Main(["push", "--dir", Local])).Should().Be(9);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Push_RejectsIdentityOrSchemaMismatch(bool schema)
    {
        await CommitAsync("first");
        var config = schema ? _config with { SchemaVersion = 2 } : _config with { RepositoryId = RepositoryId.New() };
        File.WriteAllText(Path.Combine(Remote, "dawvc.yaml"), YamlRepositoryConfigManager.ToYaml(config));
        var before = ReadState(Remote);
        await Assert.ThrowsAsync<IncompatibleRemoteRepositoryException>(() => UseCase.ExecuteAsync(new PushRequest(Local)));
        ReadState(Remote).Should().BeEquivalentTo(before);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Push_RejectsActiveOrMalformedNonBareHead(bool malformed)
    {
        await CommitAsync("first");
        MakeNonBare();
        File.WriteAllText(Path.Combine(Remote, ".dawvc", "HEAD"), malformed ? "broken" : "ref: refs/heads/main\n");
        var before = ReadState(Remote);
        await Assert.ThrowsAsync<PushRejectedException>(() => UseCase.ExecuteAsync(new PushRequest(Local)));
        ReadState(Remote).Should().BeEquivalentTo(before);
        (await DawVcs.Cli.Program.Main(["push", "--dir", Local])).Should().Be(8);
    }

    [Fact]
    public async Task Push_ToInactiveNonBareBranch_PreservesHeadAndWorktree()
    {
        await CommitAsync("first");
        MakeNonBare();
        File.WriteAllText(Path.Combine(Remote, ".dawvc", "HEAD"), "ref: refs/heads/work\n");
        File.WriteAllText(Path.Combine(Remote, "Song.flp"), "unsaved remote work");
        await UseCase.ExecuteAsync(new PushRequest(Local));
        File.ReadAllText(Path.Combine(Remote, "Song.flp")).Should().Be("unsaved remote work");
        File.ReadAllText(Path.Combine(Remote, ".dawvc", "HEAD")).Should().Be("ref: refs/heads/work\n");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CorruptLocalOrExistingRemoteObject_NeverPublishes(bool remoteCorruption)
    {
        var tip = await CommitAsync("first");
        if (remoteCorruption) await UseCase.ExecuteAsync(new PushRequest(Local));
        var objectRoot = remoteCorruption ? Path.Combine(Remote, "objects") : Path.Combine(Local, ".dawvc", "objects");
        var hex = tip.ToString();
        var path = Path.Combine(objectRoot, hex[..2], hex[2..]);
        var bytes = File.ReadAllBytes(path);
        bytes[^1] ^= 1;
        File.WriteAllBytes(path, bytes);
        var before = ReadState(Remote);
        await Assert.ThrowsAnyAsync<InvalidEnvelopeException>(() => UseCase.ExecuteAsync(new PushRequest(Local)));
        ReadState(Remote).Should().BeEquivalentTo(before);
        (await DawVcs.Cli.Program.Main(["push", "--dir", Local])).Should().Be(3);
    }

    [Fact]
    public async Task MissingAncestorBlob_IsNotSkippedWhenCommitAlreadyExistsOnRemote()
    {
        var first = await CommitAsync("first");
        await UseCase.ExecuteAsync(new PushRequest(Local));
        await CommitAsync("second", first);
        using var context = new FileSystemRepositoryContext(Local);
        var commit = await context.LoadCommitAsync(first);
        var snapshot = await context.LoadSnapshotAsync(commit!.SnapshotId);
        var hash = snapshot!.Project.Root.GetEntries().Single().Hash.ToString();
        File.Delete(Path.Combine(Local, ".dawvc", "objects", hash[..2], hash[2..]));
        var before = ReadState(Remote);
        await Assert.ThrowsAsync<InvalidEnvelopeException>(() => UseCase.ExecuteAsync(new PushRequest(Local)));
        ReadState(Remote).Should().BeEquivalentTo(before);
    }

    [Fact]
    public async Task CancellationDuringTransfer_PreservesRefAndCleansQuarantine()
    {
        var first = await CommitAsync("first");
        await UseCase.ExecuteAsync(new PushRequest(Local));
        await CommitAsync("second", first);
        using var cancellation = new CancellationTokenSource();
        var progress = new RecordingProgress(p =>
        {
            if (p.Stage == OperationStage.TransferringObjects && p.Completed > 0) cancellation.Cancel();
        });
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => UseCase.ExecuteAsync(new PushRequest(Local), cancellation.Token, progress));
        File.ReadAllText(RemoteRef).Trim().Should().Be(first.ToString());
        progress.Reports.Should().NotContain(p => p.Stage == OperationStage.Completed);
        AssertNoQuarantine();
        (await UseCase.ExecuteAsync(new PushRequest(Local))).NewCommit.Should().NotBe(first);
    }

    [Fact]
    public async Task PreCancellation_DoesNotOpenRepository()
    {
        var useCase = new PushUseCase(_ => throw new InvalidOperationException("must not open"), new DefaultRemoteTransportFactory());
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => useCase.ExecuteAsync(new PushRequest(Local), new CancellationToken(true)));
    }

    [Fact]
    public async Task Receiver_RejectsStaleTipAndSerializesWriters()
    {
        var tip = await CommitAsync("first");
        using var transport = new FileSystemRemoteTransport(Remote);
        using (var session = await transport.BeginPushAsync(BranchName.Main, null))
        {
            await Assert.ThrowsAsync<RemoteException>(() => transport.BeginPushAsync(BranchName.Main, null));
            File.WriteAllText(RemoteRef, tip + "\n"); // Simulate a legacy writer that ignores the lock.
            await Assert.ThrowsAsync<PushRejectedException>(() => session.PublishAsync(tip));
        }
        await Assert.ThrowsAsync<PushRejectedException>(() => transport.BeginPushAsync(BranchName.Main, null));
        AssertNoQuarantine();
    }

    [Fact]
    public async Task Receiver_RejectsMissingClosureAndCorruptUpload()
    {
        var tip = await CommitAsync("first");
        using var transport = new FileSystemRemoteTransport(Remote);
        using (var session = await transport.BeginPushAsync(BranchName.Main, null))
        {
            await Assert.ThrowsAsync<InvalidEnvelopeException>(() => session.PublishAsync(tip));
            using var payload = new MemoryStream(Encoding.UTF8.GetBytes("wrong"));
            var hash = Blake3ContentHasher.Hash(Encoding.UTF8.GetBytes("right"));
            await Assert.ThrowsAnyAsync<InvalidEnvelopeException>(() => session.ReceiveObjectAsync(new ObjectEnvelopeHeader(ObjectType.Blob, 5, hash), payload));
        }
        File.Exists(RemoteRef).Should().BeFalse();
        AssertNoQuarantine();
    }

    [Fact]
    public async Task ReceiverLock_AlsoBlocksLocalRefAndHeadWriters()
    {
        var tip = await CommitAsync("first");
        using var transport = new FileSystemRemoteTransport(Local);
        var target = new BranchName("publish");
        using (var session = await transport.BeginPushAsync(target, null))
        {
            using var context = new FileSystemRepositoryContext(Local);
            await Assert.ThrowsAsync<IOException>(() => context.UpdateBranchCommitAsync(target, tip));
            Action switchHead = () => context.SetCurrentBranch(target);
            switchHead.Should().Throw<IOException>();
            Action create = () => context.CreateBranch(target, tip);
            create.Should().Throw<IOException>();
            Action delete = () => context.DeleteBranch(target);
            delete.Should().Throw<IOException>();
        }
        using var after = new FileSystemRepositoryContext(Local);
        after.CreateBranch(target, tip); // Lock must be released after disposal.
    }

    [Fact]
    public async Task Cli_ExposesPushDryRunAndRejectsForceAndUnknownBranch()
    {
        await CommitAsync("first");
        (await DawVcs.Cli.Program.Main(["push", "--help"])).Should().Be(0);
        (await DawVcs.Cli.Program.Main(["push", "--dir", Local, "--dry-run"])).Should().Be(0);
        File.Exists(RemoteRef).Should().BeFalse();
        (await DawVcs.Cli.Program.Main(["push", "origin", "missing", "--dir", Local])).Should().Be(7);
        (await DawVcs.Cli.Program.Main(["push", "--force", "--dir", Local])).Should().NotBe(0);
        (await DawVcs.Cli.Program.Main(["push", "--dir", Local])).Should().Be(0);
        File.Exists(RemoteRef).Should().BeTrue();
    }

    [Fact]
    public async Task Push_UsesUpstreamMappingAndFileUrl()
    {
        var tip = await CommitAsync("first");
        using (var context = new FileSystemRepositoryContext(Local))
        {
            context.SetUpstream(BranchName.Main, "backup", new BranchName("published"));
            context.SaveRemote(new RemoteConfig(new RemoteName("backup"), new Uri(Remote + Path.DirectorySeparatorChar).AbsoluteUri));
        }
        var result = await UseCase.ExecuteAsync(new PushRequest(Local));
        result.RemoteBranch.Value.Should().Be("published");
        File.ReadAllText(Path.Combine(Remote, "refs", "heads", "published")).Trim().Should().Be(tip.ToString());
    }

    [Fact]
    public async Task Receiver_RejectsTruncatedUploadWithoutPublishing()
    {
        var tip = await CommitAsync("first");
        using var transport = new FileSystemRemoteTransport(Remote);
        using (var session = await transport.BeginPushAsync(BranchName.Main, null))
        {
            using var payload = new MemoryStream([1, 2]);
            var header = new ObjectEnvelopeHeader(ObjectType.Blob, 3, Blake3ContentHasher.Hash([1, 2, 3]));
            await Assert.ThrowsAnyAsync<InvalidEnvelopeException>(() => session.ReceiveObjectAsync(header, payload));
        }
        File.Exists(RemoteRef).Should().BeFalse();
        AssertNoQuarantine();
    }

    [Fact]
    public async Task UnsafeSnapshotPaths_AreRejectedBeforeDestinationWrites()
    {
        var original = await CommitAsync("first");
        using (var context = new FileSystemRepositoryContext(Local))
        {
            var commit = await context.LoadCommitAsync(original);
            var snapshot = await context.ObjectStore.ReadObjectPayloadAsync(commit!.SnapshotId.Value);
            var bad = Encoding.UTF8.GetString(snapshot).Replace("Song.flp", "../escape.flp", StringComparison.Ordinal);
            var snapshotId = await context.ObjectStore.WriteObjectAsync(ObjectType.Snapshot, Encoding.UTF8.GetBytes(bad));
            var invalid = new Commit([], new SnapshotId(snapshotId), "test", DateTimeOffset.UtcNow, "unsafe");
            await context.ObjectStore.WriteObjectAsync(ObjectType.Commit, invalid.ToCanonicalBytes());
            await context.UpdateBranchCommitAsync(BranchName.Main, invalid.Id);
        }
        var before = ReadState(Remote);
        await Assert.ThrowsAsync<DawVcs.Application.Exceptions.PathSecurityException>(() => UseCase.ExecuteAsync(new PushRequest(Local)));
        ReadState(Remote).Should().BeEquivalentTo(before);
    }

    [Fact]
    public async Task CancellationImmediatelyBeforePublication_LeavesOldRef()
    {
        var first = await CommitAsync("first");
        await UseCase.ExecuteAsync(new PushRequest(Local));
        await CommitAsync("second", first);
        using var cancellation = new CancellationTokenSource();
        var progress = new RecordingProgress(p =>
        {
            if (p.Stage == OperationStage.UpdatingReferences) cancellation.Cancel();
        });
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => UseCase.ExecuteAsync(new PushRequest(Local), cancellation.Token, progress));
        File.ReadAllText(RemoteRef).Trim().Should().Be(first.ToString());
        AssertNoQuarantine();
    }

    [Fact]
    public async Task ExplicitNestedBranch_SelectsThatLocalBranch()
    {
        var tip = await CommitAsync("first");
        using (var context = new FileSystemRepositoryContext(Local)) context.CreateBranch(new BranchName("mix/alternate"), tip);
        var result = await UseCase.ExecuteAsync(new PushRequest(Local, "origin", "mix/alternate"));
        result.RemoteBranch.Value.Should().Be("mix/alternate");
        File.Exists(RemoteRef).Should().BeFalse();
        File.ReadAllText(Path.Combine(Remote, "refs", "heads", "mix", "alternate")).Trim().Should().Be(tip.ToString());
    }

    [Fact]
    public async Task RemoteAncestorOnSecondParent_IsAccepted()
    {
        var remoteTip = await CommitAsync("remote history");
        await UseCase.ExecuteAsync(new PushRequest(Local));
        var sibling = await CommitAsync("independent history");
        using (var context = new FileSystemRepositoryContext(Local))
        {
            var parent = await context.LoadCommitAsync(sibling);
            var merge = new Commit([sibling, remoteTip], parent!.SnapshotId, "test", DateTimeOffset.UtcNow, "two parents");
            await context.ObjectStore.WriteObjectAsync(ObjectType.Commit, merge.ToCanonicalBytes());
            await context.UpdateBranchCommitAsync(BranchName.Main, merge.Id);
        }
        var result = await UseCase.ExecuteAsync(new PushRequest(Local));
        result.OldCommit.Should().Be(remoteTip);
        result.ObjectsTransferred.Should().Be(4);
    }

    [Fact]
    public async Task DestinationHeadChangedDuringTransfer_IsRecheckedBeforePublication()
    {
        await CommitAsync("first");
        MakeNonBare();
        var headPath = Path.Combine(Remote, ".dawvc", "HEAD");
        File.WriteAllText(headPath, "ref: refs/heads/work\n");
        var progress = new RecordingProgress(p =>
        {
            if (p.Stage == OperationStage.UpdatingReferences) File.WriteAllText(headPath, "ref: refs/heads/main\n");
        });
        await Assert.ThrowsAsync<PushRejectedException>(() => UseCase.ExecuteAsync(new PushRequest(Local), default, progress));
        File.Exists(Path.Combine(Remote, ".dawvc", "refs", "heads", "main")).Should().BeFalse();
    }

    private async Task<CommitId> CommitAsync(string text, CommitId? parent = null)
    {
        using var context = new FileSystemRepositoryContext(Local);
        using var payload = new MemoryStream(Encoding.UTF8.GetBytes(text));
        var hash = await context.ObjectStore.WriteBlobAsync(payload);
        var artifact = ProjectArtifact.CreateSingleFile("Synthetic", new ArtifactPath("Song.flp"), hash, payload.Length);
        var snapshot = new ProjectSnapshot(artifact, DateTimeOffset.UtcNow);
        await context.ObjectStore.WriteObjectAsync(ObjectType.Snapshot, snapshot.ToCanonicalBytes());
        var commit = new Commit(parent.HasValue ? [parent.Value] : [], snapshot.Id, "test", DateTimeOffset.UtcNow, text);
        await context.ObjectStore.WriteObjectAsync(ObjectType.Commit, commit.ToCanonicalBytes());
        await context.UpdateBranchCommitAsync(BranchName.Main, commit.Id);
        return commit.Id;
    }

    private void MakeNonBare()
    {
        var metadata = Path.Combine(Remote, ".dawvc");
        Directory.CreateDirectory(metadata);
        Directory.Move(Path.Combine(Remote, "objects"), Path.Combine(metadata, "objects"));
        Directory.Move(Path.Combine(Remote, "refs"), Path.Combine(metadata, "refs"));
    }

    private void AssertNoQuarantine()
    {
        var incoming = Path.Combine(Remote, "incoming");
        if (Directory.Exists(incoming)) Directory.EnumerateFileSystemEntries(incoming).Should().BeEmpty();
    }

    private static Dictionary<string, string> ReadState(string path) => Directory.GetFiles(path, "*", SearchOption.AllDirectories)
        .ToDictionary(f => Path.GetRelativePath(path, f), f => Convert.ToBase64String(File.ReadAllBytes(f)));

    private sealed class RecordingProgress(Action<OperationProgress>? callback = null) : IProgress<OperationProgress>
    {
        public List<OperationProgress> Reports { get; } = [];
        public void Report(OperationProgress value) { Reports.Add(value); callback?.Invoke(value); }
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);
}
