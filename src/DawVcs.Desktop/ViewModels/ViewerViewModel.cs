using System.ComponentModel;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using DawVcs.Application.Commits;
using DawVcs.Application.Common;
using DawVcs.Application.Diagnostics;
using DawVcs.Application.Scanning;
using DawVcs.Application.Viewing;
using DawVcs.Desktop.Services;
using DawVcs.Domain.Common;
using DawVcs.Domain.Repositories;

using Microsoft.Extensions.DependencyInjection;

namespace DawVcs.Desktop.ViewModels;

public enum ViewLoadState { Idle, Loading, Empty, Success, Error, Cancelled }

public sealed class ViewerSection : ObservableObject
{
    private ViewLoadState _state;
    private string? _error;
    public long Revision { get; private set; }
    public ViewLoadState State { get => _state; set { if (SetProperty(ref _state, value)) OnPropertyChanged(nameof(Caption)); } }
    public string Caption => State switch
    {
        ViewLoadState.Loading => "Loading…",
        ViewLoadState.Success => "Loaded",
        ViewLoadState.Empty => "No results",
        ViewLoadState.Error => "Could not load",
        ViewLoadState.Cancelled => "Stopped",
        _ => "Not loaded"
    };
    public string? Error { get => _error; set => SetProperty(ref _error, value); }
    public void Reset() { Revision++; State = ViewLoadState.Idle; Error = null; }
    public long Begin() { Revision++; Error = null; State = ViewLoadState.Loading; return Revision; }
}

public sealed record ChangeRow(string GroupHeader, string State, string Path, long Bytes, string? PreviousPath)
{
    public bool HasGroupHeader => GroupHeader.Length > 0;
}
public sealed record DiagnosticRow(string Category, string Subject, string Status);

public sealed class ViewerViewModel : ObservableObject, IAsyncDisposable
{
    private readonly OperationCoordinator _operations;
    private readonly IDiagnosticExporter? _exporter;
    private readonly CancellationTokenSource _shutdown = new();
    private readonly IAsyncRelayCommand[] _commands;
    private readonly ViewerSection[] _sections;
    private readonly List<Task> _pageLoads = [];
    private DesktopPage _page;
    private string _project = "";
    private string _branch = "";
    private string _head = "";
    private string _counts = "";
    private WorkingTreeItem[] _changes = [];
    private IReadOnlyList<ChangeRow> _filteredChanges = [];
    private string _changeSearch = "";
    private ChangeRow? _selectedChange;
    private IReadOnlyList<CommitLogEntry> _history = [];
    private CommitLogEntry? _selectedCommit;
    private CommitId? _historyNext;
    private CommitDetails? _details;
    private int? _filesNext;
    private IReadOnlyList<SnapshotFile> _files = [];
    private DependencyPage? _dependencyPage;
    private IReadOnlyList<SnapshotDependency> _dependencies = [];
    private string _dependencySearch = "";
    private string _dependencyKind = "All";
    private IReadOnlyList<DiagnosticRow> _doctorRows = [];
    private IReadOnlyList<DiagnosticRow> _fsckRows = [];
    private string _scanSummary = "";
    private string _doctorSummary = "";
    private string _fsckSummary = "";
    private DiagnosticExport? _report;
    private string? _exportNotice;
    private bool _disposed;

