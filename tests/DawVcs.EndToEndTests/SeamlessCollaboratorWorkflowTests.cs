using System.Buffers.Binary;
using System.Text;

using DawVcs.Adapters.Abstractions;
using DawVcs.Adapters.FLStudio;
using DawVcs.Application.Adapters;
using DawVcs.Application.Branches;
using DawVcs.Application.Checkouts;
using DawVcs.Application.Commits;
using DawVcs.Application.Dependencies;
using DawVcs.Application.Diagnostics;
using DawVcs.Application.Repositories;
using DawVcs.Application.Staging;
using DawVcs.Domain.Artifacts;
using DawVcs.Domain.Common;
using DawVcs.Domain.Dependencies;
using DawVcs.Domain.Repositories;
using DawVcs.Infrastructure.Repositories;

using FluentAssertions;

using Xunit;

namespace DawVcs.EndToEndTests;

public sealed class SeamlessCollaboratorWorkflowTests
{
    private static Func<string, IRepositoryContext> ContextFactory => dir => new FileSystemRepositoryContext(dir);
    private static IDawAdapterRegistry Registry => new DawAdapterRegistry([new FLStudioAdapter()]);

    [Fact]
    [Trait("Category", "EndToEnd")]
    public async Task CollaboratorWorkflow_ExternalSampleAutoBundlesAndChecksOutWith100PercentDoctorScore()
    {
        // -------------------------------------------------------------
        // SCENARIO: Multi-Producer Collaboration Workflow (Fase 1..3)
        // Producer A has samples in an external soundpack drive/folder.
        // Producer A commits without manually moving files.
        // Producer B receives the repository (clone/checkout).
        // Producer B has 100% working project in ./Samples/ with 100% doctor score.
        // -------------------------------------------------------------

        using var producerDir = new TempDirectory("producer_workspace_");
        using var externalSoundsDir = new TempDirectory("external_samplepack_");
        using var collaboratorDir = new TempDirectory("collaborator_workspace_");

        // 1. Maker A: Externe sample op een centrale drive/map (D:\Sounds\...)
        var externalKickPath = Path.Combine(externalSoundsDir.Path, "Cymatics_Kick_01.wav");
        var kickSampleBytes = new byte[] { 0x52, 0x49, 0x46, 0x46, 0x24, 0x00, 0x00, 0x00, 0x57, 0x41, 0x56, 0x45 };
        await File.WriteAllBytesAsync(externalKickPath, kickSampleBytes);

        // 2. Maker A: Maakt FL Studio project dat verwijst naar externalKickPath
        var flpBytes = CreateFlp(samplePaths: [externalKickPath]);
        var flpPath = Path.Combine(producerDir.Path, "Song.flp");
        await File.WriteAllBytesAsync(flpPath, flpBytes);

        // 3. Maker A: Init en Commit
        var initUseCase = new InitRepositoryUseCase(ContextFactory);
        await initUseCase.ExecuteAsync(new InitRequest(producerDir.Path, "CollaborationTest", "Song.flp"));

        var commitUseCase = new CommitUseCase(ContextFactory, Registry);
        var commitResult = await commitUseCase.ExecuteAsync(new CommitRequest(producerDir.Path, "Initial commit by Producer A"));

        commitResult.Should().NotBeNull();
        commitResult.IsComplete.Should().BeTrue();

        // Verifieer dat Producer A's originele bestand op de externe schijf onaangeraakt blijft
        File.Exists(externalKickPath).Should().BeTrue();

        // 4. Simulatie: Collaborator B clone-t / pull-t de repository (deelt dawvc.yaml, .dawvc metadata en object store)
        File.Copy(Path.Combine(producerDir.Path, "dawvc.yaml"), Path.Combine(collaboratorDir.Path, "dawvc.yaml"));
        CopyDirectory(Path.Combine(producerDir.Path, ".dawvc"), Path.Combine(collaboratorDir.Path, ".dawvc"));

        // 5. Collaborator B voert initial checkout / restore uit naar zijn schone workspace (Force: true voor initiële workspace populatie)
        var checkoutUseCase = new CheckoutUseCase(ContextFactory, Registry);
        var checkoutResult = await checkoutUseCase.ExecuteAsync(new CheckoutRequest(
            collaboratorDir.Path,
            "main",
            Force: true));

        checkoutResult.Should().NotBeNull();
        checkoutResult.TargetDirectory.Should().Be(Path.GetFullPath(collaboratorDir.Path));

        // Verifieer fysieke materialisatie van Song.flp en ./Samples/Cymatics_Kick_01.wav
        var restoredFlp = Path.Combine(collaboratorDir.Path, "Song.flp");
        var restoredKick = Path.Combine(collaboratorDir.Path, "Samples", "Cymatics_Kick_01.wav");

        File.Exists(restoredFlp).Should().BeTrue();
        (await File.ReadAllBytesAsync(restoredFlp)).Should().Equal(flpBytes, "FL Studio binary byte-integrity must be 100% preserved (INV-001)");

        File.Exists(restoredKick).Should().BeTrue();
        (await File.ReadAllBytesAsync(restoredKick)).Should().Equal(kickSampleBytes, "Restored audio sample must match source BLAKE3 bytes exactly");

        // Verifieer intelligente checkout guidance
        checkoutResult.ManualInstructions.Should().NotBeNull();
        checkoutResult.ManualInstructions.Should().Contain(i => i.Contains("materialized in './Samples/'"));

        // 6. Collaborator B draait 'dawvc doctor' in zijn nieuwe checkout
        var doctorUseCase = new DoctorUseCase(ContextFactory, Registry);
        var doctorReport = await doctorUseCase.ExecuteAsync(new DoctorRequest(collaboratorDir.Path));

        doctorReport.Should().NotBeNull();
        doctorReport.IsHealthy.Should().BeTrue("Collaborator workspace must be healthy immediately after checkout");
        doctorReport.ReproducibilityScore.Should().Be(100.0, "Reproducibility score must be 100% because all samples are materialized");
        doctorReport.BlockingIssuesCount.Should().Be(0);

        doctorReport.DependencyHealth.Should().Contain(d =>
            d.Name == "Cymatics_Kick_01.wav" &&
            d.Status == BindingStatus.Verified &&
            !d.IsBlocking);

        // 7. Collaborator B vertakt naar een feature branch, voegt een tweede sample toe, en switcht heen en weer
        var switchUseCase = new SwitchUseCase(ContextFactory, Registry);
        var switchResult = await switchUseCase.ExecuteAsync(new SwitchRequest(
            collaboratorDir.Path,
            "feature-drums",
            CreateBranch: true));

        switchResult.CreatedNewBranch.Should().BeTrue();

        var snareBytes = new byte[] { 0x57, 0x41, 0x56, 0x45, 10, 20, 30 };
        var snarePath = Path.Combine(collaboratorDir.Path, "Samples", "Snare_01.wav");
        await File.WriteAllBytesAsync(snarePath, snareBytes);

        // Commit op feature branch
        var addUseCase = new AddUseCase(ContextFactory);
        await addUseCase.ExecuteAsync(new AddRequest(collaboratorDir.Path, ["Samples/Snare_01.wav"]));
        await commitUseCase.ExecuteAsync(new CommitRequest(collaboratorDir.Path, "Added snare on feature-drums"));

        // Switch terug naar main
        await switchUseCase.ExecuteAsync(new SwitchRequest(collaboratorDir.Path, "main"));
        File.Exists(snarePath).Should().BeFalse("Snare should be removed when switching back to main branch");
        File.Exists(restoredKick).Should().BeTrue("Kick must remain present on main branch");

        // Switch weer naar feature-drums
        await switchUseCase.ExecuteAsync(new SwitchRequest(collaboratorDir.Path, "feature-drums"));
        File.Exists(snarePath).Should().BeTrue("Snare must be restored when switching back to feature branch");
        File.Exists(restoredKick).Should().BeTrue();
    }

