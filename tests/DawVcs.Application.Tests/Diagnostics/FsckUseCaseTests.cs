using DawVcs.Adapters.Abstractions;
using DawVcs.Application.Adapters;
using DawVcs.Application.Common;
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

    private ContentHash PrepareReachableArtifact()
    {
        var blob = Blake3ContentHasher.Hash([1, 2, 3]);
        var snapshotId = new SnapshotId(Blake3ContentHasher.Hash([4]));
        var commitId = new CommitId(Blake3ContentHasher.Hash([5]));
        _objectStore.EnumerateStoredObjects().Returns([new StoredObjectEntry(blob.ToString(), blob, true)]);
        _objectStore.Exists(Arg.Any<ContentHash>()).Returns(true);
        _objectStore.ReadObjectPayloadAsync(blob, Arg.Any<CancellationToken>()).Returns(new byte[] { 1, 2, 3 });
        _context.GetBranches().Returns([new BranchInfo(BranchName.Main, commitId, true)]);
        _context.GetCurrentBranch().Returns(BranchName.Main);
        _context.GetBranchCommit(BranchName.Main).Returns(commitId);
        _context.LoadCommitAsync(commitId, Arg.Any<CancellationToken>())
            .Returns(new Commit([], snapshotId, "Author", DateTimeOffset.UtcNow, "Test", commitId));
        var project = ProjectArtifact.CreateSingleFile("FL Studio", new ArtifactPath("Track.flp"), blob, 3);
        _context.LoadSnapshotAsync(snapshotId, Arg.Any<CancellationToken>())
            .Returns(new ProjectSnapshot(project, DateTimeOffset.UtcNow, id: snapshotId));
        _mockAdapter.DetectAsync(Arg.Any<Stream>(), Arg.Any<CancellationToken>())
            .Returns(ProjectDetectionResult.Valid("FL Studio", "2024.1", 1.0));
        return blob;
    }

    [Fact]
    public async Task Progress_EmptyStagesHaveZeroTotals_AndLegacyResultsArePreserved()
    {
        _objectStore.EnumerateStoredObjects().Returns(Array.Empty<StoredObjectEntry>());
        _context.GetBranches().Returns(Array.Empty<BranchInfo>());
        _context.GetCurrentBranch().Returns(BranchName.Main);
        var useCase = new FsckUseCase(_ => _context, _adapterRegistry);
        var request = new FsckRequest("C:\\TestRepo", CheckArtifacts: true);
        var progress = new DiagnosticProgress();

        var report = await useCase.ExecuteAsync(request, default, progress);
        var legacy = await useCase.ExecuteAsync(request);

        report.Should().BeEquivalentTo(legacy);
        progress.Reports.Should().ContainSingle(p => p.Stage == OperationStage.VerifyingObjects && p.Completed == 0 && p.Total == 0);
        progress.Reports.Should().ContainSingle(p => p.Stage == OperationStage.InspectingArtifacts && p.Completed == 0 && p.Total == 0);
        progress.Reports.Where(p => p.Stage == OperationStage.TraversingHistory).Should().OnlyContain(p => p.Total == null);
        progress.Reports.Should().ContainSingle(p => p.Stage == OperationStage.Completed);
        progress.Reports.Last().Stage.Should().Be(OperationStage.Completed);
        progress.Reports.Should().OnlyContain(p => p.Unit == OperationProgressUnit.Items);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Progress_KnownCountsIncludeArtifactsWithoutAdapters(bool hasAdapter)
    {
        PrepareReachableArtifact();
        var progress = new DiagnosticProgress();
        var report = await new FsckUseCase(_ => _context, hasAdapter ? _adapterRegistry : null)
            .ExecuteAsync(new FsckRequest("C:\\TestRepo", CheckArtifacts: true), default, progress);

        progress.Reports.Where(p => p.Stage == OperationStage.VerifyingObjects)
            .Select(p => (p.Completed, p.Total)).Should().Equal((0L, (long?)1), (1L, (long?)1));
        progress.Reports.Where(p => p.Stage == OperationStage.InspectingArtifacts)
            .Select(p => (p.Completed, p.Total)).Should().Equal((0L, (long?)1), (1L, (long?)1));
        progress.Reports.Where(p => p.Stage == OperationStage.TraversingHistory).Should().OnlyContain(p => p.Total == null);
        progress.Reports.Last(p => p.Stage == OperationStage.TraversingHistory).Completed.Should().Be(1);
        progress.Reports.Should().ContainSingle(p => p.Stage == OperationStage.Completed);
        report.ArtifactIntegrity!.ArtifactsScanned.Should().Be(1);
    }

    [Theory]
    [InlineData(OperationStage.VerifyingObjects)]
    [InlineData(OperationStage.InspectingArtifacts)]
    [InlineData(OperationStage.TraversingHistory)]
    public async Task Progress_ObserverExceptionsEscapeWithoutCompletion(OperationStage stage)
    {
        PrepareReachableArtifact();
        var failure = new InvalidOperationException("Observer failed");
        var progress = new DiagnosticProgress(p =>
        {
            if (p.Stage == stage && p.Completed == 1) throw failure;
        });
        var action = () => new FsckUseCase(_ => _context, _adapterRegistry)
            .ExecuteAsync(new FsckRequest("C:\\TestRepo", CheckArtifacts: true), default, progress);

        (await action.Should().ThrowAsync<InvalidOperationException>()).Which.Should().BeSameAs(failure);
        progress.Reports.Should().NotContain(p => p.Stage == OperationStage.Completed);
    }

    [Fact]
    public async Task Progress_PreCanceledCaller_DoesNotOpenContextOrComplete()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var opened = false;
        var progress = new DiagnosticProgress();
        var action = () => new FsckUseCase(_ => { opened = true; return _context; })
            .ExecuteAsync(new FsckRequest("C:\\TestRepo"), cancellation.Token, progress);

        await action.Should().ThrowAsync<OperationCanceledException>();
        opened.Should().BeFalse();
        progress.Reports.Should().BeEmpty();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Progress_CancellationInDiagnosticCatches_IsNotConvertedToFindings(bool duringArtifactInspection)
    {
        var blob = PrepareReachableArtifact();
        if (duringArtifactInspection)
            _mockAdapter.DetectAsync(Arg.Any<Stream>(), Arg.Any<CancellationToken>()).ThrowsAsync(new OperationCanceledException());
        else
            _objectStore.VerifyObjectIntegrityAsync(blob, Arg.Any<CancellationToken>()).ThrowsAsync(new OperationCanceledException());
        var progress = new DiagnosticProgress();
        var action = () => new FsckUseCase(_ => _context, _adapterRegistry)
            .ExecuteAsync(new FsckRequest("C:\\TestRepo", CheckArtifacts: true), default, progress);

        await action.Should().ThrowAsync<OperationCanceledException>();
        progress.Reports.Should().NotContain(p => p.Stage == OperationStage.Completed);
    }

    [Fact]
    public async Task Progress_CallerCanceledDuringOperation_DoesNotComplete()
    {
        PrepareReachableArtifact();
        using var cancellation = new CancellationTokenSource();
        var progress = new DiagnosticProgress(p =>
        {
            if (p.Stage == OperationStage.InspectingArtifacts && p.Completed == 1) cancellation.Cancel();
        });
        var action = () => new FsckUseCase(_ => _context, _adapterRegistry)
            .ExecuteAsync(new FsckRequest("C:\\TestRepo", CheckArtifacts: true), cancellation.Token, progress);

        await action.Should().ThrowAsync<OperationCanceledException>();
        progress.Reports.Should().NotContain(p => p.Stage == OperationStage.Completed);
    }

    [Fact]
    public async Task Progress_InvalidNamesCountAsProcessedEntries()
    {
        _objectStore.EnumerateStoredObjects().Returns([new StoredObjectEntry("invalid", null, false)]);
        _context.GetBranches().Returns(Array.Empty<BranchInfo>());
        _context.GetCurrentBranch().Returns(BranchName.Main);
        var progress = new DiagnosticProgress();

        var report = await new FsckUseCase(_ => _context)
            .ExecuteAsync(new FsckRequest("C:\\TestRepo"), default, progress);

        report.RepositoryIntegrity.Errors.Should().ContainSingle(e => e.Code == "InvalidObjectFileName");
        progress.Reports.Where(p => p.Stage == OperationStage.VerifyingObjects)
            .Select(p => (p.Completed, p.Total)).Should().Equal((0L, (long?)1), (1L, (long?)1));
        progress.Reports.Should().ContainSingle(p => p.Stage == OperationStage.Completed);
        progress.Reports.Should().NotContain(p => p.Stage == OperationStage.InspectingArtifacts);
    }

    [Fact]
    public async Task Progress_AdapterFailureBecomesFinding_AndCompletes()
    {
        PrepareReachableArtifact();
        _mockAdapter.DetectAsync(Arg.Any<Stream>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new IOException("Inspection failed"));
        var progress = new DiagnosticProgress();

        var report = await new FsckUseCase(_ => _context, _adapterRegistry)
            .ExecuteAsync(new FsckRequest("C:\\TestRepo", CheckArtifacts: true), default, progress);

        report.ArtifactIntegrity!.Errors.Should().ContainSingle(e => e.Code == "ArtifactInspectionException");
        progress.Reports.Should().ContainSingle(p => p.Stage == OperationStage.Completed);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Progress_TaskFailure_DoesNotComplete(bool duringDisposal)
    {
        PrepareReachableArtifact();
        if (duringDisposal) _context.When(c => c.Dispose()).Do(_ => throw new IOException("Dispose failed"));
        else _context.GetBranches().Returns(_ => throw new IOException("Refs failed"));
        var progress = new DiagnosticProgress();
        var action = () => new FsckUseCase(_ => _context, _adapterRegistry)
            .ExecuteAsync(new FsckRequest("C:\\TestRepo"), default, progress);

        await action.Should().ThrowAsync<IOException>();
        progress.Reports.Should().NotContain(p => p.Stage == OperationStage.Completed);
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
        var progress = new DiagnosticProgress();
        var report = await useCase.ExecuteAsync(new FsckRequest("C:\\TestRepo"), default, progress);

        progress.Reports.Should().ContainSingle(p => p.Stage == OperationStage.Completed);
        progress.Reports.Last(p => p.Stage == OperationStage.VerifyingObjects).Completed.Should().Be(1);
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
