using System.Text.Json;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;

using DawVcs.Adapters.FLStudio;
using DawVcs.Adapters.FLStudio.Tests.Fixtures;
using DawVcs.Application.Adapters;
using DawVcs.Application.Commits;
using DawVcs.Application.Diagnostics;
using DawVcs.Application.Repositories;
using DawVcs.Application.Scanning;
using DawVcs.Application.Staging;
using DawVcs.Application.Viewing;
using DawVcs.Bootstrap;
using DawVcs.Desktop.Services;
using DawVcs.Desktop.ViewModels;
using DawVcs.Desktop.Views;
using DawVcs.Domain.Artifacts;
using DawVcs.Domain.Common;
using DawVcs.Domain.Configuration;
using DawVcs.Domain.Hashing;
using DawVcs.Domain.Repositories;

using FluentAssertions;

using Microsoft.Extensions.DependencyInjection;

using NSubstitute;

using Xunit;

namespace DawVcs.Desktop.Tests;

public sealed class ViewerTests
{
    private static EngineScopeFactory Factory() => new(services => services.AddSingleton<IDawAdapterRegistry>(
        _ => new DawAdapterRegistry([new FLStudioAdapter([])])));

    private static async Task<string> SeedAsync(TestRepositories repositories)
    {
        var root = await repositories.CreateAsync("viewer", TestContext.Current.CancellationToken);
        await File.WriteAllBytesAsync(Path.Combine(root, "Project.flp"), FlpFixtureGenerator.CreateValidFlp(), TestContext.Current.CancellationToken);
        using var scope = Factory().Create();
        await scope.Services.GetRequiredService<CommitUseCase>().ExecuteAsync(new CommitRequest(root, "Initial", "Synthetic author"), TestContext.Current.CancellationToken);
        await File.WriteAllBytesAsync(Path.Combine(root, "Untracked.wav"), [1, 2, 3], TestContext.Current.CancellationToken);
        return root;
    }

    [AvaloniaFact]
    public async Task Viewer_UsesRealStatusHistoryDetailsAndDiagnostics_WithoutChangingUserState()
    {
        using var repositories = new TestRepositories();
        var root = await SeedAsync(repositories);
        var protectedFiles = Directory.GetFiles(root, "*", SearchOption.AllDirectories)
            .Where(path => !path.Contains(Path.DirectorySeparatorChar + ".dawvc" + Path.DirectorySeparatorChar, StringComparison.Ordinal) || path.Contains(Path.DirectorySeparatorChar + "refs" + Path.DirectorySeparatorChar, StringComparison.Ordinal) || Path.GetFileName(path) == "HEAD")
            .ToDictionary(path => path, File.ReadAllBytes);
        using var expected = Factory().Create();
        var status = await expected.Services.GetRequiredService<StatusUseCase>().ExecuteAsync(new StatusRequest(root), TestContext.Current.CancellationToken);
        var log = await expected.Services.GetRequiredService<LogUseCase>().ExecuteAsync(new LogRequest(root, 50), TestContext.Current.CancellationToken);
        var doctor = await expected.Services.GetRequiredService<DoctorUseCase>().ExecuteAsync(new DoctorRequest(root), TestContext.Current.CancellationToken);
        var fsck = await expected.Services.GetRequiredService<FsckUseCase>().ExecuteAsync(new FsckRequest(root), TestContext.Current.CancellationToken);
        await using var coordinator = new OperationCoordinator(Factory(), new UiDispatcher());
        await coordinator.OpenAsync(root);
        await using var viewer = new ViewerViewModel(coordinator);
        viewer.Page = DesktopPage.Overview;
        await viewer.LoadCurrentPageAsync();
        viewer.MetadataState.State.Should().Be(ViewLoadState.Success);
        viewer.ChangesState.State.Should().Be(ViewLoadState.Success);
        viewer.Changes.Select(row => row.Path).Should().BeEquivalentTo(status.Status.Untracked.Select(item => item.Path.Value)
            .Concat(status.Status.Modified.Select(item => item.Path.Value)).Concat(status.Status.Staged.Select(item => item.Path.Value)));
        viewer.ChangeSearch = "Untracked";
        viewer.Changes.Should().ContainSingle();
        await viewer.HistoryNewestCommand.ExecuteAsync(null);
        viewer.History.Should().BeEquivalentTo(log.Entries);
        viewer.SelectedCommit = viewer.History[0];
        await viewer.ShowCommitCommand.ExecuteAsync(null);
        viewer.Files.Should().Contain(file => file.Path == "Project.flp");
        await viewer.DependenciesRefreshCommand.ExecuteAsync(null);
        await viewer.ScanCommand.ExecuteAsync(null);
        viewer.ScanState.State.Should().Be(ViewLoadState.Success);
        await viewer.DoctorCommand.ExecuteAsync(null);
        viewer.DoctorSummary.Should().Contain($"{doctor.BlockingIssuesCount} blocking");
        await viewer.FsckCommand.ExecuteAsync(null);
        viewer.FsckSummary.Should().Contain($"{fsck.RepositoryIntegrity.TotalObjectsScanned} objects");
        foreach (var file in protectedFiles) (await File.ReadAllBytesAsync(file.Key, TestContext.Current.CancellationToken)).Should().Equal(file.Value);
        // Explicitly staged user entries must survive all viewer reads.
        using var scope = Factory().Create();
        await scope.Services.GetRequiredService<AddUseCase>().ExecuteAsync(new AddRequest(root, ["Untracked.wav"]), TestContext.Current.CancellationToken);
        var staged = (await scope.Services.GetRequiredService<StatusUseCase>().ExecuteAsync(new StatusRequest(root), TestContext.Current.CancellationToken)).Status.Staged;
        await viewer.RefreshCommand.ExecuteAsync(null);
        (await scope.Services.GetRequiredService<StatusUseCase>().ExecuteAsync(new StatusRequest(root), TestContext.Current.CancellationToken)).Status.Staged.Should().BeEquivalentTo(staged);
    }

