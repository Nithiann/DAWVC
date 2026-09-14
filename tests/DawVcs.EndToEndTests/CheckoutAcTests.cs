using System.Buffers.Binary;
using System.Text;

using DawVcs.Adapters.FLStudio;
using DawVcs.Application.Adapters;
using DawVcs.Application.Checkouts;
using DawVcs.Application.Commits;
using DawVcs.Application.Dependencies;
using DawVcs.Application.Exceptions;
using DawVcs.Application.Repositories;
using DawVcs.Application.Staging;
using DawVcs.Domain.Artifacts;
using DawVcs.Domain.Dependencies;
using DawVcs.Domain.Repositories;
using DawVcs.Infrastructure.Repositories;

using FluentAssertions;

using Xunit;

namespace DawVcs.EndToEndTests;

public sealed class CheckoutAcTests
{
    private static Func<string, IRepositoryContext> ContextFactory => dir => new FileSystemRepositoryContext(dir);
    private static IDawAdapterRegistry Registry => new DawAdapterRegistry([new FLStudioAdapter()]);

    [Fact]
    [Trait("Requirement", "AC-009")]
    public async Task Ac009_DirtyWorkspace_BlocksCheckoutWithExitCode7_AndRestoreToSucceeds()
    {
        // Given een workspace met niet-gecommitte wijzigingen
        using var temp = new TempDirectory();
        var flpPath = Path.Combine(temp.Path, "Track.flp");
        await File.WriteAllBytesAsync(flpPath, CreateFlp());

        var initUseCase = new InitRepositoryUseCase(ContextFactory);
        await initUseCase.ExecuteAsync(new InitRequest(temp.Path, "CheckoutTest", "Track.flp"));

        var commitUseCase = new CommitUseCase(ContextFactory, Registry);
        var commit1 = await commitUseCase.ExecuteAsync(new CommitRequest(temp.Path, "Initial commit"));

        // Maak een tweede commit
        var sampleBytes = new byte[] { 1, 2, 3, 4, 5 };
        var samplePath = Path.Combine(temp.Path, "Kick.wav");
        await File.WriteAllBytesAsync(samplePath, sampleBytes);

        var addUseCase = new AddUseCase(ContextFactory);
        await addUseCase.ExecuteAsync(new AddRequest(temp.Path, ["Kick.wav"]));

        var commit2 = await commitUseCase.ExecuteAsync(new CommitRequest(temp.Path, "Commit with kick"));

        // Maak de workspace dirty door een lokaal bestand aan te passen zonder commit
        var dirtyBytes = new byte[] { 0xDE, 0xAD, 0xBE, 0xEF };
        await File.WriteAllBytesAsync(flpPath, dirtyBytes);

        // When de gebruiker een normale checkout uitvoert (zonder --force)
        var checkoutUseCase = new CheckoutUseCase(ContextFactory);
        var act = () => checkoutUseCase.ExecuteAsync(new CheckoutRequest(temp.Path, commit1.CommitId.ToString(), Force: false));

        // Then stopt checkout met DirtyWorkspaceException (exitcode 7)
        var ex = await act.Should().ThrowAsync<DirtyWorkspaceException>();
        ex.Which.Status.HasWorkingTreeModifications.Should().BeTrue();

        // And blijven alle lokale bytes onaangeraakt
        var currentBytes = await File.ReadAllBytesAsync(flpPath);
        currentBytes.Should().Equal(dirtyBytes);

        // When de gebruiker --restore-to gebruikt
        using var restoreDir = new TempDirectory();
        var restoreResult = await checkoutUseCase.ExecuteAsync(new CheckoutRequest(
            temp.Path,
            commit1.CommitId.ToString(),
            RestoreToDirectory: restoreDir.Path));

        // Then wordt de snapshot in de gekozen aparte directory hersteld
        restoreResult.CommitId.Should().Be(commit1.CommitId);
        File.Exists(Path.Combine(restoreDir.Path, "Track.flp")).Should().BeTrue();
        (await File.ReadAllBytesAsync(flpPath)).Should().Equal(dirtyBytes); // Actieve workspace nog steeds onaangeroerd
    }

