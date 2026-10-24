using DawVcs.Application.Commits;
using DawVcs.Application.Common;
using DawVcs.Domain.Artifacts;
using DawVcs.Domain.Common;
using DawVcs.Domain.Configuration;
using DawVcs.Domain.Hashing;
using DawVcs.Domain.Repositories;
using DawVcs.Domain.Storage;

using FluentAssertions;

using NSubstitute;

using Xunit;

namespace DawVcs.Application.Tests.Commits;

public sealed class CommitProgressTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "dawvc_commit_progress_" + Guid.NewGuid().ToString("N"));
    private readonly IRepositoryContext _context = Substitute.For<IRepositoryContext>();
    private readonly IObjectStore _store = Substitute.For<IObjectStore>();
    private readonly IStagingIndex _index = Substitute.For<IStagingIndex>();

    public CommitProgressTests()
    {
        Directory.CreateDirectory(_root);
        File.WriteAllBytes(Path.Combine(_root, "Song.flp"), [1, 2, 3]);
        _context.RootPath.Returns(_root);
        _context.LoadConfig().Returns(new RepositoryConfig(RepositoryId.New(), "Song", new ArtifactPath("Song.flp")));
        _context.GetCurrentBranch().Returns(BranchName.Main);
        _context.StagingIndex.Returns(_index);
        _context.ObjectStore.Returns(_store);
        _index.GetStagedEntriesAsync(Arg.Any<CancellationToken>()).Returns(Array.Empty<ArtifactEntry>());
        _store.WriteBlobAsync(Arg.Any<Stream>(), Arg.Any<CancellationToken>()).Returns(Blake3ContentHasher.Hash([1, 2, 3]));
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public async Task Commit_ReportsStagesCountsAndCompletionAfterDisposal()
    {
        var reports = new List<OperationProgress>();
        var disposed = false;
        _context.When(context => context.Dispose()).Do(_ => disposed = true);
        var progress = new CallbackProgress(value =>
        {
            if (value.Stage == OperationStage.Completed) disposed.Should().BeTrue();
            reports.Add(value);
        });

        var result = await new CommitUseCase(_ => _context).ExecuteAsync(new CommitRequest(_root, "Initial", "Author"), default, progress);

        result.Message.Should().Be("Initial");
        reports.Select(value => value.Stage).Distinct().Should().Equal(
            OperationStage.OpeningRepository, OperationStage.LoadingMetadata, OperationStage.WritingObjects,
            OperationStage.DiscoveringDependencies, OperationStage.ResolvingDependencies,
            OperationStage.UpdatingReferences, OperationStage.Completed);
        reports.Should().ContainSingle(value => value.Stage == OperationStage.Completed);
        reports.Where(value => value.Stage == OperationStage.WritingObjects && value.Total == 2)
            .Select(value => value.Completed).Should().Equal(0, 1, 2);
        reports.Should().Contain(value => value.Stage == OperationStage.ResolvingDependencies && value.Total == 0 && value.Completed == 0);
        await _context.Received(1).UpdateBranchCommitAsync(BranchName.Main, result.CommitId, Arg.Any<CancellationToken>());
        await _index.Received(1).ClearStagedEntriesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Commit_PreservesLegacyMethodGroupAndNullObserverCalls()
    {
        var useCase = new CommitUseCase(_ => _context);
        Func<CommitRequest, CancellationToken, Task<CommitResult>> legacy = useCase.ExecuteAsync;
        var request = new CommitRequest(_root, "Initial", "Author");

        var first = await legacy(request, default);
        var second = await useCase.ExecuteAsync(request, default, null);

        second.Message.Should().Be(first.Message);
        second.Author.Should().Be(first.Author);
        second.Branch.Should().Be(first.Branch);
        second.IsComplete.Should().Be(first.IsComplete);
        await _store.Received(2).WriteObjectAsync(ObjectType.Commit, Arg.Any<ReadOnlyMemory<byte>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Commit_PreCancelled_DoesNotCreateContextOrReportCompletion()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var contexts = 0;
        var reports = new List<OperationProgress>();
        var useCase = new CommitUseCase(_ => { contexts++; return _context; });
        var act = () => useCase.ExecuteAsync(new CommitRequest(_root, "Initial"), cancellation.Token, new CallbackProgress(reports.Add));

        await act.Should().ThrowAsync<OperationCanceledException>();
        contexts.Should().Be(0);
        reports.Should().BeEmpty();
    }

    [Theory]
    [InlineData(OperationStage.DiscoveringDependencies)]
    [InlineData(OperationStage.UpdatingReferences)]
    public async Task Commit_ObserverFailurePropagatesWithoutPublishingRef(OperationStage stage)
    {
        var failure = new InvalidOperationException("Observer failed");
        var reports = new List<OperationProgress>();
        var progress = new CallbackProgress(value =>
        {
            reports.Add(value);
            if (value.Stage == stage) throw failure;
        });
        var act = () => new CommitUseCase(_ => _context).ExecuteAsync(new CommitRequest(_root, "Initial"), default, progress);

        var assertion = await act.Should().ThrowAsync<InvalidOperationException>();

        assertion.Which.Should().BeSameAs(failure);
        reports.Should().NotContain(value => value.Stage == OperationStage.Completed);
        await _context.DidNotReceiveWithAnyArgs().UpdateBranchCommitAsync(default, default, default);
        _context.Received(1).Dispose();
    }

    [Fact]
    public async Task Commit_IndexFailureAfterRefUpdate_DoesNotReportCompletion()
    {
        var failure = new IOException("Index unavailable");
        _index.ClearStagedEntriesAsync(Arg.Any<CancellationToken>()).Returns(Task.FromException(failure));
        var reports = new List<OperationProgress>();
        var act = () => new CommitUseCase(_ => _context)
            .ExecuteAsync(new CommitRequest(_root, "Initial"), default, new CallbackProgress(reports.Add));

        var assertion = await act.Should().ThrowAsync<IOException>();

        assertion.Which.Should().BeSameAs(failure);
        await _context.Received(1).UpdateBranchCommitAsync(BranchName.Main, Arg.Any<CommitId>(), Arg.Any<CancellationToken>());
        reports.Should().NotContain(value => value.Stage == OperationStage.Completed);
    }

    [Fact]
    public async Task Commit_DisposalFailure_DoesNotReportCompletion()
    {
        _context.When(context => context.Dispose()).Do(_ => throw new IOException("Dispose failed"));
        var reports = new List<OperationProgress>();
        var act = () => new CommitUseCase(_ => _context)
            .ExecuteAsync(new CommitRequest(_root, "Initial"), default, new CallbackProgress(reports.Add));

        await act.Should().ThrowAsync<IOException>();
        reports.Should().NotContain(value => value.Stage == OperationStage.Completed);
    }

    private sealed class CallbackProgress(Action<OperationProgress> report) : IProgress<OperationProgress>
    {
        public void Report(OperationProgress value) => report(value);
    }
}
