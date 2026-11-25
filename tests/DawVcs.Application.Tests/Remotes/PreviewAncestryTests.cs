using DawVcs.Application.Remotes;
using DawVcs.Domain.Common;
using DawVcs.Domain.Hashing;
using DawVcs.Domain.Repositories;
using DawVcs.Domain.Storage;

using FluentAssertions;

using NSubstitute;

using Xunit;

namespace DawVcs.Application.Tests.Remotes;

public sealed class PreviewAncestryTests
{
    [Fact]
    public async Task SecondParentAncestor_IsRecognizedWithoutPersistingPreviewCommits()
    {
        var local = Id(1);
        var remote = Id(2);
        var side = Id(3);
        var context = Substitute.For<IRepositoryContext>();
        var preview = new FetchPreview([new("main", remote)], new Dictionary<CommitId, IReadOnlyList<CommitId>>
        { [remote] = [side, local], [side] = [] });
        var state = await new AncestryService().DeterminePreviewStateAsync(local, remote, context, preview);
        state.State.Should().Be(SyncAncestryState.FastForward);
        state.CommitsBehind.Should().Be(1);
        await context.DidNotReceiveWithAnyArgs().LoadCommitAsync(default, default);
    }

    [Fact]
    public async Task MissingRequiredLocalHistory_IsAnIntegrityError()
    {
        var local = Id(1);
        var remote = Id(2);
        var context = Substitute.For<IRepositoryContext>();
        var preview = new FetchPreview([new("main", remote)], new Dictionary<CommitId, IReadOnlyList<CommitId>>());
        await Assert.ThrowsAsync<InvalidEnvelopeException>(() => new AncestryService().DeterminePreviewStateAsync(local, remote, context, preview));
    }

    [Fact]
    public async Task OversizedParentTraversal_IsBounded()
    {
        var parents = new Dictionary<CommitId, IReadOnlyList<CommitId>>();
        for (var i = 1; i <= 50_001; i++) parents[Id(i)] = [Id(i + 1)];
        var preview = new FetchPreview([new("main", Id(1))], parents);
        var context = Substitute.For<IRepositoryContext>();
        var error = await Assert.ThrowsAsync<InvalidEnvelopeException>(() => new AncestryService().DeterminePreviewStateAsync(Id(0), Id(1), context, preview));
        error.Message.Should().Contain("safety limits");
    }

    private static CommitId Id(int value) => new(Blake3ContentHasher.Hash(BitConverter.GetBytes(value)));
}
