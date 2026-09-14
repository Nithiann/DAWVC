using System.Buffers.Binary;
using System.Text;
using System.Text.Json;

using DawVcs.Adapters.FLStudio;
using DawVcs.Application.Adapters;
using DawVcs.Application.Commits;
using DawVcs.Application.Diagnostics;
using DawVcs.Application.Repositories;
using DawVcs.Domain.Repositories;
using DawVcs.Infrastructure.Repositories;

using FluentAssertions;

using Xunit;

namespace DawVcs.EndToEndTests;

public sealed class FsckAcTests
{
    private static Func<string, IRepositoryContext> ContextFactory => dir => new FileSystemRepositoryContext(dir);
    private static IDawAdapterRegistry Registry => new DawAdapterRegistry([new FLStudioAdapter()]);

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    [Fact]
    [Trait("Requirement", "FR-FSC-001..008")]
    public async Task Fsck_OnHealthyRepository_VerifiesObjectGraphAndZeroCorruption()
    {
        using var temp = new TempDirectory();
        var flpPath = Path.Combine(temp.Path, "Song.flp");
        await File.WriteAllBytesAsync(flpPath, CreateFlp());

        var initUseCase = new InitRepositoryUseCase(ContextFactory);
        await initUseCase.ExecuteAsync(new InitRequest(temp.Path, "FsckProject", "Song.flp"));

        var commitUseCase = new CommitUseCase(ContextFactory, Registry);
        await commitUseCase.ExecuteAsync(new CommitRequest(temp.Path, "Initial commit"));

        var fsckUseCase = new FsckUseCase(ContextFactory, Registry);
        var report = await fsckUseCase.ExecuteAsync(new FsckRequest(temp.Path, CheckArtifacts: false));

        report.SchemaVersion.Should().Be(1);
        report.IsHealthy.Should().BeTrue();
        report.RepositoryIntegrity.IsIntact.Should().BeTrue();
        report.RepositoryIntegrity.TotalObjectsScanned.Should().BeGreaterThan(0);
        report.RepositoryIntegrity.CorruptObjectsCount.Should().Be(0);
        report.RepositoryIntegrity.MissingObjectsCount.Should().Be(0);
        report.RepositoryIntegrity.OrphanObjectsCount.Should().Be(0);

        // JSON Serialization
        var json = JsonSerializer.Serialize(report, JsonOptions);
        json.Should().Contain("\"schemaVersion\":1");
        json.Should().Contain("\"isIntact\":true");
    }

    [Fact]
    [Trait("Requirement", "AC-012")]
    public async Task Ac012_CorruptArtifactContent_IsSeparatedFromRepositoryIntegrity()
    {
        // GIVEN een repository waarin een FLP bestand wordt gecommit dat ongeldige FLP data bevat
        using var temp = new TempDirectory();
        var flpPath = Path.Combine(temp.Path, "CorruptProject.flp");

        // We schrijven bytes die NIET een geldige FLP zijn (geen FLhd header)
        var invalidFlpBytes = new byte[] { 0x00, 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07 };
        await File.WriteAllBytesAsync(flpPath, invalidFlpBytes);

        var initUseCase = new InitRepositoryUseCase(ContextFactory);
        await initUseCase.ExecuteAsync(new InitRequest(temp.Path, "Ac012Project", "CorruptProject.flp"));

        var commitUseCase = new CommitUseCase(ContextFactory, Registry);
        await commitUseCase.ExecuteAsync(new CommitRequest(temp.Path, "Commit corrupt FLP"));

        // WHEN we fsck aanroepen met --artifacts
        var fsckUseCase = new FsckUseCase(ContextFactory, Registry);
        var report = await fsckUseCase.ExecuteAsync(new FsckRequest(temp.Path, CheckArtifacts: true));

        // THEN (AC-012):
        // 1. Repository integrity moet INTACT zijn: de envelope en BLAKE3 hash matchen de opgeslagen payload exact!
        report.RepositoryIntegrity.IsIntact.Should().BeTrue();
        report.RepositoryIntegrity.CorruptObjectsCount.Should().Be(0);
        report.RepositoryIntegrity.Errors.Should().BeEmpty();
        report.RepositoryIntegrity.Errors.Should().NotContain(e => e.Code == "ObjectHashMismatch");

        // 2. Artifact integrity meldt de parse/detectatiefout
        report.ArtifactIntegrity.Should().NotBeNull();
        report.ArtifactIntegrity!.IsIntact.Should().BeFalse();
        report.ArtifactIntegrity.FailedArtifactsCount.Should().BeGreaterThan(0);
        report.ArtifactIntegrity.Errors.Should().NotBeEmpty();

        // 3. Totaal rapport faalt gezondheid
        report.IsHealthy.Should().BeFalse();
    }

    private sealed class TempDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "dawvc-fsck-test-" + Guid.NewGuid().ToString("N"));

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