    [Fact]
    [Trait("Category", "EndToEnd")]
    public async Task CollaboratorWorkflow_RestoreToExternalDirectory_SucceedsWithoutForce()
    {
        using var producerDir = new TempDirectory("producer_workspace_");
        using var externalSoundsDir = new TempDirectory("external_samplepack_");
        using var exportDir = new TempDirectory("exported_workspace_");

        var externalKickPath = Path.Combine(externalSoundsDir.Path, "Kick.wav");
        var kickSampleBytes = new byte[] { 0x52, 0x49, 0x46, 0x46, 1, 2, 3, 4 };
        await File.WriteAllBytesAsync(externalKickPath, kickSampleBytes);

        var flpBytes = CreateFlp(samplePaths: [externalKickPath]);
        var flpPath = Path.Combine(producerDir.Path, "Song.flp");
        await File.WriteAllBytesAsync(flpPath, flpBytes);

        var initUseCase = new InitRepositoryUseCase(ContextFactory);
        await initUseCase.ExecuteAsync(new InitRequest(producerDir.Path, "ExportTest", "Song.flp"));

        var commitUseCase = new CommitUseCase(ContextFactory, Registry);
        var commitResult = await commitUseCase.ExecuteAsync(new CommitRequest(producerDir.Path, "Initial commit"));

        var checkoutUseCase = new CheckoutUseCase(ContextFactory, Registry);
        var restoreResult = await checkoutUseCase.ExecuteAsync(new CheckoutRequest(
            producerDir.Path,
            commitResult.CommitId.ToString(),
            RestoreToDirectory: exportDir.Path));

        restoreResult.Should().NotBeNull();
        File.Exists(Path.Combine(exportDir.Path, "Song.flp")).Should().BeTrue();
        File.Exists(Path.Combine(exportDir.Path, "Samples", "Kick.wav")).Should().BeTrue();
        (await File.ReadAllBytesAsync(Path.Combine(exportDir.Path, "Samples", "Kick.wav"))).Should().Equal(kickSampleBytes);
    }

    private static void CopyDirectory(string sourceDir, string destinationDir)
    {
        Directory.CreateDirectory(destinationDir);
        foreach (var file in Directory.GetFiles(sourceDir))
        {
            File.Copy(file, Path.Combine(destinationDir, Path.GetFileName(file)), overwrite: true);
        }

        foreach (var subDir in Directory.GetDirectories(sourceDir))
        {
            CopyDirectory(subDir, Path.Combine(destinationDir, Path.GetFileName(subDir)));
        }
    }

    private sealed class TempDirectory : IDisposable
    {
        public string Path { get; }

        public TempDirectory(string prefix = "dawvc-test-")
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), prefix + Guid.NewGuid().ToString("N"));
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
        dataStream.WriteByte(199);
        dataStream.WriteByte(11);
        dataStream.Write(Encoding.ASCII.GetBytes("25.2.5.5319"));

        if (samplePaths != null)
        {
            foreach (var s in samplePaths)
            {
                var bytes = Encoding.UTF8.GetBytes(s);
                dataStream.WriteByte(203);
                dataStream.WriteByte((byte)bytes.Length);
                dataStream.Write(bytes);
            }
        }

        if (pluginNames != null)
        {
            foreach (var p in pluginNames)
            {
                var bytes = Encoding.UTF8.GetBytes(p);
                dataStream.WriteByte(214);
                dataStream.WriteByte((byte)bytes.Length);
                dataStream.Write(bytes);
            }
        }

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
