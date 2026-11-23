using DawVcs.Adapters.Abstractions;
using DawVcs.Application.Adapters;
using DawVcs.Application.Branches;
using DawVcs.Application.Checkouts;
using DawVcs.Application.Commits;
using DawVcs.Application.Common;
using DawVcs.Application.Dependencies;
using DawVcs.Application.Diagnostics;
using DawVcs.Application.Remotes;
using DawVcs.Application.Repositories;
using DawVcs.Application.Scanning;
using DawVcs.Application.Staging;
using DawVcs.Domain.Artifacts;
using DawVcs.Domain.Checkouts;
using DawVcs.Domain.Common;
using DawVcs.Domain.Configuration;
using DawVcs.Domain.Dependencies;
using DawVcs.Domain.Entities;
using DawVcs.Domain.Hashing;
using DawVcs.Domain.Remotes;
using DawVcs.Domain.Repositories;
using DawVcs.Domain.Storage;

using FluentAssertions;

using NSubstitute;

using Xunit;

namespace DawVcs.Application.Tests.Common;

public sealed class OperationCancellationTests : IDisposable
{
    private readonly string _testRoot = Path.Combine(Path.GetTempPath(), "dawvc_cancel_tests_" + Guid.NewGuid().ToString("N"));

    public OperationCancellationTests()
    {
        Directory.CreateDirectory(_testRoot);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_testRoot))
            {
                Directory.Delete(_testRoot, recursive: true);
            }
        }
        catch { }
    }

    private static CancellationToken GetCancelledToken()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        return cts.Token;
    }

    [Fact]
    public async Task Checkout_PreCancelled_DoesNotConstructContextAndThrows()
    {
        var contextFactoryCalled = false;
        var useCase = new CheckoutUseCase((Func<string, IRepositoryContext>)(_ =>
        {
            contextFactoryCalled = true;
            return Substitute.For<IRepositoryContext>();
        }));

        var act = () => useCase.ExecuteAsync(new CheckoutRequest(_testRoot, "main"), GetCancelledToken());
        await act.Should().ThrowAsync<OperationCanceledException>();
        contextFactoryCalled.Should().BeFalse();
    }

    [Fact]
    public async Task FastForwardCheckout_PreCancelled_DoesNotConstructContextAndThrows()
    {
        var contextFactoryCalled = false;
        var useCase = new CheckoutUseCase((Func<string, IRepositoryContext>)(_ =>
        {
            contextFactoryCalled = true;
            return Substitute.For<IRepositoryContext>();
        }));

        var commitId = CommitId.Parse(new string('1', 64));
        var target = new FastForwardTarget(_testRoot, BranchName.Main, null, commitId);
        var act = () => useCase.ExecuteFastForwardAsync(target, force: false, GetCancelledToken());
        await act.Should().ThrowAsync<OperationCanceledException>();
        contextFactoryCalled.Should().BeFalse();
    }

    [Fact]
    public async Task Add_PreCancelled_DoesNotConstructContextAndThrows()
    {
        var contextFactoryCalled = false;
        var useCase = new AddUseCase(_ =>
        {
            contextFactoryCalled = true;
            return Substitute.For<IRepositoryContext>();
        });

        var act = () => useCase.ExecuteAsync(new AddRequest(_testRoot, All: true), GetCancelledToken());
        await act.Should().ThrowAsync<OperationCanceledException>();
        contextFactoryCalled.Should().BeFalse();
    }

    [Fact]
    public async Task Log_PreCancelled_DoesNotConstructContextAndThrows()
    {
        var contextFactoryCalled = false;
        var useCase = new LogUseCase(_ =>
        {
            contextFactoryCalled = true;
            return Substitute.For<IRepositoryContext>();
        });

        var act = () => useCase.ExecuteAsync(new LogRequest(_testRoot), GetCancelledToken());
        await act.Should().ThrowAsync<OperationCanceledException>();
        contextFactoryCalled.Should().BeFalse();
    }

    [Fact]
    public async Task BindDependency_PreCancelled_DoesNotConstructContextAndThrows()
    {
        var contextFactoryCalled = false;
        var useCase = new BindDependencyUseCase(_ =>
        {
            contextFactoryCalled = true;
            return Substitute.For<IRepositoryContext>();
        });

        var act = () => useCase.ExecuteAsync(new BindDependencyRequest(_testRoot, "dep1", "path1"), GetCancelledToken());
        await act.Should().ThrowAsync<OperationCanceledException>();
        contextFactoryCalled.Should().BeFalse();
    }

    [Fact]
    public async Task Switch_PreCancelled_EvenIfAlreadyOnTargetBranch_Throws()
    {
        var commitId = CommitId.Parse(new string('1', 64));
        var context = Substitute.For<IRepositoryContext>();
        context.GetCurrentBranch().Returns(BranchName.Main);
        context.GetBranchCommit(BranchName.Main).Returns(commitId);

        var useCase = new SwitchUseCase(_ => context);

        var act = () => useCase.ExecuteAsync(new SwitchRequest(_testRoot, "main"), GetCancelledToken());
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task InitRepository_PreCancelled_DoesNotCreateDirectoriesOrTouchContext()
    {
        var contextFactoryCalled = false;
        var subDir = Path.Combine(_testRoot, "uncreated_init_repo");

        var useCase = new InitRepositoryUseCase(_ =>
        {
            contextFactoryCalled = true;
            return Substitute.For<IRepositoryContext>();
        });

        var act = () => useCase.ExecuteAsync(new InitRequest(subDir), GetCancelledToken());
        await act.Should().ThrowAsync<OperationCanceledException>();
        contextFactoryCalled.Should().BeFalse();
        Directory.Exists(subDir).Should().BeFalse();
    }

    [Fact]
    public async Task RemoteUseCase_AsyncMethods_PreCancelled_ThrowOperationCanceledException()
    {
        var useCase = new RemoteUseCase(_ => Substitute.For<IRepositoryContext>());
        var token = GetCancelledToken();

        await Assert.ThrowsAsync<OperationCanceledException>(() => useCase.ListRemotesAsync(new RemoteListRequest(_testRoot), token));
        await Assert.ThrowsAsync<OperationCanceledException>(() => useCase.AddRemoteAsync(new RemoteAddRequest(_testRoot, "origin", "C:/remote"), token));
        await Assert.ThrowsAsync<OperationCanceledException>(() => useCase.RemoveRemoteAsync(new RemoteRemoveRequest(_testRoot, "origin"), token));
    }

    [Fact]
    public async Task Clone_WhenDoctorStepCancelled_PreservesAndRethrowsOperationCanceledException()
    {
        var repoId = RepositoryId.New();
        var transport = Substitute.For<IRemoteTransport>();
        transport.RemoteUrl.Returns("C:/source_remote");
        transport.HandshakeAsync(Arg.Any<CancellationToken>())
            .Returns(new RemoteHandshake(1, repoId, "v0.1.0"));

        var transportFactory = Substitute.For<IRemoteTransportFactory>();
        transportFactory.CreateTransport("C:/source_remote").Returns(transport);

        var doctorUseCase = Substitute.For<DoctorUseCase>(
            (Func<string, IRepositoryContext>)(_ => Substitute.For<IRepositoryContext>()),
            (IDawAdapterRegistry?)null);
        doctorUseCase.ExecuteAsync(Arg.Any<DoctorRequest>(), Arg.Any<CancellationToken>())
            .Returns<Task<DoctorReport>>(_ => throw new OperationCanceledException());

        var fetchUseCase = Substitute.For<FetchUseCase>(
            (Func<string, IRepositoryContext>)(_ => Substitute.For<IRepositoryContext>()),
            transportFactory,
            (Func<string, IQuarantineStore>)(_ => Substitute.For<IQuarantineStore>()),
            (ObjectGraphWalker?)null);
        fetchUseCase.ExecuteAsync(Arg.Any<FetchRequest>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new FetchResult("origin", Array.Empty<UpdatedRemoteRef>(), 0, 0, false)));
        fetchUseCase.ExecuteAsync(Arg.Any<FetchRequest>(), Arg.Any<CancellationToken>(), Arg.Any<IProgress<OperationProgress>?>())
            .Returns(Task.FromResult(new FetchResult("origin", Array.Empty<UpdatedRemoteRef>(), 0, 0, false)));

        var checkoutUseCase = Substitute.For<CheckoutUseCase>((Func<string, IRepositoryContext>)(_ => Substitute.For<IRepositoryContext>()), (IDawAdapterRegistry?)null);
        checkoutUseCase.ExecuteFastForwardAsync(Arg.Any<FastForwardTarget>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new CheckoutResult(CommitId.Parse(new string('1', 64)), SnapshotId.Parse(new string('a', 64)), _testRoot, Array.Empty<string>())));
        checkoutUseCase.ExecuteFastForwardAsync(Arg.Any<FastForwardTarget>(), Arg.Any<bool>(), Arg.Any<CancellationToken>(), Arg.Any<IProgress<OperationProgress>?>())
            .Returns(Task.FromResult(new CheckoutResult(CommitId.Parse(new string('1', 64)), SnapshotId.Parse(new string('a', 64)), _testRoot, Array.Empty<string>())));

        var remoteConfig = new RepositoryConfig(repoId, "Project", new ArtifactPath("Main.flp"), BranchName.Main);
        var mockContext = Substitute.For<IRepositoryContext>();
        mockContext.LoadConfig().Returns(remoteConfig);
        mockContext.GetRemoteTrackingCommit("origin", BranchName.Main).Returns(CommitId.Parse(new string('1', 64)));

        var useCase = new CloneUseCase(
            _ => mockContext,
            transportFactory,
            fetchUseCase,
            checkoutUseCase,
            doctorUseCase);

        var act = () => useCase.ExecuteAsync(new CloneRequest("C:/source_remote", _testRoot), CancellationToken.None);
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task Pull_WhenDoctorStepCancelled_PreservesAndRethrowsOperationCanceledException()
    {
        var localCommit = CommitId.Parse(new string('1', 64));
        var remoteCommit = CommitId.Parse(new string('2', 64));

        var context = Substitute.For<IRepositoryContext>();
        context.GetCurrentBranch().Returns(BranchName.Main);
        context.GetBranchCommit(BranchName.Main).Returns(localCommit);
        context.GetRemoteTrackingCommit("origin", BranchName.Main).Returns(remoteCommit);

        var transportFactory = Substitute.For<IRemoteTransportFactory>();
        var fetchUseCase = Substitute.For<FetchUseCase>(
            (Func<string, IRepositoryContext>)(_ => context),
            transportFactory,
            (Func<string, IQuarantineStore>)(_ => Substitute.For<IQuarantineStore>()),
            (ObjectGraphWalker?)null);
        fetchUseCase.ExecuteAsync(Arg.Any<FetchRequest>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new FetchResult("origin", Array.Empty<UpdatedRemoteRef>(), 0, 0, false)));
        fetchUseCase.ExecuteAsync(Arg.Any<FetchRequest>(), Arg.Any<CancellationToken>(), Arg.Any<IProgress<OperationProgress>?>())
            .Returns(Task.FromResult(new FetchResult("origin", Array.Empty<UpdatedRemoteRef>(), 0, 0, false)));

        var ancestryService = Substitute.For<AncestryService>();
        ancestryService.DetermineSyncStateAsync(Arg.Any<CommitId?>(), Arg.Any<CommitId>(), Arg.Any<IRepositoryContext>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new SyncAncestryClassification(SyncAncestryState.FastForward, 0, 1)));

        var checkoutUseCase = Substitute.For<CheckoutUseCase>((Func<string, IRepositoryContext>)(_ => context), (IDawAdapterRegistry?)null);
        var dummySnapshotId = SnapshotId.Parse(new string('a', 64));
        checkoutUseCase.ExecuteFastForwardAsync(Arg.Any<FastForwardTarget>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new CheckoutResult(remoteCommit, dummySnapshotId, _testRoot, Array.Empty<string>())));
        checkoutUseCase.ExecuteFastForwardAsync(Arg.Any<FastForwardTarget>(), Arg.Any<bool>(), Arg.Any<CancellationToken>(), Arg.Any<IProgress<OperationProgress>?>())
            .Returns(Task.FromResult(new CheckoutResult(remoteCommit, dummySnapshotId, _testRoot, Array.Empty<string>())));

        var doctorUseCase = Substitute.For<DoctorUseCase>(
            (Func<string, IRepositoryContext>)(_ => context),
            (IDawAdapterRegistry?)null);
        doctorUseCase.ExecuteAsync(Arg.Any<DoctorRequest>(), Arg.Any<CancellationToken>())
            .Returns<Task<DoctorReport>>(_ => throw new OperationCanceledException());

        var pullUseCase = new PullUseCase(
            _ => context,
            fetchUseCase,
            checkoutUseCase,
            ancestryService,
            doctorUseCase);

        var act = () => pullUseCase.ExecuteAsync(new PullRequest(_testRoot), CancellationToken.None);
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task DependencyDiscoveryService_WhenCancelledDuringOccurrences_ThrowsOperationCanceledException()
    {
        var json = "[{\"Name\": \"Serum\", \"Vendor\": \"Xfer\", \"Role\": \"Instrument\"}, {\"Name\": \"Pro-Q 3\", \"Vendor\": \"FabFilter\"}]";
        var detection = ProjectDetectionResult.Valid("FL Studio", "2026", metadata: new Dictionary<string, string>
        {
            ["PluginOccurrences"] = json
        });

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = () => DependencyDiscoveryService.DiscoverAsync(_testRoot, detection, adapter: null, cancellationToken: cts.Token);
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task DependencyResolverPipeline_SearchByHashAsync_WhenCancelled_ThrowsOperationCanceledException()
    {
        var sampleFile = Path.Combine(_testRoot, "TestSample.wav");
        await File.WriteAllTextAsync(sampleFile, "Dummy audio content for search by hash test");
        var hash = await Blake3ContentHasher.HashFileAsync(sampleFile);

        var assetDep = new AssetDependency(
            DependencyId.ForAsset(hash),
            "TestSample.wav",
            DependencyRequirement.Required,
            DependencySource.NativeProjectParser,
            PortabilityPolicy.BundleDefault,
            relativePath: null,
            originalPath: null,
            hash: hash,
            fileSize: 100);

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = () => DependencyResolverPipeline.ResolveAsync(_testRoot, assetDep, localBindingStore: null, cancellationToken: cts.Token);
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task Commit_WhenCancelledDuringStagedLoop_ThrowsOperationCanceledException()
    {
        var context = Substitute.For<IRepositoryContext>();
        context.RootPath.Returns(_testRoot);
        context.GetCurrentBranch().Returns(BranchName.Main);
        context.GetBranchCommit(BranchName.Main).Returns((CommitId?)null);

        var primaryFile = Path.Combine(_testRoot, "Project.flp");
        await File.WriteAllTextAsync(primaryFile, "Dummy project content");

        var config = new RepositoryConfig(RepositoryId.New(), "Project", new ArtifactPath("Project.flp"), BranchName.Main);
        context.LoadConfig().Returns(config);

        var stagingIndex = Substitute.For<IStagingIndex>();
        var staged = new List<ArtifactEntry>();
        for (int i = 0; i < 5; i++)
        {
            var p = Path.Combine(_testRoot, $"Staged_{i}.wav");
            await File.WriteAllTextAsync(p, $"Content {i}");
            staged.Add(ArtifactEntry.Create(new ArtifactPath($"Staged_{i}.wav"), ContentHash.Parse(new string('a', 64)), 10));
        }

        using var cts = new CancellationTokenSource();
        stagingIndex.GetStagedEntriesAsync(Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                cts.Cancel();
                return Task.FromResult<IReadOnlyList<ArtifactEntry>>(staged);
            });
        context.StagingIndex.Returns(stagingIndex);

        var useCase = new CommitUseCase(_ => context);
        var act = () => useCase.ExecuteAsync(new CommitRequest(_testRoot, "Commit message", "Author"), cts.Token);
        await act.Should().ThrowAsync<OperationCanceledException>();
    }
}
