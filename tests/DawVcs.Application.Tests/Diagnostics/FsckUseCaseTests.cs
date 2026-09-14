using DawVcs.Adapters.Abstractions;
using DawVcs.Application.Adapters;
using DawVcs.Application.Diagnostics;
using DawVcs.Domain.Artifacts;
using DawVcs.Domain.Common;
using DawVcs.Domain.Entities;
using DawVcs.Domain.Hashing;
using DawVcs.Domain.Repositories;
using DawVcs.Domain.Storage;

using FluentAssertions;

using NSubstitute;
using NSubstitute.ExceptionExtensions;

using Xunit;

namespace DawVcs.Application.Tests.Diagnostics;

public sealed class FsckUseCaseTests
{
    private readonly IRepositoryContext _context;
    private readonly IObjectStore _objectStore;
    private readonly IDawAdapterRegistry _adapterRegistry;
    private readonly IDawAdapter _mockAdapter;

    public FsckUseCaseTests()
    {
        _objectStore = Substitute.For<IObjectStore>();
        _context = Substitute.For<IRepositoryContext>();
        _context.RootPath.Returns("C:\\TestRepo");
        _context.ObjectStore.Returns(_objectStore);

        _mockAdapter = Substitute.For<IDawAdapter>();
        _mockAdapter.DawName.Returns("FL Studio");
        _mockAdapter.SupportedExtensions.Returns([".flp"]);

        _adapterRegistry = Substitute.For<IDawAdapterRegistry>();
        _adapterRegistry.FindAdapterForExtension(".flp").Returns(_mockAdapter);
    }

    [Fact]
    public async Task Fsck_WhenStoreHasCorruptObject_ReportsRepositoryIntegrityError()
    {
        var corruptHash = Blake3ContentHasher.Hash(new byte[] { 0xDE, 0xAD });
        var entry = new StoredObjectEntry(corruptHash.ToString(), corruptHash, true);

        _objectStore.EnumerateStoredObjects().Returns([entry]);
        _objectStore.VerifyObjectIntegrityAsync(corruptHash, Arg.Any<CancellationToken>())
            .ThrowsAsync(new PayloadHashMismatchException("Payload hash mismatch!"));

        _context.GetBranches().Returns(new List<BranchInfo>());
        _context.GetCurrentBranch().Returns(BranchName.Main);
        _context.GetBranchCommit(BranchName.Main).Returns((CommitId?)null);

        var useCase = new FsckUseCase(_ => _context, _adapterRegistry);
        var report = await useCase.ExecuteAsync(new FsckRequest("C:\\TestRepo"));

        report.IsHealthy.Should().BeFalse();
        report.RepositoryIntegrity.IsIntact.Should().BeFalse();
        report.RepositoryIntegrity.Errors.Should().ContainSingle(e => e.Code == "ObjectHashMismatch");
    }

    [Fact]
    public async Task Fsck_WhenOrphanObjectExists_ReportsWarningWithoutFailingIntegrity()
    {
        var orphanHash = Blake3ContentHasher.Hash(new byte[] { 1, 2, 3 });
        var entry = new StoredObjectEntry(orphanHash.ToString(), orphanHash, true);

        _objectStore.EnumerateStoredObjects().Returns([entry]);
        _context.GetBranches().Returns(new List<BranchInfo>());
        _context.GetCurrentBranch().Returns(BranchName.Main);
        _context.GetBranchCommit(BranchName.Main).Returns((CommitId?)null);

        var useCase = new FsckUseCase(_ => _context, _adapterRegistry);
        var report = await useCase.ExecuteAsync(new FsckRequest("C:\\TestRepo"));

        report.RepositoryIntegrity.IsIntact.Should().BeTrue();
        report.RepositoryIntegrity.OrphanObjectsCount.Should().Be(1);
        report.RepositoryIntegrity.Warnings.Should().ContainSingle(w => w.Code == "OrphanObject");
        report.IsHealthy.Should().BeTrue();
    }

    [Fact]
    [Trait("Requirement", "AC-012")]
    public async Task Ac012_ArtifactIntegrityFailure_IsNotReportedAsObjectHashMismatch()
    {
        // GIVEN een cryptografisch intact object waarvan de bytes matchen met de BLAKE3 hash
        var flpPayload = new byte[] { 0x46, 0x4C, 0x00, 0x00 };
        var blobHash = Blake3ContentHasher.Hash(flpPayload);
        var blobEntry = new StoredObjectEntry(blobHash.ToString(), blobHash, true);

        var snapshotId = new SnapshotId(Blake3ContentHasher.Hash(new byte[] { 0x02 }));
        var snapshotEntry = new StoredObjectEntry(snapshotId.Value.ToString(), snapshotId.Value, true);

        var commitId = new CommitId(Blake3ContentHasher.Hash(new byte[] { 0x03 }));
        var commitEntry = new StoredObjectEntry(commitId.Value.ToString(), commitId.Value, true);

        _objectStore.EnumerateStoredObjects().Returns([blobEntry, snapshotEntry, commitEntry]);
        _objectStore.Exists(blobHash).Returns(true);
        _objectStore.Exists(snapshotId.Value).Returns(true);
        _objectStore.Exists(commitId.Value).Returns(true);
        _objectStore.ReadObjectPayloadAsync(blobHash, Arg.Any<CancellationToken>()).Returns(flpPayload);

        // Object graph wiring
        var branch = new BranchInfo(BranchName.Main, commitId, true);
        _context.GetBranches().Returns([branch]);
        _context.GetCurrentBranch().Returns(BranchName.Main);
        _context.GetBranchCommit(BranchName.Main).Returns(commitId);

        var commit = new Commit([], snapshotId, "Author", DateTimeOffset.UtcNow, "Test commit", commitId);
        _context.LoadCommitAsync(commitId, Arg.Any<CancellationToken>()).Returns(commit);

        var project = ProjectArtifact.CreateSingleFile("FL Studio", new ArtifactPath("Track.flp"), blobHash, 4);
        var snapshot = new ProjectSnapshot(project, DateTimeOffset.UtcNow, id: snapshotId);
        _context.LoadSnapshotAsync(snapshotId, Arg.Any<CancellationToken>()).Returns(snapshot);

        // De FL Studio adapter faalt op de payload en meldt Invalid
        _mockAdapter.DetectAsync(Arg.Any<Stream>(), Arg.Any<CancellationToken>())
            .Returns(ProjectDetectionResult.Invalid("FL Studio", "Malformed FLP header."));

        // WHEN fsck wordt aangeroepen met --artifacts
        var useCase = new FsckUseCase(_ => _context, _adapterRegistry);
        var report = await useCase.ExecuteAsync(new FsckRequest("C:\\TestRepo", CheckArtifacts: true));

        // THEN:
        // 1. Repository integrity is INTACT (geen envelope/hash corruptie)
        report.RepositoryIntegrity.IsIntact.Should().BeTrue();
        report.RepositoryIntegrity.Errors.Should().BeEmpty();
        report.RepositoryIntegrity.Errors.Should().NotContain(e => e.Code == "ObjectHashMismatch");

        // 2. Artifact integrity meldt de fout expliciet onder Artifact Integrity
        report.ArtifactIntegrity.Should().NotBeNull();
        report.ArtifactIntegrity!.IsIntact.Should().BeFalse();
        report.ArtifactIntegrity.FailedArtifactsCount.Should().Be(1);
        report.ArtifactIntegrity.Errors.Should().ContainSingle(e => e.Code == "InvalidArtifactContent");

        // 3. Totaal rapport is niet healthy vanwege artifact fout
        report.IsHealthy.Should().BeFalse();
    }
}