    [AvaloniaFact]
    public async Task CorruptIndex_PreservesBytes_ShowsPartialDashboardError_AndAllowsDiagnostics()
    {
        using var repositories = new TestRepositories();
        var root = await SeedAsync(repositories);
        using (var scope = Factory().Create())
            await scope.Services.GetRequiredService<AddUseCase>().ExecuteAsync(new AddRequest(root, ["Untracked.wav"]), TestContext.Current.CancellationToken);
        var database = Path.Combine(root, ".dawvc", "index.db");
        var before = await File.ReadAllBytesAsync(database, TestContext.Current.CancellationToken);
        before[0] = 0;
        await File.WriteAllBytesAsync(database, before, TestContext.Current.CancellationToken);
        await using var coordinator = new OperationCoordinator(Factory(), new UiDispatcher());
        await coordinator.OpenAsync(root);
        await using var viewer = new ViewerViewModel(coordinator);
        viewer.Page = DesktopPage.Overview;
        await viewer.LoadCurrentPageAsync();
        viewer.MetadataState.State.Should().Be(ViewLoadState.Success);
        viewer.ChangesState.State.Should().Be(ViewLoadState.Error);
        viewer.ChangesState.Error.Should().Contain("Nothing was reset");
        await viewer.FsckCommand.ExecuteAsync(null);
        viewer.FsckState.State.Should().Be(ViewLoadState.Success);
        (await File.ReadAllBytesAsync(database, TestContext.Current.CancellationToken)).Should().Equal(before);
    }

    [AvaloniaFact]
    public async Task UnbornHistory_IsEmpty_AndRepositorySwitchClearsAllPreviousResults()
    {
        using var repositories = new TestRepositories();
        var first = await SeedAsync(repositories);
        var second = await repositories.CreateAsync("unborn", TestContext.Current.CancellationToken);
        await using var coordinator = new OperationCoordinator(Factory(), new UiDispatcher());
        await coordinator.OpenAsync(first);
        await using var viewer = new ViewerViewModel(coordinator);
        await viewer.HistoryNewestCommand.ExecuteAsync(null);
        viewer.History.Should().NotBeEmpty();
        await coordinator.OpenAsync(second);
        viewer.History.Should().BeEmpty();
        viewer.Head.Should().BeEmpty();
        await viewer.HistoryNewestCommand.ExecuteAsync(null);
        viewer.HistoryState.State.Should().Be(ViewLoadState.Empty);
    }

