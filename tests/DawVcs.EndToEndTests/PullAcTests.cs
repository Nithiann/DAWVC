using System.Buffers.Binary;
using System.Text;

using DawVcs.Adapters.Abstractions;
using DawVcs.Adapters.FLStudio;
using DawVcs.Application.Adapters;
using DawVcs.Application.Checkouts;
using DawVcs.Application.Commits;
using DawVcs.Application.Diagnostics;
using DawVcs.Application.Exceptions;
using DawVcs.Application.Remotes;
using DawVcs.Application.Repositories;
using DawVcs.Application.Staging;
using DawVcs.Domain.Artifacts;
using DawVcs.Domain.Common;
using DawVcs.Domain.Hashing;
using DawVcs.Domain.Remotes;
using DawVcs.Domain.Repositories;
using DawVcs.Domain.Serialization;
using DawVcs.Domain.Storage;
using DawVcs.Infrastructure.Remotes;
using DawVcs.Infrastructure.Repositories;
using DawVcs.Infrastructure.Storage;

using FluentAssertions;

using Xunit;

namespace DawVcs.EndToEndTests;

public sealed class PullAcTests
{
    private static Func<string, IRepositoryContext> ContextFactory => dir => new FileSystemRepositoryContext(dir);
    private static IDawAdapterRegistry Registry => new DawAdapterRegistry([new FLStudioAdapter()]);
    private static IRemoteTransportFactory TransportFactory => new DefaultRemoteTransportFactory();
    private static Func<string, IQuarantineStore> QuarantineFactory => dir => new QuarantineObjectStore(dir);

    [Fact]
    [Trait("Category", "EndToEnd")]
    public async Task AC030_FastForwardPull_SynchronizesWorkspaceAndUpdatesRefs()
    {
        // GIVEN: Machine A inits repo, commits v1 with an audio sample in Samples/
        using var machineADir = new TempDirectory("machine_a_");
        using var remoteShareDir = new TempDirectory("remote_nas_");
        using var machineBDir = new TempDirectory("machine_b_");

        Directory.CreateDirectory(Path.Combine(machineADir.Path, "Samples"));
        var sample1Bytes = new byte[] { 0x52, 0x49, 0x46, 0x46, 1, 2, 3, 4 };
        var sample1Path = Path.Combine(machineADir.Path, "Samples", "Kick.wav");
        await File.WriteAllBytesAsync(sample1Path, sample1Bytes);

        var flpv1Bytes = CreateFlp(samplePaths: [sample1Path]);
        var flpPath = Path.Combine(machineADir.Path, "Song.flp");
        await File.WriteAllBytesAsync(flpPath, flpv1Bytes);

        var initA = new InitRepositoryUseCase(ContextFactory);
        await initA.ExecuteAsync(new InitRequest(machineADir.Path, "SyncSong", "Song.flp"));

        var commitA = new CommitUseCase(ContextFactory, Registry);
        var commitV1 = await commitA.ExecuteAsync(new CommitRequest(machineADir.Path, "V1 commit"));

        // Simulate Central Remote (UNC / NAS share or folder): clone Machine A into remoteShareDir
        var cloneUseCase = new CloneUseCase(ContextFactory, TransportFactory, new FetchUseCase(ContextFactory, TransportFactory, QuarantineFactory), new CheckoutUseCase(ContextFactory, Registry));
        await cloneUseCase.ExecuteAsync(new CloneRequest(machineADir.Path, remoteShareDir.Path));

        // Machine B clones from remote share
        var cloneResultB = await cloneUseCase.ExecuteAsync(new CloneRequest(remoteShareDir.Path, machineBDir.Path));
        cloneResultB.HeadCommit.Should().Be(commitV1.CommitId);

        // Machine A creates V2 commit (adds a Snare sample, updates flp)
        var sample2Bytes = new byte[] { 0x52, 0x49, 0x46, 0x46, 9, 8, 7, 6 };
        var sample2Path = Path.Combine(machineADir.Path, "Samples", "Snare.wav");
        await File.WriteAllBytesAsync(sample2Path, sample2Bytes);

        var flpv2Bytes = CreateFlp(samplePaths: [sample1Path, sample2Path]);
        await File.WriteAllBytesAsync(flpPath, flpv2Bytes);

        var commitV2 = await commitA.ExecuteAsync(new CommitRequest(machineADir.Path, "V2 commit with Snare"));

        // Machine A pushes to remote share (simulated by updating remoteShareDir objects & refs)
        SyncRepoToRemote(machineADir.Path, remoteShareDir.Path);

        // WHEN: Machine B pulls from remote
        var pullUseCase = new PullUseCase(
            ContextFactory,
            new FetchUseCase(ContextFactory, TransportFactory, QuarantineFactory),
            new CheckoutUseCase(ContextFactory, Registry),
            new AncestryService(),
            new DoctorUseCase(ContextFactory, Registry));

        var pullResult = await pullUseCase.ExecuteAsync(new PullRequest(machineBDir.Path));

        // THEN: Pull succeeds as fast-forward, updates branch pointer and working tree
        pullResult.State.Should().Be(SyncAncestryState.FastForward);
        pullResult.NewCommit.Should().Be(commitV2.CommitId);

        var contextB = ContextFactory(machineBDir.Path);
        contextB.GetBranchCommit(BranchName.Main).Should().Be(commitV2.CommitId);
        contextB.GetRemoteTrackingCommit("origin", BranchName.Main).Should().Be(commitV2.CommitId);

        // Verify physical files on Machine B
        File.Exists(Path.Combine(machineBDir.Path, "Song.flp")).Should().BeTrue();
        (await File.ReadAllBytesAsync(Path.Combine(machineBDir.Path, "Song.flp"))).Should().Equal(flpv2Bytes);

        File.Exists(Path.Combine(machineBDir.Path, "Samples", "Snare.wav")).Should().BeTrue();
        (await File.ReadAllBytesAsync(Path.Combine(machineBDir.Path, "Samples", "Snare.wav"))).Should().Equal(sample2Bytes);
    }