    [Fact]
    [Trait("Requirement", "AC-010")]
    public async Task Ac010_ForceCheckout_CreatesRecoveryCopyAndSucceeds()
    {
        // Given een dirty workspace
        using var temp = new TempDirectory();
        var flpPath = Path.Combine(temp.Path, "Track.flp");
        var initialFlpBytes = CreateFlp();
        await File.WriteAllBytesAsync(flpPath, initialFlpBytes);

        var initUseCase = new InitRepositoryUseCase(ContextFactory);
        await initUseCase.ExecuteAsync(new InitRequest(temp.Path, "ForceCheckoutTest", "Track.flp"));

        var commitUseCase = new CommitUseCase(ContextFactory, Registry);
        var commit1 = await commitUseCase.ExecuteAsync(new CommitRequest(temp.Path, "Commit 1"));

        // Commit 2 met extra sample
        var kickBytes = new byte[] { 10, 20, 30 };
        var kickPath = Path.Combine(temp.Path, "Kick.wav");
        await File.WriteAllBytesAsync(kickPath, kickBytes);

        var addUseCase = new AddUseCase(ContextFactory);
        await addUseCase.ExecuteAsync(new AddRequest(temp.Path, ["Kick.wav"]));
        var commit2 = await commitUseCase.ExecuteAsync(new CommitRequest(temp.Path, "Commit 2"));

        // Maak workspace dirty met ongecommitte wijzigingen
        var dirtyFlpBytes = new byte[] { 0xAA, 0xBB, 0xCC, 0xDD };
        await File.WriteAllBytesAsync(flpPath, dirtyFlpBytes);

        // When de gebruiker checkout --force uitvoert naar commit 1
        var checkoutUseCase = new CheckoutUseCase(ContextFactory);
        var result = await checkoutUseCase.ExecuteAsync(new CheckoutRequest(
            temp.Path,
            commit1.CommitId.ToString(),
            Force: true));

        // Then wordt eerst een volledige recovery copy gemaakt en gerapporteerd
        result.RecoveryDirectory.Should().NotBeNullOrWhiteSpace();
        Directory.Exists(result.RecoveryDirectory).Should().BeTrue();

        // En bevat de recovery copy het dirty bestand en een manifest
        File.Exists(Path.Combine(result.RecoveryDirectory!, "manifest.txt")).Should().BeTrue();
        var recoveredBytes = await File.ReadAllBytesAsync(Path.Combine(result.RecoveryDirectory!, "Track.flp"));
        recoveredBytes.Should().Equal(dirtyFlpBytes);

        // And wordt pas daarna de geverifieerde checkout gepubliceerd
        result.CommitId.Should().Be(commit1.CommitId);
        var publishedBytes = await File.ReadAllBytesAsync(flpPath);
        publishedBytes.Should().Equal(initialFlpBytes);
    }

    [Fact]
    [Trait("Requirement", "AC-011")]
    public async Task Ac011_CrashDuringStagedCheckout_LeavesWorkspaceUntouched()
    {
        // Given een bestaande geldige workspace
        using var temp = new TempDirectory();
        var flpPath = Path.Combine(temp.Path, "Track.flp");
        var initialBytes = CreateFlp();
        await File.WriteAllBytesAsync(flpPath, initialBytes);

        var initUseCase = new InitRepositoryUseCase(ContextFactory);
        await initUseCase.ExecuteAsync(new InitRequest(temp.Path, "CrashTest", "Track.flp"));

        var commitUseCase = new CommitUseCase(ContextFactory, Registry);
        var commit1 = await commitUseCase.ExecuteAsync(new CommitRequest(temp.Path, "Commit 1"));

        // Maak Commit 2 met nieuwe sample
        var wavBytes = new byte[] { 5, 6, 7 };
        var wavPath = Path.Combine(temp.Path, "Sound.wav");
        await File.WriteAllBytesAsync(wavPath, wavBytes);

        var addUseCase = new AddUseCase(ContextFactory);
        await addUseCase.ExecuteAsync(new AddRequest(temp.Path, ["Sound.wav"]));
        var commit2 = await commitUseCase.ExecuteAsync(new CommitRequest(temp.Path, "Commit 2"));

        // Simuleer dat een vereist blobobject voor commit 2 ontbreekt/corrupt raakt in de object store
        using (var repo = ContextFactory(temp.Path))
        {
            var snap2 = await repo.LoadSnapshotAsync(commit2.SnapshotId);
            var entry = snap2!.Project.Root.GetEntries().First(e => e.Path.Value == "Sound.wav");
            var hex = entry.Blob.Value.ToString();
            var blobFile = Path.Combine(temp.Path, ".dawvc", "objects", hex[..2], hex[2..]);
            if (File.Exists(blobFile))
            {
                File.Delete(blobFile); // Object is nu missing
            }
        }

        // When checkout naar commit 2 faalt tijdens validatie/staging
        var checkoutUseCase = new CheckoutUseCase(ContextFactory);
        var act = () => checkoutUseCase.ExecuteAsync(new CheckoutRequest(temp.Path, commit2.CommitId.ToString()));

        await act.Should().ThrowAsync<CheckoutStagingException>();

        // Then blijft de bestaande workspace byte-exact onaangeraakt
        (await File.ReadAllBytesAsync(flpPath)).Should().Equal(initialBytes);
        File.Exists(wavPath).Should().BeTrue(); // Oorspronkelijke file is niet overschreven of verwijderd

        // En staging directories zijn niet blijven hangen als publicatie
        var stagingDirs = Directory.GetDirectories(Path.Combine(temp.Path, ".dawvc"), ".staging_checkout_*");
        stagingDirs.Should().BeEmpty();
    }

