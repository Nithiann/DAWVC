using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Styling;
using Avalonia.Threading;

using DawVcs.Desktop.Services;
using DawVcs.Desktop.ViewModels;
using DawVcs.Desktop.Views;

using FluentAssertions;

using NSubstitute;

using Xunit;

[assembly: AvaloniaTestApplication(typeof(DawVcs.Desktop.Tests.TestApp))]

namespace DawVcs.Desktop.Tests;

public static class TestApp
{
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>().WithInterFont().UseHeadless(new AvaloniaHeadlessPlatformOptions());
}

public sealed class ShellTests
{
    [AvaloniaFact]
    public async Task Shell_NavigatesOnlyAvailableSessionsAndSupportsThemeAndClose()
    {
        using var repositories = new TestRepositories();
        var root = await repositories.CreateAsync("repo", TestContext.Current.CancellationToken);
        var picker = Substitute.For<IFolderPicker>();
        picker.PickAsync().Returns(root);
        var store = Substitute.For<IRecentRepositories>();
        store.LoadAsync(Arg.Any<CancellationToken>()).Returns(Array.Empty<RecentRepository>());
        var explorer = Substitute.For<IFileExplorer>();
        await using var model = new ShellViewModel(new OperationCoordinator(new EngineScopeFactory(), new UiDispatcher()), picker, explorer, store);
        var window = new MainWindow();
        window.Attach(model);
        window.Show();
        try
        {
            model.NavigateCommand.Execute(DesktopPage.Changes);
            model.CurrentPage.Should().Be(DesktopPage.Home);
            model.OpenExplorerCommand.CanExecute(null).Should().BeFalse();
            await model.OpenCommand.ExecuteAsync(null);
            model.CurrentPage.Should().Be(DesktopPage.Overview);
            model.Operations.RepositoryPath.Should().Be(root);
            model.RecentRepositories.Should().ContainSingle(r => r.Path == root);
            model.NavigateCommand.Execute(DesktopPage.History);
            model.CurrentPage.Should().Be(DesktopPage.History);
            model.ToggleThemeCommand.Execute(null);
            global::Avalonia.Application.Current!.RequestedThemeVariant.Should().Be(ThemeVariant.Light);
            model.OpenExplorerCommand.Execute(null);
            explorer.Received().OpenDirectory(root);
        }
        finally
        {
            window.Close();
            await model.DisposeAsync();
            Dispatcher.UIThread.RunJobs();
            window.IsVisible.Should().BeFalse();
        }
    }

    [AvaloniaFact]
    public async Task CancelledPicker_LeavesSessionAndRecentStorageUntouched()
    {
        var picker = Substitute.For<IFolderPicker>();
        picker.PickAsync().Returns((string?)null);
        var store = Substitute.For<IRecentRepositories>();
        await using var model = new ShellViewModel(new OperationCoordinator(new EngineScopeFactory(), new UiDispatcher()), picker, Substitute.For<IFileExplorer>(), store);
        await model.OpenCommand.ExecuteAsync(null);
        model.Operations.SessionId.Should().BeNull();
        await store.DidNotReceive().SaveAsync(Arg.Any<IReadOnlyList<RecentRepository>>(), Arg.Any<CancellationToken>());
    }
    [AvaloniaFact]
    public async Task ConfirmationDialog_EscapeReturnsFalse()
    {
        var owner = new Window();
        owner.Show();
        try
        {
            var dialogs = new DesktopPlatform(() => owner);
            var result = dialogs.ConfirmAsync("Confirm", "Continue?");
            var dialog = owner.OwnedWindows.Single();
            dialog.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Escape });
            (await result).Should().BeFalse();
        }
        finally { owner.Close(); }
    }

    [AvaloniaFact]
    public async Task ConcurrentOpenCommands_SerializePreferenceWritesAndKeepMostRecentOrder()
    {
        using var repositories = new TestRepositories();
        var first = await repositories.CreateAsync("one", TestContext.Current.CancellationToken);
        var second = await repositories.CreateAsync("two", TestContext.Current.CancellationToken);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var store = Substitute.For<IRecentRepositories>();
        int writes = 0;
        store.SaveAsync(Arg.Any<IReadOnlyList<RecentRepository>>(), Arg.Any<CancellationToken>()).Returns(async _ =>
        {
            if (++writes == 1) { entered.SetResult(); await release.Task; }
        });
        await using var model = new ShellViewModel(new OperationCoordinator(new EngineScopeFactory(), new UiDispatcher()), Substitute.For<IFolderPicker>(), Substitute.For<IFileExplorer>(), store);
        model.RepositoryInput = first;
        var openingFirst = model.OpenPathCommand.ExecuteAsync(null);
        await entered.Task.WaitAsync(TestContext.Current.CancellationToken);
        var openingSecond = model.OpenRecentCommand.ExecuteAsync(second);
        openingSecond.IsCompleted.Should().BeFalse();
        release.SetResult();
        await Task.WhenAll(openingFirst, openingSecond);
        model.Operations.RepositoryPath.Should().Be(second);
        model.RecentRepositories.Select(r => r.Path).Should().Equal(second, first);
        writes.Should().Be(2);
    }

}
