using DawVcs.Application.Remotes;
using DawVcs.Domain.Common;
using DawVcs.Domain.Entities;
using DawVcs.Domain.Repositories;

using FluentAssertions;

using NSubstitute;

using Xunit;

namespace DawVcs.Application.Tests.Remotes;

public sealed class AncestryServiceTests
{
    private readonly IRepositoryContext _context = Substitute.For<IRepositoryContext>();
    private readonly AncestryService _service = new();

    private readonly CommitId _c1 = CommitId.Parse(new string('1', 64));
    private readonly CommitId _c2 = CommitId.Parse(new string('2', 64));
    private readonly CommitId _c3 = CommitId.Parse(new string('3', 64));
    private readonly CommitId _c4 = CommitId.Parse(new string('4', 64));

    [Fact]
    public async Task DetermineSyncStateAsync_NullLocalTip_ReturnsFastForward()
    {
        var result = await _service.DetermineSyncStateAsync(null, _c1, _context);

        result.State.Should().Be(SyncAncestryState.FastForward);
    }

    [Fact]
    public async Task DetermineSyncStateAsync_EqualTips_ReturnsUpToDate()
    {
        var result = await _service.DetermineSyncStateAsync(_c1, _c1, _context);

        result.State.Should().Be(SyncAncestryState.UpToDate);
    }

    [Fact]
    public async Task DetermineSyncStateAsync_LocalIsAncestorOfRemote_ReturnsFastForward()
    {
        // c1 -> c2 -> c3
        SetupCommit(_c1, Array.Empty<CommitId>());
        SetupCommit(_c2, [_c1]);
        SetupCommit(_c3, [_c2]);

        var result = await _service.DetermineSyncStateAsync(_c1, _c3, _context);

        result.State.Should().Be(SyncAncestryState.FastForward);
        result.CommitsBehind.Should().Be(2);
    }

    [Fact]
    public async Task DetermineSyncStateAsync_RemoteIsAncestorOfLocal_ReturnsLocalAhead()
    {
        // c1 -> c2 -> c3
        SetupCommit(_c1, Array.Empty<CommitId>());
        SetupCommit(_c2, [_c1]);
        SetupCommit(_c3, [_c2]);

        var result = await _service.DetermineSyncStateAsync(_c3, _c1, _context);

        result.State.Should().Be(SyncAncestryState.LocalAhead);
        result.CommitsAhead.Should().Be(2);
    }

    [Fact]
    public async Task DetermineSyncStateAsync_NeitherIsAncestor_ReturnsDiverged()
    {
        // c1 -> c2
        // c1 -> c3 (diverged!)
        SetupCommit(_c1, Array.Empty<CommitId>());
        SetupCommit(_c2, [_c1]);
        SetupCommit(_c3, [_c1]);

        var result = await _service.DetermineSyncStateAsync(_c2, _c3, _context);

        result.State.Should().Be(SyncAncestryState.Diverged);
    }

    private void SetupCommit(CommitId id, IReadOnlyList<CommitId> parents)
    {
        var dummySnapshotId = SnapshotId.Parse(new string('a', 64));
        var commit = new Commit(parents, dummySnapshotId, "Author", DateTimeOffset.UtcNow, "Message", id);
        _context.LoadCommitAsync(id, Arg.Any<CancellationToken>()).Returns(commit);
    }
}