    [Fact]
    [Trait("Requirement", "AC-013")]
    public async Task Ac013_CrossMachineCheckout_MaterializesBundledAssetsAndShowsFLStudioInstructions()
    {
        // Given een repository die op machine A is gemaakt met gebundelde sample
        using var machineA = new TempDirectory();
        var sampleBytes = new byte[] { 0x52, 0x49, 0x46, 0x46, 0x10, 0x00, 0x00, 0x00, 0x57, 0x41, 0x56, 0x45 };
        var samplePathA = Path.Combine(machineA.Path, "Audio", "VocalLead.wav");
        Directory.CreateDirectory(Path.GetDirectoryName(samplePathA)!);
        await File.WriteAllBytesAsync(samplePathA, sampleBytes);

        var flpBytes = CreateFlp(samplePaths: ["Audio/VocalLead.wav"]);
        var flpPathA = Path.Combine(machineA.Path, "Track.flp");
        await File.WriteAllBytesAsync(flpPathA, flpBytes);

        var initUseCase = new InitRepositoryUseCase(ContextFactory);
        await initUseCase.ExecuteAsync(new InitRequest(machineA.Path, "CrossMachineTest", "Track.flp"));

        var addUseCase = new AddUseCase(ContextFactory);
        await addUseCase.ExecuteAsync(new AddRequest(machineA.Path, ["Audio/VocalLead.wav"]));

        var commitUseCase = new CommitUseCase(ContextFactory, Registry);
        var commitResult = await commitUseCase.ExecuteAsync(new CommitRequest(machineA.Path, "Commit with sample"));

        // When machine B een checkout uitvoert (via --restore-to naar een nieuwe workspace B)
        using var machineB = new TempDirectory();
        var checkoutUseCase = new CheckoutUseCase(ContextFactory);
        var result = await checkoutUseCase.ExecuteAsync(new CheckoutRequest(
            machineA.Path,
            commitResult.CommitId.ToString(),
            RestoreToDirectory: machineB.Path));

        // Then zijn alle gebundelde bytes aanwezig op machine B
        var restoredSamplePath = Path.Combine(machineB.Path, "Audio", "VocalLead.wav");
        File.Exists(restoredSamplePath).Should().BeTrue();
        (await File.ReadAllBytesAsync(restoredSamplePath)).Should().Equal(sampleBytes);

        // And toont de checkout concrete FL Studio search-path instructies
        result.ManualInstructions.Should().NotBeNull();
        result.ManualInstructions.Should().Contain(i => i.Contains("FL Studio configuration") && i.Contains("Browser extra search folders"));

        // And een lokaal verplaatste identieke asset wordt via content-hash herkend
        var relocatedDir = Path.Combine(machineB.Path, "RelocatedSamples");
        Directory.CreateDirectory(relocatedDir);
        var relocatedPath = Path.Combine(relocatedDir, "DifferentName.wav");
        await File.WriteAllBytesAsync(relocatedPath, sampleBytes);

        var sampleHash = DawVcs.Domain.Hashing.Blake3ContentHasher.Hash(sampleBytes);
        var dep = new AssetDependency(
            DependencyId.ForAsset("MissingOriginal.wav"),
            "MissingOriginal.wav",
            DependencyRequirement.Required,
            DependencySource.NativeProjectParser,
            PortabilityPolicy.BundleDefault,
            new ArtifactPath("Audio/VocalLead.wav"),
            null,
            sampleHash,
            sampleBytes.Length,
            false);

        var binding = await DependencyResolverPipeline.ResolveAsync(machineB.Path, dep);
        binding.Status.Should().Be(BindingStatus.Verified);
        binding.VerifiedHash.Should().Be(sampleHash);
    }

    private sealed class TempDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "dawvc-checkout-test-" + Guid.NewGuid().ToString("N"));

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
