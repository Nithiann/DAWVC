using DawVcs.Adapters.Abstractions;
using DawVcs.Application.Adapters;
using DawVcs.Application.Diagnostics;
using DawVcs.Domain.Artifacts;
using DawVcs.Domain.Common;
using DawVcs.Domain.Configuration;
using DawVcs.Domain.Dependencies;
using DawVcs.Domain.Hashing;
using DawVcs.Domain.Repositories;

using FluentAssertions;

using NSubstitute;

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
}