    [Fact]
    [Trait("Category", "EndToEnd")]
    public async Task AC031_Fetch_UpdatesRemoteTrackingRefs_DoesNotTouchWorkspaceOrHead()
    {
        // GIVEN: Machine A has commits, Machine B cloned V1
        using var machineADir = new TempDirectory("fetch_machine_a_");
        using var machineBDir = new TempDirectory("fetch_machine_b_");

        var flpBytes = CreateFlp();
        await File.WriteAllBytesAsync(Path.Combine(machineADir.Path, "Song.flp"), flpBytes);

        var initA = new InitRepositoryUseCase(ContextFactory);
        await initA.ExecuteAsync(new InitRequest(machineADir.Path, "FetchTest", "Song.flp"));

        var commitA = new CommitUseCase(ContextFactory, Registry);
        var commitV1 = await commitA.ExecuteAsync(new CommitRequest(machineADir.Path, "V1"));

        var cloneUseCase = new CloneUseCase(ContextFactory, TransportFactory, new FetchUseCase(ContextFactory, TransportFactory, QuarantineFactory), new CheckoutUseCase(ContextFactory, Registry));
        await cloneUseCase.ExecuteAsync(new CloneRequest(machineADir.Path, machineBDir.Path));

        // Machine A creates V2 by modifying Song.flp
        var extraPath = Path.Combine(machineADir.Path, "Extra.wav");
        await File.WriteAllBytesAsync(extraPath, [0x52, 0x49, 0x46, 0x46, 1, 2, 3, 4]);
        var flpv2Bytes = CreateFlp(samplePaths: [extraPath]);
        await File.WriteAllBytesAsync(Path.Combine(machineADir.Path, "Song.flp"), flpv2Bytes);
        var commitV2 = await commitA.ExecuteAsync(new CommitRequest(machineADir.Path, "V2"));

        // WHEN: Machine B executes FetchUseCase
        var fetchUseCase = new FetchUseCase(ContextFactory, TransportFactory, QuarantineFactory);
        var fetchResult = await fetchUseCase.ExecuteAsync(new FetchRequest(machineBDir.Path, "origin"));

        // THEN: Remote tracking ref is updated to V2, but local branch and HEAD remain at V1
        fetchResult.UpdatedRefs.Should().HaveCount(1);
        fetchResult.UpdatedRefs[0].NewCommit.Should().Be(commitV2.CommitId);

        var contextB = ContextFactory(machineBDir.Path);
        contextB.GetRemoteTrackingCommit("origin", BranchName.Main).Should().Be(commitV2.CommitId);
        contextB.GetBranchCommit(BranchName.Main).Should().Be(commitV1.CommitId, "Local HEAD must remain untouched by fetch (AC-031)");
    }

