using DawVcs.Adapters.Abstractions;
using DawVcs.Application.Adapters;
using DawVcs.Application.Common;
using DawVcs.Application.Scanning;
using DawVcs.Domain.Artifacts;
using DawVcs.Domain.Common;
using DawVcs.Domain.Configuration;
using DawVcs.Domain.Hashing;
using DawVcs.Domain.Repositories;
using DawVcs.Domain.Storage;

using FluentAssertions;

using NSubstitute;

using Xunit;

namespace DawVcs.Application.Tests.Scanning;

public sealed class OperationProgressUseCaseTests : IDisposable
{
    private static readonly string[] FlpExtensions = [".flp"];

    private readonly string _root = Path.Combine(Path.GetTempPath(), "dawvc_progress_" + Guid.NewGuid().ToString("N"));
    private readonly IRepositoryContext _context = Substitute.For<IRepositoryContext>();
    private readonly IStagingIndex _index = Substitute.For<IStagingIndex>();
    private readonly IDawAdapter _adapter = Substitute.For<IDawAdapter>();
    private readonly DawAdapterRegistry _registry = new();

    public OperationProgressUseCaseTests()
    {
        Directory.CreateDirectory(_root);
        File.WriteAllBytes(Path.Combine(_root, "Main.flp"), [1, 2, 3]);
        _context.RootPath.Returns(_root);
        _context.LoadConfig().Returns(new RepositoryConfig(RepositoryId.New(), "Progress", new ArtifactPath("Main.flp")));
        _context.GetCurrentBranch().Returns(BranchName.Main);
        _context.StagingIndex.Returns(_index);
        _index.GetStagedEntriesAsync(Arg.Any<CancellationToken>()).Returns(Array.Empty<ArtifactEntry>());
        _adapter.DawName.Returns("Test DAW");
        _adapter.SupportedExtensions.Returns(FlpExtensions);
        _adapter.DetectAsync(Arg.Any<ArtifactReadContext>(), Arg.Any<CancellationToken>())
            .Returns(ProjectDetectionResult.Valid("Test DAW", "1"));
        _registry.Register(_adapter);
    }

