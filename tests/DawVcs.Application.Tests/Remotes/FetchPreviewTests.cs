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

public sealed class FetchPreviewTests
{
    private readonly IRepositoryContext _context = Substitute.For<IRepositoryContext>();
    private readonly IRemoteTransport _transport = Substitute.For<IRemoteTransport>();
    private readonly Dictionary<ContentHash, byte[]> _envelopes = [];
    private readonly FetchUseCase _fetch;
    private readonly ContentHash _blob = Blake3ContentHasher.Hash("audio"u8);
    private readonly ContentHash _snapshot;

    public FetchPreviewTests()
    {
        var factory = Substitute.For<IRemoteTransportFactory>();
        factory.CreateTransport(Arg.Any<string>()).Returns(_transport);
        var id = RepositoryId.New();
        _context.LoadConfig().Returns(new RepositoryConfig(id, "Preview", new ArtifactPath("Song.flp")));
        _context.GetRemote("origin").Returns(new RemoteConfig(RemoteName.Origin, "file://source"));
        _transport.HandshakeAsync(Arg.Any<CancellationToken>()).Returns(new RemoteHandshake(1, id));
        _transport.OpenObjectPayloadStreamAsync(Arg.Any<ContentHash>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult<Stream?>(_envelopes.TryGetValue(call.Arg<ContentHash>(), out var bytes) ? new MemoryStream(bytes, writable: false) : null));
        _fetch = new FetchUseCase(_ => _context, factory, _ => throw new InvalidOperationException("Preview must never create quarantine"));
        _snapshot = Add(ObjectType.Snapshot, CanonicalJsonSerializer.SerializeCanonical(new SnapshotPayloadDto
        {
            SchemaVersion = 1,
            Entries = [new EntryPayloadDto { Path = "Song.flp", Hash = _blob.ToString(), BlobId = _blob.ToString(), Size = 5 }]
        }));
    }

    [Fact]
    public async Task Preview_DoesNotRequestBlobBodies_AndReportsPlannedUpdates()
    {
        var tip = Commit();
        Advertise(tip);
        var result = await _fetch.ExecuteAsync(new FetchRequest("repo", DryRun: true));
        result.ObjectsFetched.Should().Be(3);
        result.UpdatedRefs.Should().ContainSingle().Which.NewCommit.Should().Be(tip);
        await _transport.DidNotReceive().OpenObjectPayloadStreamAsync(_blob, Arg.Any<CancellationToken>());
        await _context.DidNotReceiveWithAnyArgs().UpdateRemoteTrackingCommitAsync(default!, default, default, default);
        _ = _context.DidNotReceive().StagingIndex;
    }

    [Fact]
    public async Task Preview_RejectsUnsupportedSchema()
    {
        var tip = new CommitId(Add(ObjectType.Commit, CanonicalJsonSerializer.SerializeCanonical(new CommitPayloadDto
        { SchemaVersion = 2, SnapshotId = _snapshot.ToString() })));
        Advertise(tip);
        await Assert.ThrowsAsync<UnsupportedEnvelopeVersionException>(() => _fetch.ExecuteAsync(new FetchRequest("repo", DryRun: true)));
    }

    [Fact]
    public async Task Preview_RejectsInvalidParentHash()
    {
        var tip = new CommitId(Add(ObjectType.Commit, CanonicalJsonSerializer.SerializeCanonical(new CommitPayloadDto
        { SchemaVersion = 1, SnapshotId = _snapshot.ToString(), Parents = ["not-a-hash"] })));
        Advertise(tip);
        await Assert.ThrowsAsync<InvalidEnvelopeException>(() => _fetch.ExecuteAsync(new FetchRequest("repo", DryRun: true)));
    }

    [Fact]
    public async Task Preview_RejectsMalformedSnapshotJsonAsIntegrityFailure()
    {
        var snapshot = Add(ObjectType.Snapshot, "{"u8.ToArray());
        var tip = new CommitId(Add(ObjectType.Commit, CanonicalJsonSerializer.SerializeCanonical(new CommitPayloadDto
        { SchemaVersion = 1, SnapshotId = snapshot.ToString() })));
        Advertise(tip);
        await Assert.ThrowsAsync<InvalidEnvelopeException>(() => _fetch.ExecuteAsync(new FetchRequest("repo", DryRun: true)));
    }

    [Fact]
    public async Task Preview_RejectsOversizedMetadataBeforeReadingPayload()
    {
        var hash = new ContentHash(new byte[32]);
        var bytes = new byte[ObjectEnvelopeHeader.HeaderSize];
        new ObjectEnvelopeHeader(ObjectType.Commit, (ulong)IncomingObjectVerifier.MaxMetadataPayloadBytes + 1, hash).WriteTo(bytes);
        _envelopes[hash] = bytes;
        Advertise(new CommitId(hash));
        await Assert.ThrowsAsync<InvalidEnvelopeException>(() => _fetch.ExecuteAsync(new FetchRequest("repo", DryRun: true)));
    }

    [Fact]
    public async Task Preview_RejectsAggregateMetadataBeyondBudget()
    {
        var message = new string('x', (int)IncomingObjectVerifier.MaxMetadataPayloadBytes - 2048);
        CommitId? tip = null;
        for (var i = 0; i < 33; i++) tip = Commit(tip, message);
        Advertise(tip!.Value);
        var error = await Assert.ThrowsAsync<InvalidEnvelopeException>(() => _fetch.ExecuteAsync(new FetchRequest("repo", DryRun: true)));
        error.Message.Should().Contain("aggregate memory budget");
    }

    [Fact]
    public void PreviewSnapshot_CopiesCallerOwnedCollections()
    {
        var tip = Commit();
        var parent = Commit(message: "parent");
        var links = new List<CommitId> { parent };
        var refs = new List<RemoteRef> { new("main", tip) };
        var preview = new FetchPreview(refs, new Dictionary<CommitId, IReadOnlyList<CommitId>> { [tip] = links });
        refs.Clear(); links.Clear();
        preview.RemoteRefs.Should().ContainSingle();
        preview.Parents[tip].Should().Equal(parent);
        Action mutate = () => ((IDictionary<CommitId, IReadOnlyList<CommitId>>)preview.Parents).Clear();
        mutate.Should().Throw<NotSupportedException>();
    }

    private CommitId Commit(CommitId? parent = null, string message = "test") => new(Add(ObjectType.Commit,
        CanonicalJsonSerializer.SerializeCanonical(new CommitPayloadDto
        { SchemaVersion = 1, SnapshotId = _snapshot.ToString(), Parents = parent.HasValue ? [parent.Value.ToString()] : [], Message = message })));

    private ContentHash Add(ObjectType type, byte[] payload)
    {
        var hash = Blake3ContentHasher.Hash(payload);
        var envelope = new byte[ObjectEnvelopeHeader.HeaderSize + payload.Length];
        new ObjectEnvelopeHeader(type, (ulong)payload.Length, hash).WriteTo(envelope);
        payload.CopyTo(envelope, ObjectEnvelopeHeader.HeaderSize);
        _envelopes[hash] = envelope;
        return hash;
    }

    private void Advertise(CommitId tip) => _transport.ListRefsAsync(Arg.Any<CancellationToken>()).Returns(new RemoteRef[] { new("main", tip) });
}
