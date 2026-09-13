using DawVcs.Adapters.FLStudio;
using DawVcs.Application.Adapters;
using DawVcs.Application.Commits;
using DawVcs.Application.Exceptions;
using DawVcs.Application.Repositories;
using DawVcs.Application.Staging;
using DawVcs.Domain.Repositories;
using DawVcs.Infrastructure.Repositories;

using FluentAssertions;

using Xunit;

namespace DawVcs.EndToEndTests;

public sealed class PortabilityAcTests
{
    private static Func<string, IRepositoryContext> ContextFactory => dir => new FileSystemRepositoryContext(dir);
    private static IDawAdapterRegistry Registry => new DawAdapterRegistry([new FLStudioAdapter()]);

    [Fact]
    [Trait("Requirement", "AC-002")]
    public async Task Ac002_Deduplication_TwoPathsWithIdenticalBytesShareSingleBlob()
    {
        // Given twee lokale paden met exact dezelfde assetbytes
        using var temp = new TempDirectory();
        var flpPath = Path.Combine(temp.Path, "Track.flp");
        await File.WriteAllBytesAsync(flpPath, CreateFlp());

        var initUseCase = new InitRepositoryUseCase(ContextFactory);
        await initUseCase.ExecuteAsync(new InitRequest(temp.Path, "DeduplicationTest", "Track.flp"));

        // Twee verschillende bestanden met identieke bytes
        byte[] identicalWavBytes = [0x52, 0x49, 0x46, 0x46, 0x24, 0x00, 0x00, 0x00, 0x57, 0x41, 0x56, 0x45];
        var kick1Path = Path.Combine(temp.Path, "Kick_Main.wav");
        var kick2Path = Path.Combine(temp.Path, "Kick_Layer.wav");
        await File.WriteAllBytesAsync(kick1Path, identicalWavBytes);
        await File.WriteAllBytesAsync(kick2Path, identicalWavBytes);

        // When beide als dependency worden geregistreerd (gestaged)
        var addUseCase = new AddUseCase(ContextFactory);
        await addUseCase.ExecuteAsync(new AddRequest(temp.Path, ["Kick_Main.wav", "Kick_Layer.wav"], All: false));

        var commitUseCase = new CommitUseCase(ContextFactory, Registry);
        var commitResult = await commitUseCase.ExecuteAsync(new CommitRequest(temp.Path, "Commit with duplicate samples"));

        // Then verwijzen zij naar dezelfde blobidentity
        using var repo = ContextFactory(temp.Path);
        var snapshot = await repo.LoadSnapshotAsync(commitResult.SnapshotId);
        snapshot.Should().NotBeNull();

        var entries = snapshot!.Project.Root.GetEntries().ToList();
        var kick1Entry = entries.First(e => e.Path.Value == "Kick_Main.wav");
        var kick2Entry = entries.First(e => e.Path.Value == "Kick_Layer.wav");

        kick1Entry.Blob.Should().Be(kick2Entry.Blob, "Both files have identical content and must reference the same BlobId");
        kick1Entry.Hash.Should().Be(kick2Entry.Hash);

        // And worden de bytes slechts eenmaal in de object store opgeslagen
        var exists = repo.ObjectStore.Exists(kick1Entry.Blob);
        exists.Should().BeTrue();
    }

    [Fact]
    [Trait("Requirement", "AC-003")]
    public async Task Ac003_ChangedContentWithSameName_CreatesNewContentIdentity()
    {
        // Given een gevolgde asset kick.wav
        using var temp = new TempDirectory();
        var flpPath = Path.Combine(temp.Path, "Track.flp");
        await File.WriteAllBytesAsync(flpPath, CreateFlp());

        var initUseCase = new InitRepositoryUseCase(ContextFactory);
        await initUseCase.ExecuteAsync(new InitRequest(temp.Path, "ContentChangeTest", "Track.flp"));

        var kickPath = Path.Combine(temp.Path, "kick.wav");
        await File.WriteAllBytesAsync(kickPath, [1, 2, 3, 4, 5]);

        var addUseCase = new AddUseCase(ContextFactory);
        await addUseCase.ExecuteAsync(new AddRequest(temp.Path, ["kick.wav"], All: false));

        var commitUseCase = new CommitUseCase(ContextFactory, Registry);
        var commit1 = await commitUseCase.ExecuteAsync(new CommitRequest(temp.Path, "Initial kick"));

        using var repo = ContextFactory(temp.Path);
        var snapshot1 = await repo.LoadSnapshotAsync(commit1.SnapshotId);
        var oldKickEntry = snapshot1!.Project.Root.GetEntries().First(e => e.Path.Value == "kick.wav");

        // When de bytes veranderen maar de filename gelijk blijft
        await File.WriteAllBytesAsync(kickPath, [9, 9, 9, 9, 9, 9, 9]);

        var commit2 = await commitUseCase.ExecuteAsync(new CommitRequest(temp.Path, "Updated kick bytes"));

        // Then wordt een nieuwe contentidentity gemaakt
        var snapshot2 = await repo.LoadSnapshotAsync(commit2.SnapshotId);
        var newKickEntry = snapshot2!.Project.Root.GetEntries().First(e => e.Path.Value == "kick.wav");

        newKickEntry.Blob.Should().NotBe(oldKickEntry.Blob);
        newKickEntry.Hash.Should().NotBe(oldKickEntry.Hash);
    }