    public void Dispose()
    {
        Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public async Task Scan_ReportsIndeterminateStagesAndPreservesLegacyMethodGroup()
    {
        var useCase = new ScanUseCase(_ => _context, _registry);
        Func<ScanRequest, CancellationToken, Task<ScanResult>> legacy = useCase.ExecuteAsync;
        var request = new ScanRequest(_root);
        var expected = await legacy(request, default);
        var progress = new RecordingProgress();

        var actual = await useCase.ExecuteAsync(request, CancellationToken.None, progress);

        actual.Should().BeEquivalentTo(expected, options => options.Excluding(result => result.Duration));
        progress.Reports.Select(report => report.Stage).Should().Equal(
            OperationStage.OpeningRepository, OperationStage.DiscoveringFiles,
            OperationStage.InspectingArtifact, OperationStage.Completed);
        progress.Reports.Should().OnlyContain(report => report.Total == null);
    }

    [Fact]
    public async Task Scan_UnknownAdapter_CompletesWithFindings()
    {
        var useCase = new ScanUseCase(_ => _context, new DawAdapterRegistry());
        var progress = new RecordingProgress();

        var result = await useCase.ExecuteAsync(new ScanRequest(_root), default, progress);

        result.Detection.Status.Should().Be(ProjectDetectionStatus.Unknown);
        progress.Reports.Last().Stage.Should().Be(OperationStage.Completed);
    }

    [Fact]
    public async Task Scan_MissingArtifact_DoesNotReportCompletion()
    {
        var progress = new RecordingProgress();
        var useCase = new ScanUseCase(_ => _context, _registry);
        var act = () => useCase.ExecuteAsync(new ScanRequest(_root, "Missing.flp"), default, progress);

        await act.Should().ThrowAsync<FileNotFoundException>();
        progress.Reports.Should().NotContain(report => report.Stage == OperationStage.Completed);
        _context.Received(1).Dispose();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task PreCancelledScanOrStatus_DoesNotConstructContext(bool scan)
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var calls = 0;
        IRepositoryContext Factory(string _)
        {
            calls++;
            return _context;
        }
        var progress = new RecordingProgress();
        Func<Task> act = scan
            ? async () => await new ScanUseCase(Factory, _registry).ExecuteAsync(new ScanRequest(_root), cancellation.Token, progress)
            : async () => await new StatusUseCase(Factory).ExecuteAsync(new StatusRequest(_root), cancellation.Token, progress);

        var assertion = await act.Should().ThrowAsync<OperationCanceledException>();

        assertion.Which.CancellationToken.Should().Be(cancellation.Token);
        calls.Should().Be(0);
        progress.Reports.Should().NotContain(report => report.Stage == OperationStage.Completed);
    }

    [Fact]
    public async Task Scan_AdapterIgnoresCancellation_DoesNotReportCompletion()
    {
        using var cancellation = new CancellationTokenSource();
        _adapter.DetectAsync(Arg.Any<ArtifactReadContext>(), Arg.Any<CancellationToken>()).Returns(_ =>
        {
            cancellation.Cancel();
            return ProjectDetectionResult.Valid("Test DAW", "1");
        });
        var progress = new RecordingProgress();
        var act = () => new ScanUseCase(_ => _context, _registry)
            .ExecuteAsync(new ScanRequest(_root), cancellation.Token, progress);

        await act.Should().ThrowAsync<OperationCanceledException>();
        progress.Reports.Should().NotContain(report => report.Stage == OperationStage.Completed);
        _context.Received(1).Dispose();
    }

    [Fact]
    public async Task Scan_ObserverFailureIsNotConvertedIntoAdapterFinding()
    {
        var failure = new InvalidOperationException("Progress consumer failed.");
        var progress = new RecordingProgress(report =>
        {
            if (report.Stage == OperationStage.InspectingArtifact) throw failure;
        });
        var act = () => new ScanUseCase(_ => _context, _registry).ExecuteAsync(new ScanRequest(_root), default, progress);

        var assertion = await act.Should().ThrowAsync<InvalidOperationException>();

        assertion.Which.Should().BeSameAs(failure);
        _context.Received(1).Dispose();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Status_ReportsMonotonicStageCountsWithoutChangingResults(bool cached)
    {
        for (var i = 0; i < 16; i++)
        {
            await File.WriteAllBytesAsync(Path.Combine(_root, $"sample{i}.wav"), [1, 2, 3]);
        }
        if (cached)
        {
            _index.TryGetCachedHashAsync(Arg.Any<ArtifactPath>(), Arg.Any<long>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
                .Returns((ContentHash?)Blake3ContentHasher.Hash([1, 2, 3]));
        }
        var useCase = new StatusUseCase(_ => _context);
        var request = new StatusRequest(_root);
        var expected = await ((IUseCase<StatusRequest, StatusResult>)useCase).ExecuteAsync(request, default);
        var progress = new RecordingProgress();

        var actual = await useCase.ExecuteAsync(request, default, progress);

        actual.Should().BeEquivalentTo(expected);
        progress.Reports.Select(report => report.Stage).Distinct().Should().Equal(
            OperationStage.LoadingMetadata, OperationStage.DiscoveringFiles, OperationStage.CheckingCache,
            OperationStage.HashingFiles, OperationStage.ClassifyingChanges, OperationStage.Completed);
        var checks = progress.Reports.Where(report => report.Stage == OperationStage.CheckingCache).ToArray();
        checks.Select(report => report.Completed).Should().Equal(Enumerable.Range(0, 18).Select(value => (long)value));
        checks.Should().OnlyContain(report => report.Total == 17 && report.Unit == OperationProgressUnit.Items);
        var hashes = progress.Reports.Where(report => report.Stage == OperationStage.HashingFiles).ToArray();
        var misses = cached ? 0 : 17;
        hashes.Select(report => report.Completed).Should().Equal(Enumerable.Range(0, misses + 1).Select(value => (long)value));
        hashes.Should().OnlyContain(report => report.Total == misses);
    }

    [Fact]
    public async Task Status_EmptyWorkspace_ReportsKnownZeroInsteadOfUnknownTotal()
    {
        File.Delete(Path.Combine(_root, "Main.flp"));
        var progress = new RecordingProgress();

        await new StatusUseCase(_ => _context).ExecuteAsync(new StatusRequest(_root), default, progress);

        progress.Reports.Where(report => report.Stage is OperationStage.CheckingCache or OperationStage.HashingFiles)
            .Should().HaveCount(2).And.OnlyContain(report => report.Total == 0 && report.Completed == 0);
        progress.Reports.Last().Stage.Should().Be(OperationStage.Completed);
    }

    [Theory]
    [InlineData(OperationStage.DiscoveringFiles)]
    [InlineData(OperationStage.CheckingCache)]
    [InlineData(OperationStage.HashingFiles)]
    [InlineData(OperationStage.ClassifyingChanges)]
    public async Task Status_CancellationAtStageBoundary_DoesNotReportCompletion(OperationStage cancelAt)
    {
        using var cancellation = new CancellationTokenSource();
        var progress = new RecordingProgress(report =>
        {
            if (report.Stage == cancelAt) cancellation.Cancel();
        });
        var act = () => new StatusUseCase(_ => _context).ExecuteAsync(new StatusRequest(_root), cancellation.Token, progress);

        await act.Should().ThrowAsync<OperationCanceledException>();
        progress.Reports.Should().NotContain(report => report.Stage == OperationStage.Completed);
        _context.Received(1).Dispose();
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