    [Fact]
    public void DiagnosticExport_ContainsOnlyAllowlistedSummary_AndRejectsRepositoryDestinations()
    {
        var report = new DiagnosticExport(DiagnosticKind.Doctor, false, new Dictionary<string, long> { ["warnings"] = 2, [@"C:\Users\secret"] = 99 });
        using var json = JsonDocument.Parse(report.ToJson());
        json.RootElement.GetProperty("Counts").GetProperty("warnings").GetInt64().Should().Be(2);
        report.ToJson().Should().NotContain("secret");
        var root = Path.Combine(Path.GetTempPath(), "repository");
        var blocked = () => DiagnosticExporter.ValidateDestination(root, Path.Combine(root, "report.json"));
        blocked.Should().Throw<ArgumentException>();
        DiagnosticExporter.ValidateDestination(root, Path.Combine(Path.GetTempPath(), "outside.json"));
    }
    private static EngineScopeFactory FakeIndexFactory(Func<CancellationToken, Task<IReadOnlyList<ArtifactEntry>>> staged) => new(services =>
        services.AddSingleton<Func<string, IRepositoryContext>>(_ => root =>
        {
            var context = Substitute.For<IRepositoryContext>();
            var index = Substitute.For<IStagingIndex>();
            context.RootPath.Returns(root);
            context.LoadConfig().Returns(new RepositoryConfig(RepositoryId.New(), "Viewer", new ArtifactPath("Project.flp")));
            context.GetCurrentBranch().Returns(BranchName.Main);
            context.StagingIndex.Returns(index);
            index.GetStagedEntriesAsync(Arg.Any<CancellationToken>()).Returns(call => staged(call.Arg<CancellationToken>()));
            return context;
        }));

    [AvaloniaFact]
    public async Task Cancellation_ShowsCancelledStateOnlyAfterBackendFinishes()
    {
        using var repositories = new TestRepositories();
        var root = await repositories.CreateAsync("cancel", TestContext.Current.CancellationToken);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task<IReadOnlyList<ArtifactEntry>> Wait(CancellationToken token)
        {
            entered.TrySetResult();
            await Task.Delay(Timeout.Infinite, token);
            return [];
        }
        await using var coordinator = new OperationCoordinator(FakeIndexFactory(Wait), new UiDispatcher());
        await coordinator.OpenAsync(root);
        await using var viewer = new ViewerViewModel(coordinator);
        var loading = viewer.RefreshCommand.ExecuteAsync(null);
        await entered.Task.WaitAsync(TestContext.Current.CancellationToken);
        viewer.ChangesState.State.Should().Be(ViewLoadState.Loading);
        coordinator.IsBusy.Should().BeTrue();
        coordinator.Cancel();
        await loading;
        viewer.ChangesState.State.Should().Be(ViewLoadState.Cancelled);
        coordinator.IsBusy.Should().BeFalse();
        viewer.RefreshCommand.CanExecute(null).Should().BeTrue();
    }

    [AvaloniaFact]
    public async Task ChangesControl_VirtualizesTenThousandRowsAndSupportsSelectionAndFiltering()
    {
        using var repositories = new TestRepositories();
        var root = await repositories.CreateAsync("large", TestContext.Current.CancellationToken);
        var rows = Enumerable.Range(0, 10_000).Select(i => ArtifactEntry.Create(new ArtifactPath($"Samples/Asset{i:D5}.wav"), new ContentHash(new byte[32]), 4)).ToArray();
        await using var coordinator = new OperationCoordinator(FakeIndexFactory(_ => Task.FromResult<IReadOnlyList<ArtifactEntry>>(rows)), new UiDispatcher());
        await coordinator.OpenAsync(root);
        await using var viewer = new ViewerViewModel(coordinator);
        await viewer.RefreshCommand.ExecuteAsync(null);
        viewer.ChangeSearch = "Asset";
        viewer.Page = DesktopPage.Changes;
        var view = new ViewerView { DataContext = viewer };
        var window = new Window { Width = 1000, Height = 650, Content = view };
        try
        {
            window.Show(); window.UpdateLayout();
            var list = view.FindControl<ListBox>("ChangesList")!;
            list.ItemCount.Should().Be(10_000);
            list.GetVisualDescendants().OfType<ListBoxItem>().Count().Should().BeInRange(1, 200);
            list.SelectedIndex = 0;
            viewer.SelectedChange.Should().NotBeNull();
            viewer.ChangeSearch = "Asset099";
            window.UpdateLayout();
            list.ItemCount.Should().Be(100);
        }
        finally { window.Close(); }
    }

    [Fact]
    public void Export_RejectsBrokenSymlinkIntoRepository()
    {
        if (OperatingSystem.IsWindows()) return; // Creating symlinks requires optional OS privileges on Windows.
        using var repositories = new TestRepositories();
        var repo = Path.Combine(repositories.Root, "repo");
        Directory.CreateDirectory(repo);
        var link = Path.Combine(repositories.Root, "outside.json");
        File.CreateSymbolicLink(link, Path.Combine(repo, "must-not-be-created.json"));
        var write = () => DiagnosticExporter.ValidateDestination(repo, link);
        write.Should().Throw<ArgumentException>();
        File.Exists(Path.Combine(repo, "must-not-be-created.json")).Should().BeFalse();
    }

}