    [Fact]
    [Trait("Requirement", "AC-005")]
    public async Task Ac005_MissingRequiredBundleDependency_BlocksCommitUnlessAllowIncomplete()
    {
        // Given een ontbrekende verplichte Bundle dependency
        using var temp = new TempDirectory();
        var flpPath = Path.Combine(temp.Path, "Track.flp");
        await File.WriteAllBytesAsync(flpPath, CreateFlp());

        var initUseCase = new InitRepositoryUseCase(ContextFactory);
        await initUseCase.ExecuteAsync(new InitRequest(temp.Path, "IncompleteTest", "Track.flp"));

        // Stage een sample MissingKick.wav die vervolgens verwijderd wordt van disk
        var kickPath = Path.Combine(temp.Path, "MissingKick.wav");
        await File.WriteAllBytesAsync(kickPath, [1, 2, 3, 4, 5]);

        var addUseCase = new AddUseCase(ContextFactory);
        await addUseCase.ExecuteAsync(new AddRequest(temp.Path, ["MissingKick.wav"], All: false));

        // Verwijder het bestand van disk zodat de verplichte gebundelde dependency ontbreekt
        File.Delete(kickPath);

        var commitUseCase = new CommitUseCase(ContextFactory, Registry);

        // When de gebruiker een normale commit uitvoert (zonder --allow-incomplete)
        // Then stopt het commando (gooit IncompleteDependencyException)
        var act = async () => await commitUseCase.ExecuteAsync(new CommitRequest(temp.Path, "Commit with missing sample", AllowIncomplete: false));
        await act.Should().ThrowAsync<IncompleteDependencyException>()
            .WithMessage("*MissingKick.wav*");

        // When de gebruiker commit --allow-incomplete uitvoert
        var result = await commitUseCase.ExecuteAsync(new CommitRequest(temp.Path, "Commit allowing incomplete", AllowIncomplete: true));

        // Then wordt een incomplete snapshot gemaakt
        result.IsComplete.Should().BeFalse();
        result.IncompleteReason.Should().Contain("MissingKick.wav");

        using var repo = ContextFactory(temp.Path);
        var snapshot = await repo.LoadSnapshotAsync(result.SnapshotId);
        snapshot.Should().NotBeNull();
        snapshot!.IsComplete.Should().BeFalse();
        snapshot.IncompleteReason.Should().Contain("MissingKick.wav");
    }

    [Fact]
    [Trait("Requirement", "AC-006")]
    public async Task Ac006_ReferenceOnlyPluginMissing_DoesNotBundleBinaryAndCommitSucceeds()
    {
        // Given een project dat een niet-geïnstalleerde commerciële plugin vereist (Serum)
        using var temp = new TempDirectory();
        var flpBytes = CreateFlp(pluginNames: ["Serum_x64.vst3"]);
        var flpPath = Path.Combine(temp.Path, "Track.flp");
        await File.WriteAllBytesAsync(flpPath, flpBytes);

        var initUseCase = new InitRepositoryUseCase(ContextFactory);
        await initUseCase.ExecuteAsync(new InitRequest(temp.Path, "PluginTest", "Track.flp"));

        // When de gebruiker commit uitvoert
        var commitUseCase = new CommitUseCase(ContextFactory, Registry);
        var result = await commitUseCase.ExecuteAsync(new CommitRequest(temp.Path, "Commit with Serum plugin"));

        // Then slaagt de commit en is het snapshot compleet
        result.CommitId.Should().NotBe(default);
        result.IsComplete.Should().BeTrue();

        using var repo = ContextFactory(temp.Path);
        var snapshot = await repo.LoadSnapshotAsync(result.SnapshotId);
        snapshot.Should().NotBeNull();

        // And wordt de pluginbinary niet gebundeld
        var entries = snapshot!.Project.Root.GetEntries().ToList();
        entries.Should().NotContain(e => e.Path.Value.Contains("Serum"));
        entries.Should().ContainSingle(e => e.Path.Value == "Track.flp");

        // Maar is wel geregistreerd als ReferenceOnly dependency
        var pluginDep = snapshot.Dependencies.GetReferenceOnlyDependencies().Should().ContainSingle().Which;
        pluginDep.Name.Should().Be("Serum");
    }

    private sealed class TempDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "dawvc-portability-test-" + Guid.NewGuid().ToString("N"));

        public TempDirectory()
        {
            Directory.CreateDirectory(Path);
        }

        public void Dispose()
        {
            try { Directory.Delete(Path, recursive: true); } catch { }
        }
    }

    private static byte[] CreateFlp(string[]? samplePaths = null, string[]? pluginNames = null)
    {
        using var dataStream = new MemoryStream();
        // Version event 199
        dataStream.WriteByte(199);
        dataStream.WriteByte(11);
        dataStream.Write(System.Text.Encoding.ASCII.GetBytes("25.2.5.5319"));

        if (samplePaths != null)
        {
            foreach (var s in samplePaths)
            {
                var bytes = System.Text.Encoding.UTF8.GetBytes(s);
                dataStream.WriteByte(203);
                dataStream.WriteByte((byte)bytes.Length);
                dataStream.Write(bytes);
            }
        }

        if (pluginNames != null)
        {
            foreach (var p in pluginNames)
            {
                var bytes = System.Text.Encoding.UTF8.GetBytes(p);
                dataStream.WriteByte(214);
                dataStream.WriteByte((byte)bytes.Length);
                dataStream.Write(bytes);
            }
        }

        var dataBytes = dataStream.ToArray();
        using var flp = new MemoryStream();
        // FLhd
        flp.Write([0x46, 0x4C, 0x68, 0x64, 0x06, 0x00, 0x00, 0x00, 0x00, 0x00, 0x0A, 0x00, 0x60, 0x00]);
        // FLdt
        flp.Write([0x46, 0x4C, 0x64, 0x74]);
        var lenBytes = new byte[4];
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(lenBytes, (uint)dataBytes.Length);
        flp.Write(lenBytes);
        flp.Write(dataBytes);

        return flp.ToArray();
    }
}
