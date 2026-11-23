using DawVcs.Application.Common;
using DawVcs.Application.Remotes;
using DawVcs.Domain.Artifacts;
using DawVcs.Domain.Common;
using DawVcs.Domain.Configuration;
using DawVcs.Domain.Hashing;
using DawVcs.Domain.Remotes;
using DawVcs.Domain.Repositories;
using DawVcs.Domain.Serialization;
using DawVcs.Domain.Storage;

using FluentAssertions;

using NSubstitute;

using Xunit;

namespace DawVcs.Application.Tests.Remotes;

public sealed class FetchUseCaseTests
{
    private readonly IRepositoryContext _context = Substitute.For<IRepositoryContext>();
    private readonly IRemoteTransportFactory _transportFactory = Substitute.For<IRemoteTransportFactory>();
    private readonly IRemoteTransport _transport = Substitute.For<IRemoteTransport>();
    private readonly IQuarantineStore _quarantineStore = Substitute.For<IQuarantineStore>();
    private readonly FetchUseCase _useCase;

    private readonly RepositoryId _repoId = RepositoryId.New();
    private readonly RepositoryConfig _localConfig;

    public FetchUseCaseTests()
    {
        _localConfig = new RepositoryConfig(_repoId, "TestSong", new ArtifactPath("Song.flp"), BranchName.Main);
        _context.LoadConfig().Returns(_localConfig);

        _transportFactory.CreateTransport(Arg.Any<string>()).Returns(_transport);

        _useCase = new FetchUseCase(
            _ => _context,
            _transportFactory,
            _ => _quarantineStore);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExecuteAsync_Progress_CompletesOnceAndPreservesDryRun(bool dryRun)
    {
        ConfigureExistingCommit();
        var progress = new RecordingRemoteProgress();

        var result = await _useCase.ExecuteAsync(new FetchRequest("C:\\repo", DryRun: dryRun), default, progress);

        result.IsDryRun.Should().Be(dryRun);
        progress.Reports[0].Stage.Should().Be(OperationStage.OpeningRepository);
        progress.Reports[^1].Stage.Should().Be(OperationStage.Completed);
        progress.Reports.Count(p => p.Stage == OperationStage.Completed).Should().Be(1);
        progress.Reports.Where(p => p.Stage == OperationStage.TraversingHistory)
            .Should().NotBeEmpty().And.OnlyContain(p => p.Total == null);
        progress.Reports.Should().Contain(p => p.Stage == OperationStage.ReadingRemoteReferences && p.Completed == 1 && p.Total == 1);
        _quarantineStore.Received(1).PruneStaleDirectories(TimeSpan.FromHours(24));
        if (dryRun)
        {
            _quarantineStore.Received(1).DiscardSession();
            await _quarantineStore.DidNotReceiveWithAnyArgs().PromoteAllAsync(default!, default);
            await _context.DidNotReceiveWithAnyArgs().UpdateRemoteTrackingCommitAsync(default!, default, default, default);
            progress.Reports.Should().NotContain(p => p.Stage == OperationStage.TransferringObjects || p.Stage == OperationStage.UpdatingReferences);
        }
        else
        {
            progress.Reports.Should().Contain(p => p.Stage == OperationStage.TransferringObjects && p.Completed == 0 && p.Total == 0);
            progress.Reports.Should().Contain(p => p.Stage == OperationStage.UpdatingReferences && p.Completed == 1 && p.Total == 1);
        }
    }

    [Fact]
    public async Task ExecuteAsync_NullProgress_MatchesLegacyResult()
    {
        ConfigureExistingCommit();
        var request = new FetchRequest("C:\\repo", DryRun: true);
        var legacy = await _useCase.ExecuteAsync(request);
        var explicitNull = await _useCase.ExecuteAsync(request, default, null);
        explicitNull.Should().BeEquivalentTo(legacy);
    }

    [Fact]
    public async Task ExecuteAsync_ObserverFailure_PropagatesWithoutCompletionOrHandshake()
    {
        ConfigureExistingCommit();
        var progress = new RecordingRemoteProgress { ThrowOn = OperationStage.ConnectingRemote };
        var act = () => _useCase.ExecuteAsync(new FetchRequest("C:\\repo"), default, progress);

        (await act.Should().ThrowAsync<InvalidOperationException>()).Which.Should().BeSameAs(progress.ObserverFailure);
        progress.Reports.Should().NotContain(p => p.Stage == OperationStage.Completed);
        await _transport.DidNotReceiveWithAnyArgs().HandshakeAsync(default);
    }

    [Fact]
    public async Task ExecuteAsync_RemoteFailure_DoesNotComplete()
    {
        var progress = new RecordingRemoteProgress();
        var act = () => _useCase.ExecuteAsync(new FetchRequest("C:\\repo", "unknown"), default, progress);
        await act.Should().ThrowAsync<RemoteException>();
        progress.Reports.Should().NotContain(p => p.Stage == OperationStage.Completed);
    }

    [Fact]
    public async Task ExecuteAsync_EntryCancellation_DoesNotOpenContextOrComplete()
    {
        var opened = false;
        var useCase = new FetchUseCase(_ => { opened = true; return _context; }, _transportFactory, _ => _quarantineStore);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var progress = new RecordingRemoteProgress();
        var act = () => useCase.ExecuteAsync(new FetchRequest("C:\\repo"), cancellation.Token, progress);
        await act.Should().ThrowAsync<OperationCanceledException>();
        opened.Should().BeFalse();
        progress.Reports.Should().BeEmpty();
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task ExecuteAsync_BlobProgress_AdvancesOnlyAfterVerification(bool corruptBlob, bool dryRun)
    {
        ConfigureExistingCommit();
        var directory = Path.Combine(Path.GetTempPath(), "dawvc_fetch_progress_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var blobPayload = "audio payload"u8.ToArray();
            var blobHash = Blake3ContentHasher.Hash(blobPayload);
            var snapshotPayload = CanonicalJsonSerializer.SerializeCanonical(new SnapshotPayloadDto
            {
                Entries = [new EntryPayloadDto { Hash = blobHash.ToString(), Path = "Song.flp", Size = blobPayload.Length }]
            });
            var snapshotHash = Blake3ContentHasher.Hash(snapshotPayload);
            var commitPayload = CanonicalJsonSerializer.SerializeCanonical(new CommitPayloadDto { SnapshotId = snapshotHash.ToString() });
            var commitHash = Blake3ContentHasher.Hash(commitPayload);
            var commit = CommitId.Parse(commitHash.ToString());
            _transport.ListRefsAsync(Arg.Any<CancellationToken>()).Returns(new List<RemoteRef> { new("refs/heads/main", commit) });
            _context.ObjectStore.Exists(commitHash).Returns(false);
            var blobEnvelope = CreateEnvelope(ObjectType.Blob, blobHash, blobPayload);
            if (corruptBlob) blobEnvelope[^1] ^= 1;
            var envelopes = new Dictionary<ContentHash, byte[]>
            {
                [commitHash] = CreateEnvelope(ObjectType.Commit, commitHash, commitPayload),
                [snapshotHash] = CreateEnvelope(ObjectType.Snapshot, snapshotHash, snapshotPayload),
                [blobHash] = blobEnvelope
            };
            _transport.OpenObjectPayloadStreamAsync(Arg.Any<ContentHash>(), Arg.Any<CancellationToken>())
                .Returns(call => (Stream)new MemoryStream(envelopes[call.Arg<ContentHash>()]));
            _quarantineStore.GetIncomingPath(Arg.Any<ContentHash>())
                .Returns(call => Path.Combine(directory, call.Arg<ContentHash>().ToString()));
            _quarantineStore.CreateIncomingWriteStreamAsync(Arg.Any<ContentHash>(), Arg.Any<CancellationToken>())
                .Returns(call => (Stream)File.Create(Path.Combine(directory, call.Arg<ContentHash>().ToString())));
            var progress = new RecordingRemoteProgress();
            var act = () => _useCase.ExecuteAsync(new FetchRequest("C:\\repo", DryRun: dryRun), default, progress);

            if (corruptBlob && !dryRun)
            {
                await act.Should().ThrowAsync<PayloadHashMismatchException>();
                progress.Reports.Should().NotContain(p => p.Stage == OperationStage.Completed);
                progress.Reports.Where(p => p.Stage == OperationStage.TransferringObjects)
                    .Should().NotBeEmpty().And.OnlyContain(p => p.Completed == 0 && p.Total == 1);
                await _quarantineStore.DidNotReceiveWithAnyArgs().PromoteAsync(default!, default);
            }
            else
            {
                await act();
                progress.Reports.Count(p => p.Stage == OperationStage.Completed).Should().Be(1);
                if (dryRun)
                {
                    progress.Reports.Should().NotContain(p => p.Stage == OperationStage.TransferringObjects);
                    await _transport.DidNotReceive().OpenObjectPayloadStreamAsync(blobHash, Arg.Any<CancellationToken>());
                    _quarantineStore.Received(1).DiscardSession();
                }
                else
                {
                    progress.Reports.Should().Contain(p => p.Stage == OperationStage.TransferringObjects && p.Completed == 1 && p.Total == 1);
                    await _quarantineStore.Received(1).PromoteAsync(Arg.Is<VerifiedIncomingObject>(o => o.Hash == blobHash), Arg.Any<CancellationToken>());
                }
            }
            progress.Reports.Where(p => p.Stage == OperationStage.TraversingHistory).Should().OnlyContain(p => p.Total == null);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static byte[] CreateEnvelope(ObjectType type, ContentHash hash, byte[] payload)
    {
        var bytes = new byte[ObjectEnvelopeHeader.HeaderSize + payload.Length];
        new ObjectEnvelopeHeader(type, (ulong)payload.Length, hash).WriteTo(bytes.AsSpan(0, ObjectEnvelopeHeader.HeaderSize));
        payload.CopyTo(bytes.AsSpan(ObjectEnvelopeHeader.HeaderSize));
        return bytes;
    }

    private void ConfigureExistingCommit()
    {
        _context.GetRemote("origin").Returns(new RemoteConfig(RemoteName.Origin, "https://remote.url"));
        _transport.HandshakeAsync(Arg.Any<CancellationToken>()).Returns(new RemoteHandshake(1, _repoId, "v0.1.0"));
        var commit = CommitId.Parse(new string('a', 64));
        _transport.ListRefsAsync(Arg.Any<CancellationToken>()).Returns(new List<RemoteRef> { new("refs/heads/main", commit) });
        _context.ObjectStore.Exists(commit.Value).Returns(true);
    }

    [Fact]
    public async Task ExecuteAsync_UnconfiguredRemote_ThrowsRemoteException()
    {
        _context.GetRemote("unknown").Returns((RemoteConfig?)null);

        var request = new FetchRequest("C:\\repo", "unknown");
        var act = () => _useCase.ExecuteAsync(request);

        await act.Should().ThrowAsync<RemoteException>()
            .WithMessage("*No remote named 'unknown'*");
    }

    [Fact]
    public async Task ExecuteAsync_RepositoryIdMismatch_ThrowsRemoteException()
    {
        var otherRepoId = RepositoryId.New();
        _context.GetRemote("origin").Returns(new RemoteConfig(RemoteName.Origin, "https://remote.url"));
        _transport.HandshakeAsync(Arg.Any<CancellationToken>())
            .Returns(new RemoteHandshake(1, otherRepoId, "v0.1.0"));

        var request = new FetchRequest("C:\\repo", "origin");
        var act = () => _useCase.ExecuteAsync(request);

        await act.Should().ThrowAsync<RemoteException>()
            .WithMessage("*does not match local repositoryId*");
    }

    [Fact]
    public async Task ExecuteAsync_UnsupportedRemoteSchemaVersion_ThrowsRemoteException()
    {
        _context.GetRemote("origin").Returns(new RemoteConfig(RemoteName.Origin, "https://remote.url"));
        _transport.HandshakeAsync(Arg.Any<CancellationToken>())
            .Returns(new RemoteHandshake(999, _repoId, "v999.0.0"));

        var request = new FetchRequest("C:\\repo", "origin");
        var act = () => _useCase.ExecuteAsync(request);

        await act.Should().ThrowAsync<RemoteException>()
            .WithMessage("*newer than supported version*");
    }

    [Fact]
    public async Task ExecuteAsync_SpecificBranchNotFoundOnRemote_ThrowsRemoteException()
    {
        _context.GetRemote("origin").Returns(new RemoteConfig(RemoteName.Origin, "https://remote.url"));
        _transport.HandshakeAsync(Arg.Any<CancellationToken>())
            .Returns(new RemoteHandshake(1, _repoId, "v0.1.0"));

        _transport.ListRefsAsync(Arg.Any<CancellationToken>())
            .Returns(new List<RemoteRef> { new("refs/heads/main", CommitId.Parse(new string('a', 64))) });

        var request = new FetchRequest("C:\\repo", "origin", "feature-x");
        var act = () => _useCase.ExecuteAsync(request);

        await act.Should().ThrowAsync<RemoteException>()
            .WithMessage("*Remote branch 'feature-x' not found*");
    }

    [Fact]
    public async Task ExecuteAsync_DryRun_ReturnsObjectCountsWithoutUpdatingRefs()
    {
        _context.GetRemote("origin").Returns(new RemoteConfig(RemoteName.Origin, "https://remote.url"));
        _transport.HandshakeAsync(Arg.Any<CancellationToken>())
            .Returns(new RemoteHandshake(1, _repoId, "v0.1.0"));

        var commit = CommitId.Parse(new string('a', 64));
        _transport.ListRefsAsync(Arg.Any<CancellationToken>())
            .Returns(new List<RemoteRef> { new("refs/heads/main", commit) });

        // Local object store already has commit
        _context.ObjectStore.Exists(commit.Value).Returns(true);

        var request = new FetchRequest("C:\\repo", "origin", DryRun: true);
        var result = await _useCase.ExecuteAsync(request);

        result.IsDryRun.Should().BeTrue();
        result.ObjectsFetched.Should().Be(0);
        await _context.DidNotReceiveWithAnyArgs().UpdateRemoteTrackingCommitAsync(default!, default, default, default);
        _quarantineStore.Received(1).DiscardSession();
    }
}