    [Fact]
    [Trait("Category", "EndToEnd")]
    public async Task AC032_DirtyWorkspaceGuard_RejectsPullWithoutForce_AndSucceedsWithForce()
    {
        // GIVEN: Remote has V2, Machine B has uncommitted local changes
        using var machineADir = new TempDirectory("guard_machine_a_");
        using var machineBDir = new TempDirectory("guard_machine_b_");

        var flpBytes = CreateFlp();
        await File.WriteAllBytesAsync(Path.Combine(machineADir.Path, "Song.flp"), flpBytes);

        var initA = new InitRepositoryUseCase(ContextFactory);
        await initA.ExecuteAsync(new InitRequest(machineADir.Path, "GuardTest", "Song.flp"));

        var commitA = new CommitUseCase(ContextFactory, Registry);
        var commitV1 = await commitA.ExecuteAsync(new CommitRequest(machineADir.Path, "V1"));

        var cloneUseCase = new CloneUseCase(ContextFactory, TransportFactory, new FetchUseCase(ContextFactory, TransportFactory, QuarantineFactory), new CheckoutUseCase(ContextFactory, Registry));
        await cloneUseCase.ExecuteAsync(new CloneRequest(machineADir.Path, machineBDir.Path));

        // Machine A creates V2
        var extraPath = Path.Combine(machineADir.Path, "Extra.wav");
        await File.WriteAllBytesAsync(extraPath, [0x52, 0x49, 0x46, 0x46, 1, 2, 3, 4]);
        var flpv2Bytes = CreateFlp(samplePaths: [extraPath]);
        await File.WriteAllBytesAsync(Path.Combine(machineADir.Path, "Song.flp"), flpv2Bytes);
        var commitV2 = await commitA.ExecuteAsync(new CommitRequest(machineADir.Path, "V2"));

        // Machine B modifies Song.flp locally without committing
        var dirtyFlpBytes = new byte[] { 0x00, 0x11, 0x22, 0x33 };
        await File.WriteAllBytesAsync(Path.Combine(machineBDir.Path, "Song.flp"), dirtyFlpBytes);

        var pullUseCase = new PullUseCase(
            ContextFactory,
            new FetchUseCase(ContextFactory, TransportFactory, QuarantineFactory),
            new CheckoutUseCase(ContextFactory, Registry),
            new AncestryService(),
            new DoctorUseCase(ContextFactory, Registry));

        // WHEN: Machine B pulls without --force
        var act = () => pullUseCase.ExecuteAsync(new PullRequest(machineBDir.Path, Force: false));

        // THEN: Rejected with DirtyWorkspaceException (mapped to Exit Code 4, AC-032)
        await act.Should().ThrowAsync<DirtyWorkspaceException>();

        // Local changes still intact
        (await File.ReadAllBytesAsync(Path.Combine(machineBDir.Path, "Song.flp"))).Should().Equal(dirtyFlpBytes);

        // WHEN: Machine B pulls with --force
        var forceResult = await pullUseCase.ExecuteAsync(new PullRequest(machineBDir.Path, Force: true));

        // THEN: Recovery copy created and pull succeeds
        forceResult.State.Should().Be(SyncAncestryState.FastForward);
        forceResult.RecoveryDirectory.Should().NotBeNull();
        Directory.Exists(forceResult.RecoveryDirectory!).Should().BeTrue();
    }

