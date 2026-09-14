using DawVcs.Adapters.Abstractions;
using DawVcs.Application.Adapters;
using DawVcs.Application.Scanning;
using DawVcs.Domain.Artifacts;
using DawVcs.Domain.Common;
using DawVcs.Domain.Configuration;
using DawVcs.Domain.Repositories;

using FluentAssertions;

using NSubstitute;

using Xunit;

namespace DawVcs.Application.Tests.Scanning;

public sealed class ScanUseCaseTests
{
    private static readonly string[] FlpExtensions = [".flp"];

    private readonly IRepositoryContext _repo = Substitute.For<IRepositoryContext>();
    private readonly IDawAdapter _adapter = Substitute.For<IDawAdapter>();
    private readonly DawAdapterRegistry _registry = new();

    public ScanUseCaseTests()
    {
        _adapter.DawName.Returns("Mock DAW");
        _adapter.SupportedExtensions.Returns(FlpExtensions);
        _registry.Register(_adapter);
    }

    [Fact]
    public async Task ExecuteAsync_ValidProject_ReturnsValidResult()
    {
        // Arrange
        using var temp = new TempDirectory();
        var flpPath = Path.Combine(temp.Path, "Project.flp");
        await File.WriteAllTextAsync(flpPath, "dummy content");

        var config = new RepositoryConfig(
            RepositoryId.New(),
            "TestProject",
            new ArtifactPath("Project.flp"));

        _repo.RootPath.Returns(temp.Path);
        _repo.LoadConfig().Returns(config);

        _adapter.DetectAsync(Arg.Any<ArtifactReadContext>(), Arg.Any<CancellationToken>())
            .Returns(ProjectDetectionResult.Valid("Mock DAW", "25.0"));

        var useCase = new ScanUseCase(_ => _repo, _registry);

        // Act
        var result = await useCase.ExecuteAsync(new ScanRequest(temp.Path));

        // Assert
        result.Detection.Status.Should().Be(ProjectDetectionStatus.Valid);
        result.RequiresOpaqueFallback.Should().BeFalse();
        result.PrimaryDawName.Should().Be("Mock DAW");
    }

    [Fact]
    public async Task ExecuteAsync_UnsupportedVersion_RequiresOpaqueFallback()
    {
        // Arrange
        using var temp = new TempDirectory();
        var flpPath = Path.Combine(temp.Path, "Project.flp");
        await File.WriteAllTextAsync(flpPath, "dummy content");

        var config = new RepositoryConfig(
            RepositoryId.New(),
            "TestProject",
            new ArtifactPath("Project.flp"));

        _repo.RootPath.Returns(temp.Path);
        _repo.LoadConfig().Returns(config);

        _adapter.DetectAsync(Arg.Any<ArtifactReadContext>(), Arg.Any<CancellationToken>())
            .Returns(ProjectDetectionResult.Unsupported("Mock DAW", "99.0", "Newer version"));

        var useCase = new ScanUseCase(_ => _repo, _registry);

        // Act
        var result = await useCase.ExecuteAsync(new ScanRequest(temp.Path));

        // Assert
        result.Detection.Status.Should().Be(ProjectDetectionStatus.Unsupported);
        result.RequiresOpaqueFallback.Should().BeTrue();
    }

    [Fact]
    public async Task ExecuteAsync_AdapterTimeout_ReturnsInvalidResultWithoutHanging()
    {
        // Arrange
        using var temp = new TempDirectory();
        var flpPath = Path.Combine(temp.Path, "Project.flp");
        await File.WriteAllTextAsync(flpPath, "dummy content");

        var config = new RepositoryConfig(
            RepositoryId.New(),
            "TestProject",
            new ArtifactPath("Project.flp"));

        _repo.RootPath.Returns(temp.Path);
        _repo.LoadConfig().Returns(config);

        // Adapter that hangs until cancellation
        _adapter.DetectAsync(Arg.Any<ArtifactReadContext>(), Arg.Any<CancellationToken>())
            .Returns(async callInfo =>
            {
                var ct = callInfo.Arg<CancellationToken>();
                await Task.Delay(5000, ct);
                return ProjectDetectionResult.Valid("Mock DAW", "25.0");
            });

        var useCase = new ScanUseCase(_ => _repo, _registry);

        // Act: Execute with 50ms timeout
        var result = await useCase.ExecuteAsync(new ScanRequest(temp.Path, Timeout: TimeSpan.FromMilliseconds(50)));

        // Assert
        result.Detection.Status.Should().Be(ProjectDetectionStatus.Invalid);
        result.Detection.Findings.Should().Contain(f => f.Contains("time-out"));
    }

    [Fact]
    public async Task ExecuteAsync_UnknownExtension_ReturnsUnknownStatusWithOpaqueFallback()
    {
        // Arrange
        using var temp = new TempDirectory();
        var unknownPath = Path.Combine(temp.Path, "Project.xyz");
        await File.WriteAllTextAsync(unknownPath, "dummy content");

        var config = new RepositoryConfig(
            RepositoryId.New(),
            "TestProject",
            new ArtifactPath("Project.xyz"));

        _repo.RootPath.Returns(temp.Path);
        _repo.LoadConfig().Returns(config);

        var useCase = new ScanUseCase(_ => _repo, _registry);

        // Act
        var result = await useCase.ExecuteAsync(new ScanRequest(temp.Path));

        // Assert
        result.Detection.Status.Should().Be(ProjectDetectionStatus.Unknown);
        result.RequiresOpaqueFallback.Should().BeTrue();
    }

    private sealed class TempDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "dawvc-test-" + Guid.NewGuid().ToString("N"));

        public TempDirectory()
        {
            Directory.CreateDirectory(Path);
        }

        public void Dispose()
        {
            try { Directory.Delete(Path, recursive: true); } catch { }
        }
    }
}
