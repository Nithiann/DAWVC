using System.Buffers.Binary;
using System.Text;
using System.Text.Json;

using DawVcs.Adapters.FLStudio;
using DawVcs.Application.Adapters;
using DawVcs.Application.Diagnostics;
using DawVcs.Application.Repositories;
using DawVcs.Domain.Repositories;
using DawVcs.Infrastructure.Repositories;

using FluentAssertions;

using Xunit;

namespace DawVcs.EndToEndTests;

public sealed class DoctorAcTests
{
    private static Func<string, IRepositoryContext> ContextFactory => dir => new FileSystemRepositoryContext(dir);
    private static IDawAdapterRegistry Registry => new DawAdapterRegistry([new FLStudioAdapter()]);

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    [Fact]
    [Trait("Requirement", "FR-DOC-001..010")]
    public async Task Doctor_OnHealthyRepository_EvaluatesAllThreeDomainsSuccessfully()
    {
        using var temp = new TempDirectory();
        var flpPath = Path.Combine(temp.Path, "Project.flp");
        await File.WriteAllBytesAsync(flpPath, CreateFlp());

        var initUseCase = new InitRepositoryUseCase(ContextFactory);
        await initUseCase.ExecuteAsync(new InitRequest(temp.Path, "DoctorProject", "Project.flp"));

        var doctorUseCase = new DoctorUseCase(ContextFactory, Registry);
        var report = await doctorUseCase.ExecuteAsync(new DoctorRequest(temp.Path));

        // Schema & Verdict
        report.SchemaVersion.Should().Be(1);
        report.IsHealthy.Should().BeTrue();
        report.BlockingIssuesCount.Should().Be(0);
        report.ReproducibilityScore.Should().Be(100.0);

        // Domain 1: Artifact
        report.ArtifactHealth.Should().ContainSingle();
        var art = report.ArtifactHealth[0];
        art.Path.Should().Be("Project.flp");
        art.Status.Should().Be(HealthStatus.Healthy);
        art.IsBlocking.Should().BeFalse();

        // Domain 3: Environment
        report.EnvironmentHealth.OperatingSystem.Should().NotBeNullOrWhiteSpace();
        report.EnvironmentHealth.DotNetVersion.Should().NotBeNullOrWhiteSpace();

        // JSON Serialization Check
        var json = JsonSerializer.Serialize(report, JsonOptions);
        json.Should().Contain("\"schemaVersion\":1");
        json.Should().Contain("\"isHealthy\":true");
        json.Should().Contain("\"reproducibilityScore\":100");
    }

    [Fact]
    [Trait("Requirement", "FR-DOC-005")]
    public async Task Doctor_WhenArtifactMissing_ReportsBlockingIssue()
    {
        using var temp = new TempDirectory();
        var flpPath = Path.Combine(temp.Path, "Project.flp");
        await File.WriteAllBytesAsync(flpPath, CreateFlp());

        var initUseCase = new InitRepositoryUseCase(ContextFactory);
        await initUseCase.ExecuteAsync(new InitRequest(temp.Path, "DoctorMissing", "Project.flp"));

        // Delete the file to simulate missing primary artifact
        File.Delete(flpPath);

        var doctorUseCase = new DoctorUseCase(ContextFactory, Registry);
        var report = await doctorUseCase.ExecuteAsync(new DoctorRequest(temp.Path));

        report.IsHealthy.Should().BeFalse();
        report.BlockingIssuesCount.Should().BeGreaterThan(0);
        report.ArtifactHealth.Should().ContainSingle(a => a.IsBlocking && a.Status == HealthStatus.Error);
    }

    private sealed class TempDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "dawvc-doctor-test-" + Guid.NewGuid().ToString("N"));

        public TempDirectory()
        {
            Directory.CreateDirectory(Path);
        }

        public void Dispose()
        {
            try { Directory.Delete(Path, recursive: true); } catch { }
            GC.SuppressFinalize(this);
        }
    }

    private static byte[] CreateFlp()
    {
        using var dataStream = new MemoryStream();
        dataStream.WriteByte(199);
        dataStream.WriteByte(11);
        dataStream.Write(Encoding.ASCII.GetBytes("25.2.5.5319"));

        var dataBytes = dataStream.ToArray();
        using var flp = new MemoryStream();
        flp.Write([0x46, 0x4C, 0x68, 0x64, 0x06, 0x00, 0x00, 0x00, 0x00, 0x00, 0x0A, 0x00, 0x60, 0x00]);
        flp.Write([0x46, 0x4C, 0x64, 0x74]);
        var lenBytes = new byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(lenBytes, (uint)dataBytes.Length);
        flp.Write(lenBytes);
        flp.Write(dataBytes);

        return flp.ToArray();
    }
}