    public ViewerViewModel(OperationCoordinator operations, IDiagnosticExporter? exporter = null)
    {
        _operations = operations;
        _exporter = exporter;
        _sections = [MetadataState, ChangesState, HistoryState, DetailsState, DependenciesState, ScanState, DoctorState, FsckState];
        foreach (var section in _sections) section.PropertyChanged += StateChanged;
        RefreshCommand = new AsyncRelayCommand(RefreshAsync, CanRun);
        HistoryNewestCommand = new AsyncRelayCommand(() => LoadHistoryAsync(null), CanRun);
        HistoryOlderCommand = new AsyncRelayCommand(() => LoadHistoryAsync(_historyNext), () => CanRun() && _historyNext.HasValue);
        ShowCommitCommand = new AsyncRelayCommand(() => LoadDetailsAsync(0), () => CanRun() && SelectedCommit is not null);
        MoreFilesCommand = new AsyncRelayCommand(() => LoadDetailsAsync(_filesNext ?? 0), () => CanRun() && _filesNext.HasValue);
        DependenciesRefreshCommand = new AsyncRelayCommand(() => LoadDependenciesAsync(null, 0), CanRun);
        DependenciesNextCommand = new AsyncRelayCommand(() => LoadDependenciesAsync(_dependencyPage?.Commit, _dependencyPage?.NextOffset ?? 0), () => CanRun() && _dependencyPage?.NextOffset is not null);
        ScanCommand = new AsyncRelayCommand(ScanAsync, CanRun);
        DoctorCommand = new AsyncRelayCommand(DoctorAsync, CanRun);
        FsckCommand = new AsyncRelayCommand(FsckAsync, CanRun);
        ExportCommand = new AsyncRelayCommand(ExportAsync, () => !_disposed && _report is not null && _exporter is not null && _operations.RepositoryPath is not null);
        _commands = [RefreshCommand, HistoryNewestCommand, HistoryOlderCommand, ShowCommitCommand, MoreFilesCommand,
            DependenciesRefreshCommand, DependenciesNextCommand, ScanCommand, DoctorCommand, FsckCommand, ExportCommand];
        _operations.PropertyChanged += OperationsChanged;
    }

    public ViewerSection MetadataState { get; } = new();
    public ViewerSection ChangesState { get; } = new();
    public ViewerSection HistoryState { get; } = new();
    public ViewerSection DetailsState { get; } = new();
    public ViewerSection DependenciesState { get; } = new();
    public ViewerSection ScanState { get; } = new();
    public ViewerSection DoctorState { get; } = new();
    public ViewerSection FsckState { get; } = new();
    public IAsyncRelayCommand RefreshCommand { get; }
    public IAsyncRelayCommand HistoryNewestCommand { get; }
    public IAsyncRelayCommand HistoryOlderCommand { get; }
    public IAsyncRelayCommand ShowCommitCommand { get; }
    public IAsyncRelayCommand MoreFilesCommand { get; }
    public IAsyncRelayCommand DependenciesRefreshCommand { get; }
    public IAsyncRelayCommand DependenciesNextCommand { get; }
    public IAsyncRelayCommand ScanCommand { get; }
    public IAsyncRelayCommand DoctorCommand { get; }
    public IAsyncRelayCommand FsckCommand { get; }
    public IAsyncRelayCommand ExportCommand { get; }
    public DesktopPage Page { get => _page; set { if (SetProperty(ref _page, value)) NotifyPages(); } }
    public bool IsOverview => Page == DesktopPage.Overview;
    public bool IsChanges => Page == DesktopPage.Changes;
    public bool IsHistory => Page == DesktopPage.History;
    public bool IsDependencies => Page == DesktopPage.Dependencies;
    public bool IsDiagnostics => Page == DesktopPage.Diagnostics;
    public string Project { get => _project; private set => SetProperty(ref _project, value); }
    public string Branch { get => _branch; private set => SetProperty(ref _branch, value); }
    public string Head { get => _head; private set => SetProperty(ref _head, value); }
    public string Counts { get => _counts; private set => SetProperty(ref _counts, value); }
    public IReadOnlyList<ChangeRow> Changes { get => _filteredChanges; private set => SetProperty(ref _filteredChanges, value); }
    public string ChangeSearch { get => _changeSearch; set { if (SetProperty(ref _changeSearch, value)) FilterChanges(); } }
    public ChangeRow? SelectedChange { get => _selectedChange; set => SetProperty(ref _selectedChange, value); }
    public IReadOnlyList<CommitLogEntry> History { get => _history; private set => SetProperty(ref _history, value); }
    public CommitLogEntry? SelectedCommit
    {
        get => _selectedCommit;
        set { if (SetProperty(ref _selectedCommit, value)) { DetailsState.Reset(); Details = null; Files = []; _filesNext = null; NotifyCommands(); } }
    }
    public CommitDetails? Details { get => _details; private set { if (SetProperty(ref _details, value)) OnPropertyChanged(nameof(CommitParents)); } }
    public string CommitParents => Details is null ? "" : string.Join(" · ", Details.Commit.Parents.Select(id => id.ToString()));
    public IReadOnlyList<SnapshotFile> Files { get => _files; private set => SetProperty(ref _files, value); }
    public IReadOnlyList<SnapshotDependency> Dependencies { get => _dependencies; private set => SetProperty(ref _dependencies, value); }
    public string DependencySearch { get => _dependencySearch; set { if (SetProperty(ref _dependencySearch, value)) FilterDependencies(); } }
    public string DependencyKind { get => _dependencyKind; set { if (SetProperty(ref _dependencyKind, value)) FilterDependencies(); } }
    public IReadOnlyList<string> DependencyKinds { get; } = ["All", "Asset", "Plugin", "Environment"];
    public string DependencySource => _dependencyPage?.Commit is { } id ? $"Snapshot {id}; {_dependencyPage.Total} recorded dependencies. Filters apply to this page." : "No committed snapshot; use Doctor to inspect current dependencies.";
    public IReadOnlyList<DiagnosticRow> DoctorRows { get => _doctorRows; private set => SetProperty(ref _doctorRows, value); }
    public IReadOnlyList<DiagnosticRow> FsckRows { get => _fsckRows; private set => SetProperty(ref _fsckRows, value); }
    public string ScanSummary { get => _scanSummary; private set => SetProperty(ref _scanSummary, value); }
    public string DoctorSummary { get => _doctorSummary; private set => SetProperty(ref _doctorSummary, value); }
    public string FsckSummary { get => _fsckSummary; private set => SetProperty(ref _fsckSummary, value); }
    public string? ExportNotice { get => _exportNotice; private set => SetProperty(ref _exportNotice, value); }

