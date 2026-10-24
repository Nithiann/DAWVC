using DawVcs.Adapters.Abstractions;
using DawVcs.Application.Adapters;
using DawVcs.Application.Common;
using DawVcs.Application.Diagnostics;
using DawVcs.Domain.Artifacts;
using DawVcs.Domain.Common;
using DawVcs.Domain.Configuration;
using DawVcs.Domain.Dependencies;
using DawVcs.Domain.Entities;
using DawVcs.Domain.Hashing;
using DawVcs.Domain.Repositories;

using FluentAssertions;

using NSubstitute;
using NSubstitute.ExceptionExtensions;

using Xunit;

namespace DawVcs.Application.Tests.Diagnostics;

public sealed class DoctorUseCaseTests : IDisposable
{
    private readonly string _tempDir;
    private readonly IRepositoryContext _context;
    private readonly IDawAdapterRegistry _adapterRegistry;
    private readonly IDawAdapter _mockAdapter;

    public DoctorUseCaseTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "dawvc_doctor_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);

        _context = Substitute.For<IRepositoryContext>();
        _context.RootPath.Returns(_tempDir);

        _mockAdapter = Substitute.For<IDawAdapter>();
        _mockAdapter.DawName.Returns("FL Studio");
        _mockAdapter.SupportedExtensions.Returns([".flp"]);

