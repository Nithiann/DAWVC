using DawVcs.Application.Checkouts;
using DawVcs.Application.Common;
using DawVcs.Application.Exceptions;
using DawVcs.Domain.Artifacts;
using DawVcs.Domain.Common;
using DawVcs.Domain.Configuration;
using DawVcs.Domain.Entities;
using DawVcs.Domain.Hashing;
using DawVcs.Domain.Repositories;
using DawVcs.Domain.Storage;

using FluentAssertions;

using NSubstitute;

using Xunit;

namespace DawVcs.Application.Tests.Checkouts;

public sealed class CheckoutProgressTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "dawvc_checkout_progress_" + Guid.NewGuid().ToString("N"));
    private readonly IRepositoryContext _context = Substitute.For<IRepositoryContext>();
    private readonly IObjectStore _objects = Substitute.For<IObjectStore>();
    private readonly IStagingIndex _index = Substitute.For<IStagingIndex>();
    private readonly CheckoutUseCase _checkout;
    private readonly CheckoutRestoreUseCase _restore;
    private Commit _commit = null!;
    private ProjectSnapshot _snapshot = null!;
    private readonly byte[] _content = [1, 2, 3, 4];

    public CheckoutProgressTests()
    {
        Directory.CreateDirectory(_root);
        _context.RootPath.Returns(_root);
        _context.ObjectStore.Returns(_objects);
        _context.StagingIndex.Returns(_index);
        _context.LoadConfig().Returns(new RepositoryConfig(RepositoryId.New(), "Progress", new ArtifactPath("Main.flp")));
        // An unborn current branch keeps nested Status clean while main names the checkout target.
        _context.GetCurrentBranch().Returns(new BranchName("workspace"));
        _index.GetStagedEntriesAsync(Arg.Any<CancellationToken>()).Returns(Array.Empty<ArtifactEntry>());
        _objects.Exists(Arg.Any<ContentHash>()).Returns(true);
        _objects.OpenPayloadStreamAsync(Arg.Any<ContentHash>(), Arg.Any<CancellationToken>())
            .Returns(_ => new MemoryStream(_content));
        _checkout = new CheckoutUseCase(_ => _context);
        _restore = new CheckoutRestoreUseCase(_ => _context);
        SetSnapshot(2);
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Theory]
    [InlineData(0)] // External checkout
    [InlineData(1)] // Restore delegator
    [InlineData(2)] // Fast-forward with nested Status
    [InlineData(3)] // In-place checkout with nested Status
    public async Task Success_ReportsVerifiedCountsAndCompletesAfterCleanup(int operation)
    {
        var progress = new RecordingProgress(report =>
        {
            if (report.Stage == OperationStage.StagingFiles && report.Completed > 0)
            {
                var staging = Directory.GetDirectories(_root, ".staging_checkout_*", SearchOption.AllDirectories).Single();
                var stagedFiles = Directory.GetFiles(staging, "*", SearchOption.AllDirectories);
                stagedFiles.Should().HaveCount((int)report.Completed);
                foreach (var file in stagedFiles)
                {
                    File.ReadAllBytes(file).Should().Equal(_content);
                }
            }
            if (report.Stage == OperationStage.Completed)
            {
                AssertCleanedUp();
                _context.Received(operation >= 2 ? 2 : 1).Dispose();
            }
        });

        await RunAsync(operation, progress);

        var stagingReports = progress.Reports.Where(report => report.Stage == OperationStage.StagingFiles).ToArray();
        stagingReports.Select(report => report.Completed).Should().Equal(0L, 1L, 2L);
        stagingReports.Should().OnlyContain(report => report.Total == 2 && report.Unit == OperationProgressUnit.Items);
        var stages = progress.Reports.Select(report => report.Stage).ToList();
        stages[0].Should().Be(OperationStage.OpeningRepository);
        stages[1].Should().Be(OperationStage.LoadingMetadata);
        stages.IndexOf(OperationStage.VerifyingObjects).Should().BeLessThan(stages.IndexOf(OperationStage.PlanningCheckout));
        stages.IndexOf(OperationStage.PlanningCheckout).Should().BeLessThan(stages.IndexOf(OperationStage.StagingFiles));
        stages.IndexOf(OperationStage.StagingFiles).Should().BeLessThan(stages.IndexOf(OperationStage.PublishingFiles));
        stages.IndexOf(OperationStage.PublishingFiles).Should().BeLessThan(stages.IndexOf(OperationStage.ResolvingDependencies));
        stages.Should().ContainSingle(stage => stage == OperationStage.Completed);
        stages.Last().Should().Be(OperationStage.Completed);
        progress.Reports.Where(report => report.Stage is OperationStage.PlanningCheckout or OperationStage.PublishingFiles or OperationStage.ResolvingDependencies)
            .Should().OnlyContain(report => report.Total == null);
        if (operation >= 2)
        {
            stages.Should().Contain(OperationStage.DiscoveringFiles).And.Contain(OperationStage.ClassifyingChanges);
        }
        foreach (var entry in _snapshot.Project.Root.GetEntries())
        {
            File.ReadAllBytes(Path.Combine(TargetDirectory(operation), entry.Path.Value)).Should().Equal(_content);
        }
        if (operation == 2)
        {
            await _context.Received(1).UpdateBranchCommitAsync(BranchName.Main, _commit.Id, Arg.Any<CancellationToken>());
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public async Task EmptySnapshot_ReportsKnownZeroAndSingleOuterCompletion(int operation)
    {
        SetSnapshot(0);
        var progress = new RecordingProgress();

        await RunAsync(operation, progress);

        progress.Reports.Where(report => report.Stage == OperationStage.StagingFiles)
            .Should().ContainSingle().Which.Should().Be(new OperationProgress(OperationStage.StagingFiles, 0, 0));
        progress.Reports.Count(report => report.Stage == OperationStage.Completed).Should().Be(1);
        progress.Reports.Last().Stage.Should().Be(OperationStage.Completed);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public async Task CorruptPayload_DoesNotCountUnverifiedFileOrReportCompletion(int operation)
    {
        _objects.OpenPayloadStreamAsync(Arg.Any<ContentHash>(), Arg.Any<CancellationToken>())
            .Returns(_ => new MemoryStream(new byte[] { 9 }));
        var progress = new RecordingProgress();
        var act = () => RunAsync(operation, progress);

        await act.Should().ThrowAsync<CheckoutStagingException>().WithMessage("*Integrity verification failed*");

        progress.Reports.Where(report => report.Stage == OperationStage.StagingFiles)
            .Should().ContainSingle().Which.Completed.Should().Be(0);
        progress.Reports.Should().NotContain(report => report.Stage == OperationStage.Completed || report.Stage == OperationStage.PublishingFiles);
        File.Exists(Path.Combine(TargetDirectory(operation), "Track0.flp")).Should().BeFalse();
        AssertCleanedUp();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public async Task MissingCommit_DoesNotReportCompletion(int operation)
    {
        _context.LoadCommitAsync(_commit.Id, Arg.Any<CancellationToken>()).Returns((Commit?)null);
        var progress = new RecordingProgress();
        var act = () => RunAsync(operation, progress);

        await act.Should().ThrowAsync<InvalidOperationException>();

        progress.Reports.Should().NotContain(report => report.Stage == OperationStage.Completed);
    }

    [Theory]
    [InlineData(0, OperationStage.StagingFiles)]
    [InlineData(1, OperationStage.StagingFiles)]
    [InlineData(2, OperationStage.StagingFiles)]
    [InlineData(3, OperationStage.StagingFiles)]
    [InlineData(0, OperationStage.PublishingFiles)]
    [InlineData(1, OperationStage.PublishingFiles)]
    [InlineData(2, OperationStage.PublishingFiles)]
    [InlineData(3, OperationStage.PublishingFiles)]
    public async Task ObserverFailureBeforePublication_PropagatesWithoutMutatingWorkspace(int operation, OperationStage failAt)
    {
        var failure = new InvalidOperationException("Observer failed");
        var progress = new RecordingProgress(report =>
        {
            if (report.Stage == failAt && (failAt != OperationStage.StagingFiles || report.Completed == 1)) throw failure;
        });
        var act = () => RunAsync(operation, progress);

        var assertion = await act.Should().ThrowAsync<InvalidOperationException>();

        assertion.Which.Should().BeSameAs(failure);
        progress.Reports.Should().NotContain(report => report.Stage == OperationStage.Completed);
        File.Exists(Path.Combine(TargetDirectory(operation), "Track0.flp")).Should().BeFalse();
        await _context.DidNotReceive().UpdateBranchCommitAsync(Arg.Any<BranchName>(), Arg.Any<CommitId>(), Arg.Any<CancellationToken>());
        AssertCleanedUp();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public async Task ObserverFailureAfterPublication_DoesNotRollbackPublishedFiles(int operation)
    {
        var failure = new InvalidOperationException("Observer failed after publication");
        var progress = new RecordingProgress(report =>
        {
            if (report.Stage == OperationStage.ResolvingDependencies) throw failure;
        });
        var act = () => RunAsync(operation, progress);

        var assertion = await act.Should().ThrowAsync<InvalidOperationException>();

        assertion.Which.Should().BeSameAs(failure);
        File.ReadAllBytes(Path.Combine(TargetDirectory(operation), "Track0.flp")).Should().Equal(_content);
        progress.Reports.Should().NotContain(report => report.Stage == OperationStage.Completed);
        AssertCleanedUp();
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    public async Task IndexFailure_RollsBackPublicationAndDoesNotReportCompletion(int operation)
    {
        var failure = new IOException("Index write failed");
        _index.ClearStagedEntriesAsync(Arg.Any<CancellationToken>()).Returns(Task.FromException(failure));
        var progress = new RecordingProgress();
        var act = () => RunAsync(operation, progress);

        var assertion = await act.Should().ThrowAsync<IOException>();

        assertion.Which.Should().BeSameAs(failure);
        progress.Reports.Should().Contain(report => report.Stage == OperationStage.PublishingFiles);
        progress.Reports.Should().NotContain(report => report.Stage == OperationStage.Completed);
        File.Exists(Path.Combine(_root, "Track0.flp")).Should().BeFalse();
        File.Exists(Path.Combine(_root, "Track1.flp")).Should().BeFalse();
        AssertCleanedUp();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public async Task ObservedCancellationDuringStaging_DoesNotReportCompletion(int operation)
    {
        using var cancellation = new CancellationTokenSource();
        var progress = new RecordingProgress(report =>
        {
            if (report.Stage == OperationStage.StagingFiles && report.Completed == 1) cancellation.Cancel();
        });
        var act = () => RunAsync(operation, progress, cancellation.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();

        progress.Reports.Should().NotContain(report => report.Stage == OperationStage.Completed);
        File.Exists(Path.Combine(TargetDirectory(operation), "Track0.flp")).Should().BeFalse();
        AssertCleanedUp();
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    public async Task UnchangedFiles_AreNotIncludedInStagingTotal(int operation)
    {
        _context.GetBranchCommit(new BranchName("workspace")).Returns(_commit.Id);
        foreach (var entry in _snapshot.Project.Root.GetEntries())
        {
            await File.WriteAllBytesAsync(Path.Combine(_root, entry.Path.Value), _content);
        }
        var progress = new RecordingProgress();

        await RunAsync(operation, progress);

        progress.Reports.Where(report => report.Stage == OperationStage.StagingFiles)
            .Should().ContainSingle().Which.Should().Be(new OperationProgress(OperationStage.StagingFiles, 0, 0));
        await _objects.DidNotReceive().OpenPayloadStreamAsync(Arg.Any<ContentHash>(), Arg.Any<CancellationToken>());
        progress.Reports.Last().Stage.Should().Be(OperationStage.Completed);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Force_ReportsRecoveryBeforeCopyAndStaging(bool fastForward)
    {
        var localContent = new byte[] { 9, 8, 7 };
        await File.WriteAllBytesAsync(Path.Combine(_root, "Track0.flp"), localContent);
        var progress = new RecordingProgress(report =>
        {
            if (report.Stage == OperationStage.CreatingRecoveryCopy)
            {
                report.Total.Should().BeNull();
                Directory.Exists(Path.Combine(_root, ".dawvc", "recovery")).Should().BeFalse();
            }
        });

        var result = fastForward
            ? await _checkout.ExecuteFastForwardAsync(new FastForwardTarget(_root, BranchName.Main, null, _commit.Id), true, default, progress)
            : await _checkout.ExecuteAsync(new CheckoutRequest(_root, "main", Force: true), default, progress);

        result.RecoveryDirectory.Should().NotBeNull();
        File.ReadAllBytes(Path.Combine(result.RecoveryDirectory!, "Track0.flp")).Should().Equal(localContent);
        var stages = progress.Reports.Select(report => report.Stage).ToList();
        stages.IndexOf(OperationStage.CreatingRecoveryCopy).Should().BeLessThan(stages.IndexOf(OperationStage.StagingFiles));
        stages.Last().Should().Be(OperationStage.Completed);
        AssertCleanedUp();
    }

    [Fact]
    public async Task LegacyMethodGroupsAndInterfaces_PreserveResults()
    {
        SetSnapshot(0);
        Func<CheckoutRequest, CancellationToken, Task<CheckoutResult>> checkout = _checkout.ExecuteAsync;
        Func<CheckoutRestoreRequest, CancellationToken, Task<CheckoutRestoreResult>> restore = _restore.ExecuteAsync;
        Func<FastForwardTarget, bool, CancellationToken, Task<CheckoutResult>> fastForward = _checkout.ExecuteFastForwardAsync;
        var request = new CheckoutRequest(_root, "main", TargetDirectory(0));
        var restoreRequest = new CheckoutRestoreRequest(_root, "main", TargetDirectory(1));
        var target = new FastForwardTarget(_root, BranchName.Main, null, _commit.Id);

        var checkoutResult = await checkout(request, default);
        (await _checkout.ExecuteAsync(request, default, new RecordingProgress())).Should().BeEquivalentTo(checkoutResult);
        (await ((IUseCase<CheckoutRequest, CheckoutResult>)_checkout).ExecuteAsync(request, default)).Should().BeEquivalentTo(checkoutResult);
        var restoreResult = await restore(restoreRequest, default);
        (await _restore.ExecuteAsync(restoreRequest, default, new RecordingProgress())).Should().BeEquivalentTo(restoreResult);
        (await ((IUseCase<CheckoutRestoreRequest, CheckoutRestoreResult>)_restore).ExecuteAsync(restoreRequest, default)).Should().BeEquivalentTo(restoreResult);
        var fastForwardResult = await fastForward(target, false, default);
        (await _checkout.ExecuteFastForwardAsync(target, false, default, new RecordingProgress())).Should().BeEquivalentTo(fastForwardResult);
    }

    [Fact]
    public async Task ProgressOverloads_PreserveNonemptyResults()
    {
        var request = new CheckoutRequest(_root, "main", TargetDirectory(0));
        var expected = await _checkout.ExecuteAsync(request);
        Directory.Delete(TargetDirectory(0), recursive: true);
        var actual = await _checkout.ExecuteAsync(request, default, new RecordingProgress());
        actual.Should().BeEquivalentTo(expected);

        Directory.Delete(TargetDirectory(0), recursive: true);
        var restoreRequest = new CheckoutRestoreRequest(_root, "main", TargetDirectory(1));
        var expectedRestore = await _restore.ExecuteAsync(restoreRequest);
        Directory.Delete(TargetDirectory(1), recursive: true);
        var actualRestore = await _restore.ExecuteAsync(restoreRequest, default, new RecordingProgress());
        actualRestore.Should().BeEquivalentTo(expectedRestore);

        var target = new FastForwardTarget(_root, BranchName.Main, null, _commit.Id);
        var expectedFastForward = await _checkout.ExecuteFastForwardAsync(target);
        foreach (var entry in _snapshot.Project.Root.GetEntries())
        {
            File.Delete(Path.Combine(_root, entry.Path.Value));
        }
        var actualFastForward = await _checkout.ExecuteFastForwardAsync(target, false, default, new RecordingProgress());
        actualFastForward.Should().BeEquivalentTo(expectedFastForward);
    }

    private void SetSnapshot(int count)
    {
        var hash = Blake3ContentHasher.Hash(_content);
        var entries = Enumerable.Range(0, count)
            .Select(i => ArtifactEntry.Create(new ArtifactPath($"Track{i}.flp"), hash, _content.Length)).ToArray();
        _snapshot = new ProjectSnapshot(new ProjectArtifact("Test DAW", new DirectoryArtifact(entries)), DateTimeOffset.UtcNow);
        _commit = new Commit(Array.Empty<CommitId>(), _snapshot.Id, "Producer", DateTimeOffset.UtcNow, "Test");
        _context.ResolveReference("main").Returns(_commit.Id);
        _context.GetBranchCommit(BranchName.Main).Returns(_commit.Id);
        _context.LoadCommitAsync(_commit.Id, Arg.Any<CancellationToken>()).Returns(_commit);
        _context.LoadSnapshotAsync(_snapshot.Id, Arg.Any<CancellationToken>()).Returns(_snapshot);
    }

    private string TargetDirectory(int operation) => operation >= 2 ? _root : Path.Combine(_root, ".restore");

    private async Task RunAsync(int operation, IProgress<OperationProgress> progress, CancellationToken cancellationToken = default)
    {
        switch (operation)
        {
            case 0:
            case 3:
                await _checkout.ExecuteAsync(new CheckoutRequest(_root, "main", operation == 0 ? TargetDirectory(operation) : null), cancellationToken, progress);
                break;
            case 1:
                await _restore.ExecuteAsync(new CheckoutRestoreRequest(_root, "main", TargetDirectory(operation)), cancellationToken, progress);
                break;
            case 2:
                await _checkout.ExecuteFastForwardAsync(new FastForwardTarget(_root, BranchName.Main, null, _commit.Id), false, cancellationToken, progress);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(operation));
        }
    }

    private void AssertCleanedUp()
    {
        Directory.GetDirectories(_root, ".staging_checkout_*", SearchOption.AllDirectories).Should().BeEmpty();
        Directory.GetDirectories(_root, ".journal_backup_*", SearchOption.AllDirectories).Should().BeEmpty();
    }

    private sealed class RecordingProgress(Action<OperationProgress>? onReport = null) : IProgress<OperationProgress>
    {
        public List<OperationProgress> Reports { get; } = new();

        public void Report(OperationProgress value)
        {
            Reports.Add(value);
            onReport?.Invoke(value);
        }
    }
}
