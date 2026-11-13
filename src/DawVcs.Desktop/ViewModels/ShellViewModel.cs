using System.Collections.ObjectModel;
using System.ComponentModel;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using DawVcs.Application.Common;
using DawVcs.Desktop.Services;

namespace DawVcs.Desktop.ViewModels;

public enum DesktopPage { Home, Overview, Changes, History, Dependencies, Diagnostics, Settings }

public sealed class ShellViewModel : ObservableObject, IAsyncDisposable
{
    private readonly IFolderPicker _picker;
    private readonly IFileExplorer _explorer;
    private readonly IRecentRepositories _recents;
    private readonly CancellationTokenSource _shutdown = new();
    private readonly SemaphoreSlim _openingGate = new(1, 1);
    private long _recentsRevision;
    private DesktopPage _page;
    private bool _dark = true;
    private string? _notice;
    private bool _closing;
    private readonly object _disposeSync = new();
    private Task? _disposal;
    private string _repositoryInput = "";

    public ShellViewModel(OperationCoordinator operations, IFolderPicker picker, IFileExplorer explorer, IRecentRepositories recents)
    {
        Operations = operations;
        _picker = picker;
        _explorer = explorer;
        _recents = recents;
        OpenCommand = new AsyncRelayCommand(PickAndOpenAsync, () => !_closing);
        OpenPathCommand = new AsyncRelayCommand(() => OpenAsync(RepositoryInput), () => !_closing && !string.IsNullOrWhiteSpace(RepositoryInput));
        OpenRecentCommand = new AsyncRelayCommand<string>(path => path is null ? Task.CompletedTask : OpenAsync(path), _ => !_closing);
        LoadRecentsCommand = new AsyncRelayCommand(LoadRecentsAsync);
        CancelCommand = new RelayCommand(Operations.Cancel, () => Operations.IsBusy && !Operations.IsCancelling);
        NavigateCommand = new RelayCommand<DesktopPage>(Navigate, page => !_closing && (page is DesktopPage.Home or DesktopPage.Settings || Operations.SessionId is not null));
        ToggleThemeCommand = new RelayCommand(() => IsDark = !IsDark);
        OpenExplorerCommand = new RelayCommand(OpenExplorer, () => Operations.RepositoryPath is not null && !_closing);
        Operations.PropertyChanged += OperationChanged;
    }

    public OperationCoordinator Operations { get; }
    public ObservableCollection<RecentRepository> RecentRepositories { get; } = [];
    public IAsyncRelayCommand OpenCommand { get; }
    public IAsyncRelayCommand OpenPathCommand { get; }
    public IAsyncRelayCommand<string> OpenRecentCommand { get; }
    public IAsyncRelayCommand LoadRecentsCommand { get; }
    public IRelayCommand CancelCommand { get; }
    public IRelayCommand<DesktopPage> NavigateCommand { get; }
    public IRelayCommand ToggleThemeCommand { get; }
    public IRelayCommand OpenExplorerCommand { get; }
    public DesktopPage CurrentPage { get => _page; private set { if (SetProperty(ref _page, value)) { OnPropertyChanged(nameof(PageTitle)); OnPropertyChanged(nameof(PageDescription)); } } }
    public bool IsDark { get => _dark; set => SetProperty(ref _dark, value); }
    public string? Notice { get => _notice; private set => SetProperty(ref _notice, value); }
    public string RepositoryInput { get => _repositoryInput; set { if (SetProperty(ref _repositoryInput, value)) OpenPathCommand.NotifyCanExecuteChanged(); } }
    public string PageTitle => CurrentPage == DesktopPage.Home ? "Your projects" : CurrentPage.ToString();
    public string PageDescription => CurrentPage switch
    {
        DesktopPage.Home => "Open an existing DAWVC repository to begin.",
        DesktopPage.Settings => "Choose the appearance that suits your workspace.",
        _ => "This view will be available in a future update."
    };
    public string? FailureMessage => Operations.Failure?.Category switch
    {
        null => null,
        OperationFailureCategory.Validation => "Choose a valid, initialized DAWVC repository.",
        OperationFailureCategory.Cancellation => "The operation has stopped. Changes already published may remain.",
        OperationFailureCategory.Timeout => "The operation timed out.",
        OperationFailureCategory.PermissionOrIoFailure => "The location could not be accessed. Check its availability and permissions.",
        OperationFailureCategory.IncompleteRollback => "Recovery is incomplete. Preserve the recovery location before continuing.",
        OperationFailureCategory.IncompatibleRepository => "This repository requires a compatible DAWVC version.",
        _ => "The operation could not finish."
    };
    public string ProgressText => Operations.IsCancelling ? "Stopping — waiting for the operation to finish…" :
        Operations.Progress is { } progress ? $"{progress.Stage} · {progress.Completed}/{progress.Total?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "?"}" :
        Operations.IsBusy ? "Opening workspace…" : "Ready";