        _adapterRegistry = Substitute.For<IDawAdapterRegistry>();
        _adapterRegistry.FindAdapterForExtension(".flp").Returns(_mockAdapter);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, recursive: true);
            }
        }
        catch
        {
            // Best effort
        }

        GC.SuppressFinalize(this);
    }

    private async Task PrepareValidArtifactAsync()
    {
        _context.LoadConfig().Returns(new RepositoryConfig(
            RepositoryId.New(), "Test Project", new ArtifactPath("Project.flp"), BranchName.Main));
        await File.WriteAllBytesAsync(Path.Combine(_tempDir, "Project.flp"), [1, 2, 3]);
        _mockAdapter.DetectAsync(Arg.Any<Stream>(), Arg.Any<CancellationToken>())
            .Returns(ProjectDetectionResult.Valid("FL Studio", "2024.1", 1.0));
        _adapterRegistry.GetAllAdapters().Returns([_mockAdapter]);
    }

    [Fact]
    public async Task Progress_ReportsKnownCountsAndUnknownDiscovery_AndPreservesLegacyResults()
    {
        await PrepareValidArtifactAsync();
        var useCase = new DoctorUseCase(_ => _context, _adapterRegistry);
        var progress = new DiagnosticProgress();

        var report = await useCase.ExecuteAsync(new DoctorRequest(_tempDir), default, progress);
        var legacy = await useCase.ExecuteAsync(new DoctorRequest(_tempDir));

        report.Should().BeEquivalentTo(legacy);
        progress.Reports.Should().ContainSingle(p => p.Stage == OperationStage.Completed);
        progress.Reports.Last().Stage.Should().Be(OperationStage.Completed);
        progress.Reports.Where(p => p.Stage == OperationStage.InspectingArtifacts)
            .Select(p => (p.Completed, p.Total)).Should().Equal((0L, (long?)1), (1L, (long?)1));
        progress.Reports.Should().ContainSingle(p => p.Stage == OperationStage.DiscoveringDependencies && p.Total == null);
        progress.Reports.Should().ContainSingle(p => p.Stage == OperationStage.ResolvingDependencies && p.Completed == 0 && p.Total == 0);
        progress.Reports.Where(p => p.Stage == OperationStage.ProbingEnvironment)
            .Select(p => (p.Completed, p.Total)).Should().Equal((0L, (long?)1), (1L, (long?)1));
        progress.Reports.Should().OnlyContain(p => p.Unit == OperationProgressUnit.Items);
    }

    [Fact]
    public async Task Progress_FindingsAndEmptyEnvironment_StillComplete()
    {
        _context.LoadConfig().Returns(new RepositoryConfig(
            RepositoryId.New(), "Test", new ArtifactPath("Missing.flp"), BranchName.Main));
        var progress = new DiagnosticProgress();

        var report = await new DoctorUseCase(_ => _context)
            .ExecuteAsync(new DoctorRequest(_tempDir), default, progress);

        report.IsHealthy.Should().BeFalse();
        progress.Reports.Should().ContainSingle(p => p.Stage == OperationStage.Completed);
        progress.Reports.Should().ContainSingle(p => p.Stage == OperationStage.ProbingEnvironment && p.Completed == 0 && p.Total == 0);
    }

    [Theory]
    [InlineData(OperationStage.InspectingArtifacts, 1)]
    [InlineData(OperationStage.DiscoveringDependencies, 0)]
    [InlineData(OperationStage.ResolvingDependencies, 0)]
    [InlineData(OperationStage.ProbingEnvironment, 1)]
    public async Task Progress_ObserverExceptionsEscapeWithoutCompletion(OperationStage stage, long completed)
    {
        await PrepareValidArtifactAsync();
        var failure = new InvalidOperationException("Observer failed");
        var progress = new DiagnosticProgress(p =>
        {
            if (p.Stage == stage && p.Completed == completed) throw failure;
        });
        var action = () => new DoctorUseCase(_ => _context, _adapterRegistry)
            .ExecuteAsync(new DoctorRequest(_tempDir), default, progress);

        (await action.Should().ThrowAsync<InvalidOperationException>()).Which.Should().BeSameAs(failure);
        progress.Reports.Should().NotContain(p => p.Stage == OperationStage.Completed);
    }

    [Fact]
    public async Task Progress_PreCanceledCaller_DoesNotOpenContextOrComplete()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var opened = false;
        var progress = new DiagnosticProgress();
        var action = () => new DoctorUseCase(_ => { opened = true; return _context; })
            .ExecuteAsync(new DoctorRequest(_tempDir), cancellation.Token, progress);

        await action.Should().ThrowAsync<OperationCanceledException>();
        opened.Should().BeFalse();
        progress.Reports.Should().BeEmpty();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Progress_CancellationInDiagnosticCatches_IsNotConvertedToFindings(bool duringHeadLoad)
    {
        await PrepareValidArtifactAsync();
        if (duringHeadLoad)
        {
            var commit = new CommitId(Blake3ContentHasher.Hash([42]));
            _context.GetCurrentBranch().Returns(BranchName.Main);
            _context.GetBranchCommit(BranchName.Main).Returns(commit);
            _context.LoadCommitAsync(commit, Arg.Any<CancellationToken>()).ThrowsAsync(new OperationCanceledException());
        }
        else
        {
            _mockAdapter.DetectAsync(Arg.Any<Stream>(), Arg.Any<CancellationToken>())
                .ThrowsAsync(new OperationCanceledException());
        }
        var progress = new DiagnosticProgress();
        var action = () => new DoctorUseCase(_ => _context, _adapterRegistry)
            .ExecuteAsync(new DoctorRequest(_tempDir), default, progress);

        await action.Should().ThrowAsync<OperationCanceledException>();
        progress.Reports.Should().NotContain(p => p.Stage == OperationStage.Completed);
    }

    [Fact]
    public async Task Progress_CallerCanceledDuringOperation_DoesNotComplete()
    {
        await PrepareValidArtifactAsync();
        using var cancellation = new CancellationTokenSource();
        var progress = new DiagnosticProgress(p =>
        {
            if (p.Stage == OperationStage.ProbingEnvironment && p.Completed == 1) cancellation.Cancel();
        });
        var action = () => new DoctorUseCase(_ => _context, _adapterRegistry)
            .ExecuteAsync(new DoctorRequest(_tempDir), cancellation.Token, progress);

        await action.Should().ThrowAsync<OperationCanceledException>();
        progress.Reports.Should().NotContain(p => p.Stage == OperationStage.Completed);
    }

    [Fact]
    public async Task Progress_AdapterFailureBecomesFinding_AndCompletes()
    {
        await PrepareValidArtifactAsync();
        _mockAdapter.DetectAsync(Arg.Any<Stream>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new IOException("Inspection failed"));
        var progress = new DiagnosticProgress();

        var report = await new DoctorUseCase(_ => _context, _adapterRegistry)
            .ExecuteAsync(new DoctorRequest(_tempDir), default, progress);

        report.ArtifactHealth.Should().ContainSingle(a => a.IsBlocking);
        progress.Reports.Should().ContainSingle(p => p.Stage == OperationStage.Completed);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Progress_TaskFailure_DoesNotComplete(bool duringDisposal)
    {
        await PrepareValidArtifactAsync();
        if (duringDisposal) _context.When(c => c.Dispose()).Do(_ => throw new IOException("Dispose failed"));
        else _context.LoadConfig().Returns(_ => throw new IOException("Config failed"));
        var progress = new DiagnosticProgress();
        var action = () => new DoctorUseCase(_ => _context, _adapterRegistry)
            .ExecuteAsync(new DoctorRequest(_tempDir), default, progress);

        await action.Should().ThrowAsync<IOException>();
        progress.Reports.Should().NotContain(p => p.Stage == OperationStage.Completed);
    }

    [Fact]
    public async Task Doctor_WhenPrimaryArtifactMissing_ReportsBlockingError()
    {
        var config = new RepositoryConfig(
            RepositoryId.New(),
            "Test Project",
            new ArtifactPath("Missing.flp"),
            new BranchName("main"));

        _context.LoadConfig().Returns(config);

        var useCase = new DoctorUseCase(_ => _context, _adapterRegistry);
        var report = await useCase.ExecuteAsync(new DoctorRequest(_tempDir));

        report.SchemaVersion.Should().Be(1);
        report.IsHealthy.Should().BeFalse();
        report.BlockingIssuesCount.Should().BeGreaterThan(0);
        report.ArtifactHealth.Should().ContainSingle(a => a.IsBlocking && a.Status == HealthStatus.Error);
    }

    [Fact]
    public async Task Doctor_WhenArtifactValidAndNoDependencies_Reports100PercentScoreAndHealthy()
    {
        var flpPath = Path.Combine(_tempDir, "Project.flp");
        await File.WriteAllBytesAsync(flpPath, [0x01, 0x02, 0x03]);

        var config = new RepositoryConfig(
            RepositoryId.New(),
            "Test Project",
            new ArtifactPath("Project.flp"),
            new BranchName("main"));

        _context.LoadConfig().Returns(config);

        var detectResult = ProjectDetectionResult.Valid("FL Studio", "2024.1", 1.0);
        _mockAdapter.DetectAsync(Arg.Any<Stream>(), Arg.Any<CancellationToken>())
            .Returns(detectResult);

        var useCase = new DoctorUseCase(_ => _context, _adapterRegistry);
        var report = await useCase.ExecuteAsync(new DoctorRequest(_tempDir));

        report.SchemaVersion.Should().Be(1);
        report.IsHealthy.Should().BeTrue();
        report.ReproducibilityScore.Should().Be(100.0);
        report.ArtifactHealth.Should().ContainSingle(a => a.Status == HealthStatus.Healthy && !a.IsBlocking);
        report.EnvironmentHealth.OperatingSystem.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Doctor_WhenPluginVersionIsOlderThanRequirement_ReportsMismatch()
    {
        var flpPath = Path.Combine(_tempDir, "Project.flp");
        await File.WriteAllBytesAsync(flpPath, [0x01, 0x02, 0x03]);

        var config = new RepositoryConfig(
            RepositoryId.New(),
            "Test Project",
            new ArtifactPath("Project.flp"),
            new BranchName("main"));

        _context.LoadConfig().Returns(config);

        var pluginMeta = new Dictionary<string, string>
        {
            ["PluginOccurrences"] = "[{\"Name\":\"Kickstart 2\",\"Vendor\":\"Cableguys\",\"Role\":2,\"Format\":1,\"Location\":\"Mixer: Insert 1\"}]"
        };

        var detectResult = ProjectDetectionResult.Valid("FL Studio", "2024.1", 1.0, metadata: pluginMeta);
        _mockAdapter.DetectAsync(Arg.Any<Stream>(), Arg.Any<CancellationToken>())
            .Returns(detectResult);
        _mockAdapter.NormalizePlugin("Kickstart 2")
            .Returns(new PluginIdentity("Cableguys", "Kickstart 2", PluginFormat.VST3));
        _mockAdapter.DeterminePluginRole("Kickstart 2")
            .Returns(PluginRole.Effect);

        // Simulate probe finding installed version 1.0.0
        _mockAdapter.ProbePluginInstallation(Arg.Any<PluginDependency>())
            .Returns((true, "C:\\Program Files\\Common Files\\VST3\\Kickstart 2.vst3", "1.0.0"));

        // Simulate committed snapshot requiring version 2.0.6
        var branch = new BranchName("main");
        var commitId = new CommitId(Blake3ContentHasher.Hash([1]));
        _context.GetCurrentBranch().Returns(branch);
        _context.GetBranchCommit(branch).Returns(commitId);

        var snapshotPlugin = new PluginDependency(
            DependencyId.ForPlugin("Cableguys", "Kickstart 2", "VST3"),
            new PluginIdentity("Cableguys", "Kickstart 2", PluginFormat.VST3),
            PluginRole.Effect,
            versionRequirement: "2.0.6",
            location: "Mixer: Insert 1");

        var headEntry = ArtifactEntry.Create(new ArtifactPath("Project.flp"), Blake3ContentHasher.Hash([1, 2, 3]), 3, ArtifactRole.PrimaryProjectFile);
        var snapshot = new ProjectSnapshot(
            new ProjectArtifact("FL Studio", new SingleFileArtifact(headEntry)),
            DateTimeOffset.UtcNow,
            dependencies: new DependencyGraph([snapshotPlugin]));

        var commit = new Commit(Array.Empty<CommitId>(), snapshot.Id, "Author", DateTimeOffset.UtcNow, "Initial commit", commitId);
        _context.LoadCommitAsync(commitId, Arg.Any<CancellationToken>()).Returns(commit);
        _context.LoadSnapshotAsync(snapshot.Id, Arg.Any<CancellationToken>()).Returns(snapshot);

        var useCase = new DoctorUseCase(_ => _context, _adapterRegistry);
        var progress = new DiagnosticProgress();
        var report = await useCase.ExecuteAsync(new DoctorRequest(_tempDir), default, progress);

        progress.Reports.Where(p => p.Stage == OperationStage.ResolvingDependencies)
            .Select(p => (p.Completed, p.Total)).Should().Equal((0L, (long?)1), (1L, (long?)1));
        progress.Reports.Should().ContainSingle(p => p.Stage == OperationStage.Completed);
        report.DependencyHealth.Should().ContainSingle();
        var dep = report.DependencyHealth[0];
        dep.Name.Should().Be("Kickstart 2");
        dep.ExpectedVersion.Should().Be("2.0.6");
        dep.DetectedVersion.Should().Be("1.0.0");
        dep.Status.Should().Be(BindingStatus.Mismatch);
        dep.Details.Should().Contain("older than project version 2.0.6");
    }
}