    [Fact]
    [Trait("Category", "EndToEnd")]
    public async Task AC033_DivergedBranches_RejectedWithExitCode9_WithoutDataLoss()
    {
        // GIVEN: Machine A and Machine B diverge from V1
        using var machineADir = new TempDirectory("diverge_machine_a_");
        using var machineBDir = new TempDirectory("diverge_machine_b_");

        var flpBytes = CreateFlp();
        await File.WriteAllBytesAsync(Path.Combine(machineADir.Path, "Song.flp"), flpBytes);

        var initA = new InitRepositoryUseCase(ContextFactory);
        await initA.ExecuteAsync(new InitRequest(machineADir.Path, "DivergeTest", "Song.flp"));

        var commitA = new CommitUseCase(ContextFactory, Registry);
        var commitV1 = await commitA.ExecuteAsync(new CommitRequest(machineADir.Path, "V1 common root"));

        var cloneUseCase = new CloneUseCase(ContextFactory, TransportFactory, new FetchUseCase(ContextFactory, TransportFactory, QuarantineFactory), new CheckoutUseCase(ContextFactory, Registry));
        await cloneUseCase.ExecuteAsync(new CloneRequest(machineADir.Path, machineBDir.Path));

        // Machine A commits V2-A
        var sampleAPath = Path.Combine(machineADir.Path, "SampleA.wav");
        await File.WriteAllBytesAsync(sampleAPath, [0x52, 0x49, 0x46, 0x46, 1, 1, 1, 1]);
        var flpv2ABytes = CreateFlp(samplePaths: [sampleAPath]);
        await File.WriteAllBytesAsync(Path.Combine(machineADir.Path, "Song.flp"), flpv2ABytes);
        var commitV2A = await commitA.ExecuteAsync(new CommitRequest(machineADir.Path, "Commit V2-A on remote"));

        // Machine B commits V2-B locally (divergence!)
        var sampleBPath = Path.Combine(machineBDir.Path, "SampleB.wav");
        await File.WriteAllBytesAsync(sampleBPath, [0x52, 0x49, 0x46, 0x46, 2, 2, 2, 2]);
        var flpv2BBytes = CreateFlp(samplePaths: [sampleBPath]);
        await File.WriteAllBytesAsync(Path.Combine(machineBDir.Path, "Song.flp"), flpv2BBytes);
        var commitB = new CommitUseCase(ContextFactory, Registry);
        var commitV2B = await commitB.ExecuteAsync(new CommitRequest(machineBDir.Path, "Commit V2-B on local"));

        var pullUseCase = new PullUseCase(
            ContextFactory,
            new FetchUseCase(ContextFactory, TransportFactory, QuarantineFactory),
            new CheckoutUseCase(ContextFactory, Registry),
            new AncestryService(),
            new DoctorUseCase(ContextFactory, Registry));

        // WHEN: Machine B pulls
        var act = () => pullUseCase.ExecuteAsync(new PullRequest(machineBDir.Path));

        // THEN: Rejected with RejectedNonFastForwardException (Exit Code 9, AC-033)
        var ex = await act.Should().ThrowAsync<RejectedNonFastForwardException>();
        ex.Which.LocalCommit.Should().Be(commitV2B.CommitId);
        ex.Which.RemoteCommit.Should().Be(commitV2A.CommitId);

        // Remote tracking ref origin/main was updated, but local main remains at V2B
        var contextB = ContextFactory(machineBDir.Path);
        contextB.GetBranchCommit(BranchName.Main).Should().Be(commitV2B.CommitId);
        contextB.GetRemoteTrackingCommit("origin", BranchName.Main).Should().Be(commitV2A.CommitId);
    }

    [Fact]
    [Trait("Category", "EndToEnd")]
    public async Task AC034_CorruptedRemotePayload_RejectedInQuarantine_NoObjectsLeakToStore()
    {
        // GIVEN: Remote repository has a corrupted blob payload file
        using var remoteDir = new TempDirectory("corrupt_remote_");
        using var localDir = new TempDirectory("local_fetch_");

        var flpBytes = CreateFlp();
        await File.WriteAllBytesAsync(Path.Combine(remoteDir.Path, "Song.flp"), flpBytes);

        var initRemote = new InitRepositoryUseCase(ContextFactory);
        await initRemote.ExecuteAsync(new InitRequest(remoteDir.Path, "CorruptTest", "Song.flp"));

        var commitRemote = new CommitUseCase(ContextFactory, Registry);
        var commit = await commitRemote.ExecuteAsync(new CommitRequest(remoteDir.Path, "V1"));

        var cloneUseCase = new CloneUseCase(ContextFactory, TransportFactory, new FetchUseCase(ContextFactory, TransportFactory, QuarantineFactory), new CheckoutUseCase(ContextFactory, Registry));
        await cloneUseCase.ExecuteAsync(new CloneRequest(remoteDir.Path, localDir.Path));

        // Remote commits V2
        var sampleV2Path = Path.Combine(remoteDir.Path, "SampleV2.wav");
        await File.WriteAllBytesAsync(sampleV2Path, [0x52, 0x49, 0x46, 0x46, 3, 3, 3, 3]);
        var flpv2Bytes = CreateFlp(samplePaths: [sampleV2Path]);
        await File.WriteAllBytesAsync(Path.Combine(remoteDir.Path, "Song.flp"), flpv2Bytes);
        var commitV2 = await commitRemote.ExecuteAsync(new CommitRequest(remoteDir.Path, "V2"));

        // Corrupt commit object file on remote
        var remoteObjectsDir = Path.Combine(remoteDir.Path, ".dawvc", "objects");
        var localObjectsDir = Path.Combine(localDir.Path, ".dawvc", "objects");
        var localHashes = Directory.GetFiles(localObjectsDir, "*", SearchOption.AllDirectories)
            .Select(Path.GetFileName)
            .ToHashSet();

        var commitHex = commitV2.CommitId.Value.ToString();
        var corruptCommitFile = Path.Combine(remoteObjectsDir, commitHex[..2], commitHex[2..]);

        var bytes = await File.ReadAllBytesAsync(corruptCommitFile);
        bytes[^1] = (byte)(bytes[^1] ^ 0xFF); // flip bits in payload
        await File.WriteAllBytesAsync(corruptCommitFile, bytes);

        // WHEN: Local runs fetch
        var fetchUseCase = new FetchUseCase(ContextFactory, TransportFactory, QuarantineFactory);
        var act = () => fetchUseCase.ExecuteAsync(new FetchRequest(localDir.Path, "origin"));

        // THEN: Rejected in quarantine with hash mismatch
        await act.Should().ThrowAsync<PayloadHashMismatchException>();

        // Zero new objects leaked to local objects/ directory
        var localObjectsAfter = Directory.GetFiles(localObjectsDir, "*", SearchOption.AllDirectories)
            .Where(f => !f.Contains(".tmp"));

        localObjectsAfter.Select(Path.GetFileName).Should().BeEquivalentTo(localHashes, "Quarantine must prevent corrupt objects from entering the local object store (AC-034)");
    }

