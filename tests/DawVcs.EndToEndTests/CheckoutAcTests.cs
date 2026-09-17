using System.Buffers.Binary;
using System.Text;

using DawVcs.Adapters.FLStudio;
using DawVcs.Application.Adapters;
using DawVcs.Application.Branches;
using DawVcs.Application.Checkouts;
using DawVcs.Application.Commits;
using DawVcs.Application.Dependencies;
using DawVcs.Application.Exceptions;
using DawVcs.Application.Repositories;
using DawVcs.Application.Staging;
using DawVcs.Domain.Artifacts;
using DawVcs.Domain.Common;
using DawVcs.Domain.Configuration;
using DawVcs.Domain.Dependencies;
using DawVcs.Domain.Entities;
using DawVcs.Domain.Hashing;
using DawVcs.Domain.Repositories;
using DawVcs.Domain.Storage;
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

        // Maak feature branch 'feature' aan op commit 1
        var branchUseCase = new BranchUseCase(ContextFactory);
        await branchUseCase.CreateAsync(new BranchCreateRequest(temp.Path, "feature"));

        // Maak een tweede commit op main
        var sampleBytes = new byte[] { 1, 2, 3, 4, 5 };
        var samplePath = Path.Combine(temp.Path, "Kick.wav");
        await File.WriteAllBytesAsync(samplePath, sampleBytes);

        var addUseCase = new AddUseCase(ContextFactory);
        await addUseCase.ExecuteAsync(new AddRequest(temp.Path, ["Kick.wav"]));

        var commit2 = await commitUseCase.ExecuteAsync(new CommitRequest(temp.Path, "Commit with kick"));

        // Maak de workspace dirty door een lokaal bestand aan te passen zonder commit
        var dirtyBytes = new byte[] { 0xDE, 0xAD, 0xBE, 0xEF };
        await File.WriteAllBytesAsync(flpPath, dirtyBytes);

        // When de gebruiker een normale in-place checkout uitvoert naar branch feature (zonder --force)
        var checkoutUseCase = new CheckoutUseCase(ContextFactory);
        var act = () => checkoutUseCase.ExecuteAsync(new CheckoutRequest(temp.Path, "feature", Force: false));

        // Then stopt checkout met DirtyWorkspaceException (exitcode 7)
        var ex = await act.Should().ThrowAsync<DirtyWorkspaceException>();
        ex.Which.Status.HasWorkingTreeModifications.Should().BeTrue();

        // And direct in-place checkout van een losse commit hash wordt geweigerd om branches te beschermen (Option 2)
        var commitAct = () => checkoutUseCase.ExecuteAsync(new CheckoutRequest(temp.Path, commit1.CommitId.ToString(), Force: false));
        await commitAct.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Direct checkout of a commit into the active workspace is disabled*");

        // And blijven alle lokale bytes onaangeraakt
        var currentBytes = await File.ReadAllBytesAsync(flpPath);
        currentBytes.Should().Equal(dirtyBytes);

        // When de gebruiker --restore-to gebruikt met commit hash
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

        // Maak feature branch 'v1' aan op commit 1
        var branchUseCase = new BranchUseCase(ContextFactory);
        await branchUseCase.CreateAsync(new BranchCreateRequest(temp.Path, "v1"));

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

        // When de gebruiker checkout --force uitvoert naar branch v1
        var checkoutUseCase = new CheckoutUseCase(ContextFactory);
        var result = await checkoutUseCase.ExecuteAsync(new CheckoutRequest(
            temp.Path,
            "v1",
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

        // Maak feature branch 'feature' op commit 2
        var branchUseCase = new BranchUseCase(ContextFactory);
        await branchUseCase.CreateAsync(new BranchCreateRequest(temp.Path, "feature"));

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

        // When checkout naar branch feature faalt tijdens validatie/staging
        var checkoutUseCase = new CheckoutUseCase(ContextFactory);
        var act = () => checkoutUseCase.ExecuteAsync(new CheckoutRequest(temp.Path, "feature"));

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
        var checkoutUseCase = new CheckoutUseCase(ContextFactory, Registry);
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
            DependencyId.ForAsset(sampleHash),
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

    [Fact]
    [Trait("Requirement", "AC-011")]
    public async Task Checkout_UntrackedConflictingFile_BlocksCheckoutWithoutForce_AndPreservesContent()
    {
        // Given workspace at commit 1 with only Track.flp
        using var temp = new TempDirectory();
        var flpPath = Path.Combine(temp.Path, "Track.flp");
        await File.WriteAllBytesAsync(flpPath, CreateFlp());

        var initUseCase = new InitRepositoryUseCase(ContextFactory);
        await initUseCase.ExecuteAsync(new InitRequest(temp.Path, "UntrackedConflictTest", "Track.flp"));

        var commitUseCase = new CommitUseCase(ContextFactory, Registry);
        var commit1 = await commitUseCase.ExecuteAsync(new CommitRequest(temp.Path, "Initial commit"));

        var branchUseCase = new BranchUseCase(ContextFactory);
        await branchUseCase.CreateAsync(new BranchCreateRequest(temp.Path, "v1"));

        // Commit 2 adds Kick.wav on main
        var kickInCommitBytes = new byte[] { 1, 1, 1, 1 };
        var kickPath = Path.Combine(temp.Path, "Kick.wav");
        await File.WriteAllBytesAsync(kickPath, kickInCommitBytes);

        var addUseCase = new AddUseCase(ContextFactory);
        await addUseCase.ExecuteAsync(new AddRequest(temp.Path, ["Kick.wav"]));
        var commit2 = await commitUseCase.ExecuteAsync(new CommitRequest(temp.Path, "Commit with Kick"));

        // Switch to branch v1: Kick.wav is now deleted/obsolete
        var switchUseCase = new SwitchUseCase(ContextFactory);
        await switchUseCase.ExecuteAsync(new SwitchRequest(temp.Path, "v1"));
        File.Exists(kickPath).Should().BeFalse();

        // Now user creates an untracked Kick.wav with their own local content
        var localUntrackedBytes = new byte[] { 9, 9, 9, 9, 9 };
        await File.WriteAllBytesAsync(kickPath, localUntrackedBytes);

        // When attempting checkout to branch main without --force
        var checkoutUseCase = new CheckoutUseCase(ContextFactory, Registry);
        var act = () => checkoutUseCase.ExecuteAsync(new CheckoutRequest(temp.Path, "main", Force: false));

        // Then it must throw DirtyWorkspaceException with untracked conflict details
        var ex = await act.Should().ThrowAsync<DirtyWorkspaceException>();
        ex.Which.Conflicts.Should().ContainSingle(c => c.Path.Value == "Kick.wav" && c.Reason.Contains("untracked", StringComparison.OrdinalIgnoreCase));

        // And untracked Kick.wav must NOT be overwritten
        var preservedBytes = await File.ReadAllBytesAsync(kickPath);
        preservedBytes.Should().Equal(localUntrackedBytes);
    }

    [Fact]
    [Trait("Requirement", "AC-012")]
    public async Task Checkout_DeletesObsoleteFilesFromPreviousCommit()
    {
        // Given workspace at commit 1 with Track.flp and Extra.wav
        using var temp = new TempDirectory();
        var flpPath = Path.Combine(temp.Path, "Track.flp");
        await File.WriteAllBytesAsync(flpPath, CreateFlp());

        var initUseCase = new InitRepositoryUseCase(ContextFactory);
        await initUseCase.ExecuteAsync(new InitRequest(temp.Path, "ObsoleteDeletionTest", "Track.flp"));

        var commitUseCase = new CommitUseCase(ContextFactory, Registry);
        var commit1 = await commitUseCase.ExecuteAsync(new CommitRequest(temp.Path, "Base commit"));

        var branchUseCase = new BranchUseCase(ContextFactory);
        await branchUseCase.CreateAsync(new BranchCreateRequest(temp.Path, "base"));

        // Commit 2 adds Extra.wav on main
        var extraBytes = new byte[] { 42, 43, 44 };
        var extraPath = Path.Combine(temp.Path, "Extra.wav");
        await File.WriteAllBytesAsync(extraPath, extraBytes);

        var addUseCase = new AddUseCase(ContextFactory);
        await addUseCase.ExecuteAsync(new AddRequest(temp.Path, ["Extra.wav"]));
        var commit2 = await commitUseCase.ExecuteAsync(new CommitRequest(temp.Path, "Commit with Extra"));

        File.Exists(extraPath).Should().BeTrue();

        // When checking out branch base
        var checkoutUseCase = new CheckoutUseCase(ContextFactory, Registry);
        await checkoutUseCase.ExecuteAsync(new CheckoutRequest(temp.Path, "base", Force: false));

        // Then Extra.wav should be deleted from working tree as it does not exist in branch base
        File.Exists(extraPath).Should().BeFalse();
        File.Exists(flpPath).Should().BeTrue();
    }

    [Fact]
    [Trait("Requirement", "FR-CHK-008")]
    public async Task Checkout_CommitHashWithoutRestoreTo_ThrowsInvalidOperationException()
    {
        using var temp = new TempDirectory();
        var flpPath = Path.Combine(temp.Path, "Track.flp");
        await File.WriteAllBytesAsync(flpPath, CreateFlp());

        var initUseCase = new InitRepositoryUseCase(ContextFactory);
        await initUseCase.ExecuteAsync(new InitRequest(temp.Path, "CommitCheckoutGuardTest", "Track.flp"));

        var commitUseCase = new CommitUseCase(ContextFactory, Registry);
        var commit = await commitUseCase.ExecuteAsync(new CommitRequest(temp.Path, "Initial commit"));

        var checkoutUseCase = new CheckoutUseCase(ContextFactory);
        var act = () => checkoutUseCase.ExecuteAsync(new CheckoutRequest(temp.Path, commit.CommitId.ToString(), Force: false));

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Direct checkout of a commit into the active workspace is disabled*");
    }

    [Fact]
    [Trait("Requirement", "FR-CHK-006")]
    public async Task Checkout_WhenStagingIndexClearThrows_RollsBackHeadFilesAndStagedEntries()
    {
        using var temp = new TempDirectory();
        var flpPath = Path.Combine(temp.Path, "Track.flp");
        await File.WriteAllBytesAsync(flpPath, CreateFlp());

        var initUseCase = new InitRepositoryUseCase(ContextFactory);
        await initUseCase.ExecuteAsync(new InitRequest(temp.Path, "StagingClearRollbackTest", "Track.flp"));

        var commitUseCase = new CommitUseCase(ContextFactory, Registry);
        await commitUseCase.ExecuteAsync(new CommitRequest(temp.Path, "Initial commit on main"));

        // Maak feature branch en commit Synth.wav
        var switchUseCase = new SwitchUseCase(ContextFactory, Registry);
        await switchUseCase.ExecuteAsync(new SwitchRequest(temp.Path, "feature", CreateBranch: true));

        var synthPath = Path.Combine(temp.Path, "Synth.wav");
        await File.WriteAllBytesAsync(synthPath, new byte[] { 10, 20, 30 });
        var addUseCase = new AddUseCase(ContextFactory);
        await addUseCase.ExecuteAsync(new AddRequest(temp.Path, ["Synth.wav"]));
        await commitUseCase.ExecuteAsync(new CommitRequest(temp.Path, "Commit Synth on feature"));

        // Ga terug naar main
        await switchUseCase.ExecuteAsync(new SwitchRequest(temp.Path, "main"));
        File.Exists(synthPath).Should().BeFalse();

        // Stage een bestand op main
        var localPath = Path.Combine(temp.Path, "LocalOnly.wav");
        await File.WriteAllBytesAsync(localPath, new byte[] { 1, 2, 3 });
        await addUseCase.ExecuteAsync(new AddRequest(temp.Path, ["LocalOnly.wav"]));

        // Simuleer fout tijdens het leegmaken van staging index bij checkout
        var faultyContext = new FaultyStagingIndexContext(ContextFactory(temp.Path));
        var faultyCheckoutUseCase = new CheckoutUseCase(_ => faultyContext, Registry);

        // WHEN checkout naar feature wordt uitgevoerd met force
        var act = () => faultyCheckoutUseCase.ExecuteAsync(new CheckoutRequest(temp.Path, "feature", Force: true));

        // THEN staging fout wordt gegooid
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Simulated staging index SQLite failure during clear*");

        // EN HEAD moet gerolled back zijn naar 'main'
        var normalContext = ContextFactory(temp.Path);
        normalContext.GetCurrentBranch().Value.Should().Be("main");

        // EN bestanden van feature (Synth.wav) moeten gerolled back zijn (niet aanwezig)
        File.Exists(synthPath).Should().BeFalse();

        // EN gestagete entries moeten hersteld zijn in de index
        var stagedEntries = await normalContext.StagingIndex.GetStagedEntriesAsync();
        stagedEntries.Should().Contain(e => e.Path.Value == "LocalOnly.wav");
    }

    private sealed class FaultyStagingIndexContext : IRepositoryContext
    {
        private readonly IRepositoryContext _inner;
        private readonly FaultyStagingIndex _stagingIndex;

        public FaultyStagingIndexContext(IRepositoryContext inner)
        {
            _inner = inner;
            _stagingIndex = new FaultyStagingIndex(inner.StagingIndex);
        }

        public string RootPath => _inner.RootPath;
        public IObjectStore ObjectStore => _inner.ObjectStore;
        public IStagingIndex StagingIndex => _stagingIndex;
        public ILocalBindingStore LocalBindings => _inner.LocalBindings;
        public BranchName GetCurrentBranch() => _inner.GetCurrentBranch();
        public void SetCurrentBranch(BranchName branch) => _inner.SetCurrentBranch(branch);
        public IReadOnlyList<BranchInfo> GetBranches() => _inner.GetBranches();
        public void CreateBranch(BranchName branch, CommitId commitId) => _inner.CreateBranch(branch, commitId);
        public bool DeleteBranch(BranchName branch) => _inner.DeleteBranch(branch);
        public CommitId? GetBranchCommit(BranchName branch) => _inner.GetBranchCommit(branch);
        public Task UpdateBranchCommitAsync(BranchName branch, CommitId newCommit, CancellationToken cancellationToken = default) => _inner.UpdateBranchCommitAsync(branch, newCommit, cancellationToken);
        public CommitId? ResolveReference(string reference) => _inner.ResolveReference(reference);
        public Task<Commit?> LoadCommitAsync(CommitId commitId, CancellationToken cancellationToken = default) => _inner.LoadCommitAsync(commitId, cancellationToken);
        public Task<ProjectSnapshot?> LoadSnapshotAsync(SnapshotId snapshotId, CancellationToken cancellationToken = default) => _inner.LoadSnapshotAsync(snapshotId, cancellationToken);
        public RepositoryConfig LoadConfig() => _inner.LoadConfig();
        public void SaveConfig(RepositoryConfig config) => _inner.SaveConfig(config);
        public void Dispose() => _inner.Dispose();
    }

    private sealed class FaultyStagingIndex : IStagingIndex
    {
        private readonly IStagingIndex _inner;
        public bool ShouldThrowOnClear { get; set; } = true;

        public FaultyStagingIndex(IStagingIndex inner) => _inner = inner;

        public Task ClearStagedEntriesAsync(CancellationToken cancellationToken = default)
        {
            if (ShouldThrowOnClear)
            {
                throw new InvalidOperationException("Simulated staging index SQLite failure during clear.");
            }
            return _inner.ClearStagedEntriesAsync(cancellationToken);
        }

        public Task StageEntryAsync(ArtifactEntry entry, CancellationToken cancellationToken = default) => _inner.StageEntryAsync(entry, cancellationToken);
        public Task UnstageEntryAsync(ArtifactPath path, CancellationToken cancellationToken = default) => _inner.UnstageEntryAsync(path, cancellationToken);
        public Task<IReadOnlyList<ArtifactEntry>> GetStagedEntriesAsync(CancellationToken cancellationToken = default) => _inner.GetStagedEntriesAsync(cancellationToken);
        public Task<ContentHash?> TryGetCachedHashAsync(ArtifactPath path, long size, DateTimeOffset lastModifiedUtc, CancellationToken cancellationToken = default) => _inner.TryGetCachedHashAsync(path, size, lastModifiedUtc, cancellationToken);
        public Task SetCachedHashAsync(ArtifactPath path, long size, DateTimeOffset lastModifiedUtc, ContentHash hash, CancellationToken cancellationToken = default) => _inner.SetCachedHashAsync(path, size, lastModifiedUtc, hash, cancellationToken);
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