    public Task LoadCurrentPageAsync()
    {
        var pageLoad = Page switch
        {
            DesktopPage.Overview when MetadataState.State == ViewLoadState.Idle => RefreshAsync(),
            DesktopPage.Changes when ChangesState.State == ViewLoadState.Idle => RefreshAsync(),
            DesktopPage.History when HistoryState.State == ViewLoadState.Idle => LoadHistoryAsync(null),
            DesktopPage.Dependencies when DependenciesState.State == ViewLoadState.Idle => LoadDependenciesAsync(null, 0),
            _ => Task.CompletedTask
        };
        _pageLoads.RemoveAll(task => task.IsCompleted);
        _pageLoads.Add(pageLoad);
        return pageLoad;
    }

    private async Task RefreshAsync()
    {
        var session = _operations.SessionId;
        Project = ""; Branch = ""; Head = "";
        await LoadAsync(MetadataState, (services, root, _, token) => services.GetRequiredService<ViewerQueries>().MetadataAsync(root, token), value =>
        { Project = value.Project; Branch = value.Branch; Head = value.Head?.ToString() ?? "Unborn branch"; }, _ => false);
        if (_operations.SessionId != session || _disposed || MetadataState.State == ViewLoadState.Cancelled) return;
        Counts = ""; _changes = []; FilterChanges();
        await LoadAsync(ChangesState, (services, root, progress, token) => services.GetRequiredService<StatusUseCase>().ExecuteAsync(new StatusRequest(root), token, progress), value =>
        {
            var status = value.Status;
            Branch = status.CurrentBranch.Value;
            Head = status.HeadCommit?.ToString() ?? "Unborn branch";
            _changes = status.Staged.Concat(status.Modified).Concat(status.Untracked).Concat(status.Missing).Concat(status.Renamed).ToArray();
            Counts = $"{status.Staged.Count} staged · {status.Modified.Count} modified · {status.Untracked.Count} untracked · {status.Missing.Count} missing · {status.Renamed.Count} renamed";
            FilterChanges();
        }, value => value.Status.IsClean);
    }

    private Task LoadHistoryAsync(CommitId? cursor)
    {
        SelectedCommit = null;
        History = [];
        _historyNext = null;
        return LoadAsync(HistoryState, (services, root, _, token) => services.GetRequiredService<ViewerQueries>().HistoryAsync(root, cursor, cancellationToken: token), value =>
        { History = value.Entries; _historyNext = value.Next; }, value => value.Entries.Count == 0);
    }
    private Task LoadDetailsAsync(int offset)
    {
        if (SelectedCommit is not { } selected) return Task.CompletedTask;
        return LoadAsync(DetailsState, (services, root, _, token) => services.GetRequiredService<ViewerQueries>().CommitAsync(root, selected.Id, offset, cancellationToken: token), value =>
        { Details = value; Files = value.Files; _filesNext = value.NextOffset; }, value => value.TotalFiles == 0);
    }
    private Task LoadDependenciesAsync(CommitId? id, int offset) => LoadAsync(DependenciesState,
        (services, root, _, token) => services.GetRequiredService<ViewerQueries>().DependenciesAsync(root, id, offset, cancellationToken: token), value =>
        { _dependencyPage = value; FilterDependencies(); OnPropertyChanged(nameof(DependencySource)); }, value => value.Total == 0);