    private async Task PickAndOpenAsync()
    {
        try { var path = await _picker.PickAsync(); if (path is not null) await OpenAsync(path); }
        catch (Exception ex) { Notice = OperationFailure.FromException(ex).Category.ToString(); }
    }

    private async Task OpenAsync(string path)
    {
        if (_closing) return;
        try { await _openingGate.WaitAsync(_shutdown.Token); }
        catch (OperationCanceledException) { return; }
        try { if (!_closing) await OpenCoreAsync(path); }
        finally { _openingGate.Release(); }
    }

    private async Task OpenCoreAsync(string path)
    {
        Notice = null;
        if (!await Operations.OpenAsync(path)) return;
        var session = Operations.SessionId;
        if (_closing || Operations.RepositoryPath is not { } root) return;
        ++_recentsRevision;
        RepositoryInput = root;
        CurrentPage = DesktopPage.Overview;
        var items = new[] { new RecentRepository(root) }.Concat(RecentRepositories)
            .DistinctBy(item => item.Path, OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal).Take(20).ToArray();
        try
        {
            await _recents.SaveAsync(items, _shutdown.Token);
            if (!_closing && Operations.SessionId == session) SetRecents(items);
        }
        catch (Exception ex) when (ex is not OperationCanceledException) { Notice = "Recent projects could not be saved."; }
        catch (OperationCanceledException) { }
    }

    private async Task LoadRecentsAsync()
    {
        var revision = _recentsRevision;
        try { var items = await _recents.LoadAsync(_shutdown.Token); if (!_closing && revision == _recentsRevision) SetRecents(items); }
        catch (Exception ex) when (ex is not OperationCanceledException) { Notice = "Recent projects could not be read."; }
        catch (OperationCanceledException) { }
    }

    private void SetRecents(IEnumerable<RecentRepository> items) { RecentRepositories.Clear(); foreach (var item in items) RecentRepositories.Add(item); }
    private void Navigate(DesktopPage page)
    {
        if (page is DesktopPage.Home or DesktopPage.Settings || Operations.RepositoryPath is not null) CurrentPage = page;
    }
    private void OpenExplorer()
    {
        if (Operations.RepositoryPath is not { } path) return;
        try { _explorer.OpenDirectory(path); }
        catch (Exception ex) { Notice = OperationFailure.FromException(ex).Category.ToString(); }
    }
    private void OperationChanged(object? sender, PropertyChangedEventArgs args)
    {
        CancelCommand.NotifyCanExecuteChanged(); OpenExplorerCommand.NotifyCanExecuteChanged(); NavigateCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(ProgressText)); OnPropertyChanged(nameof(FailureMessage));
        if (args.PropertyName == nameof(OperationCoordinator.SessionId) && Operations.SessionId is null) CurrentPage = DesktopPage.Home;
    }

    public ValueTask DisposeAsync()
    {
        lock (_disposeSync) return new ValueTask(_disposal ??= DisposeCoreAsync());
    }

    private async Task DisposeCoreAsync()
    {
        _closing = true;
        _shutdown.Cancel();
        OpenCommand.NotifyCanExecuteChanged(); OpenPathCommand.NotifyCanExecuteChanged(); OpenRecentCommand.NotifyCanExecuteChanged();
        await Operations.DisposeAsync();
        await Task.WhenAll(OpenCommand.ExecutionTask ?? Task.CompletedTask, OpenPathCommand.ExecutionTask ?? Task.CompletedTask,
            OpenRecentCommand.ExecutionTask ?? Task.CompletedTask, LoadRecentsCommand.ExecutionTask ?? Task.CompletedTask);
        Operations.PropertyChanged -= OperationChanged;
        _openingGate.Dispose();
        _shutdown.Dispose();
    }
}
