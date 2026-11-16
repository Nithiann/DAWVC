using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;

using DawVcs.Application.Common;
using DawVcs.Application.Diagnostics;
using DawVcs.Application.Scanning;
using DawVcs.Bootstrap;

using Microsoft.Extensions.DependencyInjection;

namespace DawVcs.GuiSpike;

public sealed record AssetRow(string Path, string State, long Bytes);

/// <summary>Disposable experimental session shared by all three hosts; not production WP4 code.</summary>
public sealed class SpikeSession : INotifyPropertyChanged, IAsyncDisposable
{
    private readonly AssetRow[] _all = Enumerable.Range(0, 10_000)
        .Select(i => new AssetRow($"Samples/Asset{i:D5}.wav", i % 2 == 0 ? "Verified" : "Missing", 4096 + i)).ToArray();
    private AssetRow[] _rows;
    private string _repository = "";
    private string _filter = "";
    private string _summary = "Choose a disposable repository. Synthetic rows below are not repository data.";
    private string _progress = "Idle";
    private bool _busy;
    private int _operationId;
    private bool _disposed;
    private CancellationTokenSource? _cancellation;
    private Task _running = Task.CompletedTask;

    public SpikeSession() => _rows = _all;
    public event PropertyChangedEventHandler? PropertyChanged;
    public IReadOnlyList<AssetRow> Rows => _rows;
    public int RowCount => _rows.Length;
    public bool Busy => _busy;
    public bool CanStart => !_busy && !_disposed && _repository.Length > 0;
    public bool CanOpen => !_busy && !_disposed;
    public string Repository => _repository;
    public OperationFailureCategory? FailureCategory { get; private set; }
    public string Summary => _summary;
    public string ProgressText => _progress;
    public double LastFilterMilliseconds { get; private set; }

    public string Filter
    {
        get => _filter;
        set
        {
            _filter = value ?? "";
            var timer = Stopwatch.StartNew();
            _rows = _all.Where(row => row.Path.Contains(_filter, StringComparison.OrdinalIgnoreCase)).ToArray();
            LastFilterMilliseconds = timer.Elapsed.TotalMilliseconds;
            Notify();
            Notify(nameof(Rows));
            Notify(nameof(RowCount));
        }
    }

    public bool Open(string path)
    {
        if (!CanOpen) return false;
        _repository = "";
        Notify(nameof(Repository));
        Notify(nameof(CanStart));
        try
        {
            var root = Path.GetFullPath(path);
            // Do not instantiate a context for an arbitrary or dropped invalid directory.
            if (!File.Exists(Path.Combine(root, "dawvc.yaml")) || !Directory.Exists(Path.Combine(root, ".dawvc")))
            {
                SetSummary("Choose an initialized DAWVC repository.");
                return false;
            }
            _repository = root;
            Notify(nameof(Repository));
            Notify(nameof(CanStart));
            SetSummary("Repository selected. Status may update local metadata; use a disposable fixture.");
            return true;
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException)
        {
            SetSummary(OperationFailure.FromException(ex).Category.ToString());
            return false;
        }
    }

    public Task RunAsync(string operation)
    {
        if (!CanStart) return Task.CompletedTask;
        _busy = true;
        FailureCategory = null;
        NotifyState();
        _cancellation = new CancellationTokenSource();
        _running = RunCoreAsync(operation, ++_operationId, _cancellation.Token);
        return _running;
    }

    public void Cancel()
    {
        _cancellation?.Cancel();
        if (_busy) SetProgress("Stopping — waiting for backend completion");
    }

    private async Task RunCoreAsync(string operation, int operationId, CancellationToken token)
    {
        // Progress<T> captures the host context; throttle before posting to its event queue.
        var observed = new Progress<OperationProgress>(p =>
        {
            if (_busy && operationId == _operationId && !token.IsCancellationRequested) SetProgress($"{p.Stage}: {p.Completed}/{p.Total?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "?"}");
        });
        var progress = new SampledProgress(observed);
        try
        {
            // Includes context construction and synchronous work before the first await.
            var result = await Task.Run(async () =>
            {
                // Fresh provider per operation avoids carrying adapter caches across measurements.
                using var provider = new ServiceCollection().AddDawVcs().BuildServiceProvider();
                switch (operation)
                {
                    case "Status":
                        var status = (await provider.GetRequiredService<StatusUseCase>()
                            .ExecuteAsync(new StatusRequest(_repository), token, progress).ConfigureAwait(false)).Status;
                        return $"Branch {status.CurrentBranch.Value}; staged {status.Staged.Count}, modified {status.Modified.Count}, untracked {status.Untracked.Count}, missing {status.Missing.Count}";
                    case "Scan":
                        var scan = await provider.GetRequiredService<ScanUseCase>()
                            .ExecuteAsync(new ScanRequest(_repository), token, progress).ConfigureAwait(false);
                        return $"Scan: {scan.PrimaryDawName ?? "unknown DAW"}; opaque fallback {scan.RequiresOpaqueFallback}; {scan.Duration.TotalMilliseconds:F1} ms";
                    case "Doctor":
                        var doctor = await provider.GetRequiredService<DoctorUseCase>()
                            .ExecuteAsync(new DoctorRequest(_repository), token, progress).ConfigureAwait(false);
                        return $"Doctor: healthy {doctor.IsHealthy}; score {doctor.ReproducibilityScore:F1}; blocking {doctor.BlockingIssuesCount}; warnings {doctor.WarningsCount}";
                    default: throw new ArgumentException("Unknown spike operation.", nameof(operation));
                }
            }, token);
            SetSummary(result);
            SetProgress("Finished");
        }
        catch (Exception ex)
        {
            // Raw messages/paths are deliberately not shown or exported by the prototype.
            FailureCategory = OperationFailure.FromException(ex).Category;
            SetSummary(FailureCategory.Value.ToString());
            SetProgress(token.IsCancellationRequested ? "Stopped; durable metadata effects may remain" : "Failed");
        }
        finally
        {
            _cancellation?.Dispose();
            _cancellation = null;
            _busy = false;
            NotifyState();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        Cancel();
        await _running;
        NotifyState();
    }

    private void NotifyState()
    {
        Notify(nameof(Busy));
        Notify(nameof(CanStart));
        Notify(nameof(CanOpen));
    }

    private void SetSummary(string value) { _summary = value; Notify(nameof(Summary)); }
    private void SetProgress(string value) { _progress = value; Notify(nameof(ProgressText)); }
    private void Notify([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    private sealed class SampledProgress(IProgress<OperationProgress> target) : IProgress<OperationProgress>
    {
        private readonly object _gate = new();
        private long _last;
        private OperationStage? _stage;

        public void Report(OperationProgress value)
        {
            lock (_gate)
            {
                var now = Stopwatch.GetTimestamp();
                if (_stage == value.Stage && Stopwatch.GetElapsedTime(_last, now).TotalMilliseconds < 50) return;
                _stage = value.Stage;
                _last = now;
                target.Report(value);
            }
        }
    }
}