    private Task ScanAsync() => LoadAsync(ScanState,
        (services, root, progress, token) => services.GetRequiredService<ScanUseCase>().ExecuteAsync(new ScanRequest(root), token, progress), value =>
        {
            ScanSummary = $"{value.PrimaryDawName ?? "Unknown DAW"} · opaque fallback: {value.RequiresOpaqueFallback} · {value.Duration.TotalMilliseconds:F0} ms";
            _report = new DiagnosticExport(DiagnosticKind.Scan, !value.RequiresOpaqueFallback, new Dictionary<string, long> { ["opaqueFallback"] = value.RequiresOpaqueFallback ? 1 : 0 });
        }, _ => false);
    private Task DoctorAsync() => LoadAsync(DoctorState,
        (services, root, progress, token) => services.GetRequiredService<DoctorUseCase>().ExecuteAsync(new DoctorRequest(root), token, progress), value =>
        {
            DoctorSummary = $"Score {value.ReproducibilityScore:F1} · {value.BlockingIssuesCount} blocking · {value.WarningsCount} warnings";
            DoctorRows = value.ArtifactHealth.Select(a => new DiagnosticRow("Artifact", a.Path, a.Status.ToString()))
                .Concat(value.DependencyHealth.Select(d => new DiagnosticRow(d.Category, SafeName(d.Name), d.Status.ToString())))
                .Concat(value.EnvironmentHealth.DawInstallations.Select(d => new DiagnosticRow("Environment", d.DawName, d.IsDetected ? "Detected" : "Not detected"))).Take(200).ToArray();
            _report = new DiagnosticExport(DiagnosticKind.Doctor, value.IsHealthy, new Dictionary<string, long> { ["blocking"] = value.BlockingIssuesCount, ["warnings"] = value.WarningsCount, ["artifacts"] = value.ArtifactHealth.Count, ["dependencies"] = value.DependencyHealth.Count });
        }, _ => false);
    private Task FsckAsync() => LoadAsync(FsckState,
        (services, root, progress, token) => services.GetRequiredService<FsckUseCase>().ExecuteAsync(new FsckRequest(root), token, progress), value =>
        {
            var integrity = value.RepositoryIntegrity;
            FsckSummary = $"{integrity.TotalObjectsScanned} objects · {integrity.CorruptObjectsCount} corrupt · {integrity.MissingObjectsCount} missing";
            FsckRows = integrity.Errors.Concat(integrity.Warnings).Select(issue => new DiagnosticRow("Repository", issue.Code, issue.IsError ? "Error" : "Warning")).Take(200).ToArray();
            _report = new DiagnosticExport(DiagnosticKind.Fsck, value.IsHealthy, new Dictionary<string, long> { ["objects"] = integrity.TotalObjectsScanned, ["valid"] = integrity.ValidObjectsCount, ["corrupt"] = integrity.CorruptObjectsCount, ["missing"] = integrity.MissingObjectsCount, ["orphans"] = integrity.OrphanObjectsCount });
        }, _ => false);

    private async Task LoadAsync<T>(ViewerSection section,
        Func<IServiceProvider, string, IProgress<OperationProgress>, CancellationToken, Task<T>> work, Action<T> apply, Func<T, bool> empty)
    {
        if (_disposed || _operations.SessionId is not { } session) return;
        var revision = section.Begin();
        try
        {
            var completed = await _operations.RunAsync((services, root, progress, token) =>
            {
                if (!File.Exists(Path.Combine(root, "dawvc.yaml")) || !Directory.Exists(Path.Combine(root, ".dawvc")))
                    throw new ArgumentException("Repository no longer exists.");
                return work(services, root, progress, token);
            }, value =>
            {
                if (section.Revision != revision || _operations.SessionId != session) return;
                apply(value);
                section.State = empty(value) ? ViewLoadState.Empty : ViewLoadState.Success;
            }, _shutdown.Token);
            if (!completed && section.Revision == revision && _operations.SessionId == session)
            {
                var failure = _operations.Failure;
                section.State = failure?.Category == OperationFailureCategory.Cancellation ? ViewLoadState.Cancelled : ViewLoadState.Error;
                section.Error = failure?.TechnicalException is StagingIndexCorruptException
                    ? "The staging index is corrupt. Preserve index.db and its journal files; use Doctor/Fsck and recover explicitly. Nothing was reset."
                    : failure?.Category == OperationFailureCategory.Cancellation ? "Stopped. You can retry this view." : "This view could not be loaded. Check diagnostics or try again.";
            }
        }
        finally { NotifyCommands(); }
    }