    [Fact]
    [Trait("Category", "EndToEnd")]
    public async Task AC036_Clone_InitializesAndMaterializesWith100PercentDoctorScore()
    {
        // GIVEN: Remote repository with primary file and bundled sample
        using var remoteDir = new TempDirectory("clone_source_");
        using var targetDir = new TempDirectory("clone_dest_");

        Directory.CreateDirectory(Path.Combine(remoteDir.Path, "Samples"));
        var kickBytes = new byte[] { 0x52, 0x49, 0x46, 0x46, 11, 22, 33, 44 };
        var kickPath = Path.Combine(remoteDir.Path, "Samples", "Kick.wav");
        await File.WriteAllBytesAsync(kickPath, kickBytes);

        var flpBytes = CreateFlp(samplePaths: [kickPath]);
        var flpPath = Path.Combine(remoteDir.Path, "Song.flp");
        await File.WriteAllBytesAsync(flpPath, flpBytes);

        var initRemote = new InitRepositoryUseCase(ContextFactory);
        await initRemote.ExecuteAsync(new InitRequest(remoteDir.Path, "CloneTest", "Song.flp"));

        var commitRemote = new CommitUseCase(ContextFactory, Registry);
        var commit = await commitRemote.ExecuteAsync(new CommitRequest(remoteDir.Path, "Initial commit"));

        // WHEN: Executing CloneUseCase
        var cloneUseCase = new CloneUseCase(
            ContextFactory,
            TransportFactory,
            new FetchUseCase(ContextFactory, TransportFactory, QuarantineFactory),
            new CheckoutUseCase(ContextFactory, Registry),
            new DoctorUseCase(ContextFactory, Registry));

        var cloneDestPath = Path.Combine(targetDir.Path, "MyClonedSong");
        var result = await cloneUseCase.ExecuteAsync(new CloneRequest(remoteDir.Path, cloneDestPath));

        // THEN: Project is cloned, materialized, and has 100% doctor readiness score
        result.ProjectName.Should().Be("CloneTest");
        result.DefaultBranch.Should().Be(BranchName.Main);
        result.HeadCommit.Should().Be(commit.CommitId);

        File.Exists(Path.Combine(cloneDestPath, "Song.flp")).Should().BeTrue();
        File.Exists(Path.Combine(cloneDestPath, "Samples", "Kick.wav")).Should().BeTrue();

        result.DoctorSummary.Should().NotBeNull();
        result.DoctorSummary!.ReproducibilityScore.Should().Be(100.0);
    }

    private static void SyncRepoToRemote(string sourceRepoDir, string remoteRepoDir)
    {
        // Copies objects and refs from source to remote
        var srcObjects = Path.Combine(sourceRepoDir, ".dawvc", "objects");
        var dstObjects = Directory.Exists(Path.Combine(remoteRepoDir, ".dawvc"))
            ? Path.Combine(remoteRepoDir, ".dawvc", "objects")
            : Path.Combine(remoteRepoDir, "objects");

        CopyDirectory(srcObjects, dstObjects);

        var srcHeads = Path.Combine(sourceRepoDir, ".dawvc", "refs", "heads");
        var dstHeads = Directory.Exists(Path.Combine(remoteRepoDir, ".dawvc"))
            ? Path.Combine(remoteRepoDir, ".dawvc", "refs", "heads")
            : Path.Combine(remoteRepoDir, "refs", "heads");

        CopyDirectory(srcHeads, dstHeads);
    }

    private static void CopyDirectory(string sourceDir, string destinationDir)
    {
        if (!Directory.Exists(sourceDir)) return;
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

        public TempDirectory(string prefix = "dawvc-ac-")
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), prefix + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public void Dispose()
        {
            try { Directory.Delete(Path, recursive: true); } catch { }
        }
    }

    private static byte[] CreateFlp(string[]? samplePaths = null)
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