    private void FilterChanges()
    {
        var rows = new List<ChangeRow>();
        foreach (var group in _changes.Where(item => item.Path.Value.Contains(ChangeSearch, StringComparison.OrdinalIgnoreCase)).GroupBy(item => item.Kind).OrderBy(group => group.Key))
        {
            bool first = true;
            foreach (var item in group.OrderBy(item => item.Path.Value, StringComparer.OrdinalIgnoreCase))
            {
                rows.Add(new ChangeRow(first ? group.Key.ToString() : "", group.Key.ToString(), item.Path.Value, item.Size, item.OldPath?.Value));
                first = false;
            }
        }
        SelectedChange = null;
        Changes = rows;
    }
    private void FilterDependencies() => Dependencies = _dependencyPage?.Entries.Where(d =>
        (DependencyKind == "All" || d.Kind == DependencyKind) && d.Name.Contains(DependencySearch, StringComparison.OrdinalIgnoreCase)).ToArray() ?? [];
    private static string SafeName(string value) => value.Replace('\\', '/').Split('/')[^1];
    private bool CanRun() => !_disposed && _operations.SessionId is not null && !_operations.IsBusy && !_sections.Any(s => s.State == ViewLoadState.Loading);
    private void NotifyCommands() { foreach (var command in _commands) command.NotifyCanExecuteChanged(); }
    private void StateChanged(object? sender, PropertyChangedEventArgs args) { if (args.PropertyName == nameof(ViewerSection.State)) NotifyCommands(); }
    private void OperationsChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(OperationCoordinator.SessionId) && _operations.SessionId is null) Reset();
        NotifyCommands();
    }
    public void Reset()
    {
        foreach (var section in _sections) section.Reset();
        Project = ""; Branch = ""; Head = ""; Counts = ""; _changes = []; Changes = []; SelectedChange = null;
        History = []; SelectedCommit = null; _historyNext = null; Details = null; Files = []; _filesNext = null;
        _dependencyPage = null; Dependencies = []; OnPropertyChanged(nameof(DependencySource));
        ScanSummary = ""; DoctorSummary = ""; FsckSummary = ""; DoctorRows = []; FsckRows = []; _report = null; ExportNotice = null;
        ChangeSearch = ""; DependencySearch = ""; DependencyKind = "All";
        NotifyCommands();
    }
    private void NotifyPages()
    {
        OnPropertyChanged(nameof(IsOverview)); OnPropertyChanged(nameof(IsChanges)); OnPropertyChanged(nameof(IsHistory));
        OnPropertyChanged(nameof(IsDependencies)); OnPropertyChanged(nameof(IsDiagnostics));
    }
    private async Task ExportAsync()
    {
        if (_report is null || _exporter is null || _operations.RepositoryPath is not { } root) return;
        var session = _operations.SessionId;
        try
        {
            var saved = await _exporter.SaveAsync(_report, root, _shutdown.Token);
            if (_operations.SessionId == session) ExportNotice = saved ? "Summary exported; paths and free-text details excluded." : "Export cancelled.";
        }
        catch (OperationCanceledException) { ExportNotice = "Export cancelled."; }
        catch (Exception) { ExportNotice = "Choose a writable JSON destination outside the repository."; }
    }
    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        _shutdown.Cancel();
        await Task.WhenAll(_commands.Select(c => c.ExecutionTask ?? Task.CompletedTask).Concat(_pageLoads));
        _operations.PropertyChanged -= OperationsChanged;
        foreach (var section in _sections) section.PropertyChanged -= StateChanged;
        _shutdown.Dispose();
    }
}
