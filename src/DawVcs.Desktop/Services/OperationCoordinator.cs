using System.Diagnostics;

using CommunityToolkit.Mvvm.ComponentModel;

using DawVcs.Application.Common;
using DawVcs.Application.Repositories;

using Microsoft.Extensions.DependencyInjection;

namespace DawVcs.Desktop.Services;

/// <summary>Serializes engine work, owns session cancellation, and filters queued results by session/operation identity.</summary>
public sealed class OperationCoordinator(IEngineScopeFactory scopes, IUiDispatcher ui) : ObservableObject, IAsyncDisposable
{
    private sealed class Session(string root) : IDisposable
    {
        public Guid Id { get; } = Guid.NewGuid();
        public string Root { get; } = root;
        public CancellationTokenSource Cancellation { get; } = new();
        public void Dispose() => Cancellation.Dispose();
    }

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly object _sync = new();
    private readonly CancellationTokenSource _shutdown = new();
    private Session? _session;
    private CancellationTokenSource? _active;
    private CancellationTokenSource? _opening;
    private long _generation;
    private long _operation;
    private bool _disposed;
    private Task? _disposal;
    private bool _isBusy;
    private bool _isCancelling;
    private OperationProgress? _progress;
    private OperationFailure? _failure;

    public bool IsBusy { get => _isBusy; private set => SetProperty(ref _isBusy, value); }
    public bool IsCancelling { get => _isCancelling; private set => SetProperty(ref _isCancelling, value); }
    public OperationProgress? Progress { get => _progress; private set => SetProperty(ref _progress, value); }
    public OperationFailure? Failure { get => _failure; private set => SetProperty(ref _failure, value); }
    public string? RepositoryPath { get { lock (_sync) return _session?.Root; } }
    public Guid? SessionId { get { lock (_sync) return _session?.Id; } }

    public async Task<bool> OpenAsync(string path)
    {
        Session? previous;
        long generation;
        CancellationTokenSource opening;
        CancellationToken shutdownToken;
        CancellationToken lifetimeToken;
        lock (_sync)
        {
            if (_disposed) return false;
            generation = ++_generation;
            previous = _session;
            _session = null;
            previous?.Cancellation.Cancel();
            _opening?.Cancel();
            lifetimeToken = _shutdown.Token;
            opening = CancellationTokenSource.CreateLinkedTokenSource(lifetimeToken);
            _opening = opening;
            shutdownToken = opening.Token;
        }
        await UpdateAsync(generation, () => { NotifySession(); IsBusy = true; IsCancelling = false; Progress = null; Failure = null; });
        bool entered = false;
        try
        {
            await _gate.WaitAsync(lifetimeToken).ConfigureAwait(false);
            entered = true;
            if (!IsGeneration(generation)) return false;
            var root = await Task.Run(async () =>
            {
                shutdownToken.ThrowIfCancellationRequested();
                using var scope = scopes.Create();
                var result = await scope.Services.GetRequiredService<OpenRepositoryUseCase>()
                    .ExecuteAsync(new OpenRepositoryRequest(path), shutdownToken).ConfigureAwait(false);
                return result.Root;
            }, shutdownToken).ConfigureAwait(false);
            lock (_sync)
            {
                if (_disposed || generation != _generation) return false;
                _session = new Session(root);
            }
            await UpdateAsync(generation, NotifySession).ConfigureAwait(false);
            return IsGeneration(generation);
        }
        catch (Exception ex)
        {
            await UpdateAsync(generation, () => Failure = OperationFailure.FromException(ex)).ConfigureAwait(false);
            return false;
        }
        finally
        {
            previous?.Dispose();
            lock (_sync) { if (_opening == opening) _opening = null; }
            opening.Dispose();
            await UpdateAsync(generation, () => IsBusy = false).ConfigureAwait(false);
            if (entered) _gate.Release();
        }
    }

    public async Task<bool> RunAsync<T>(
        Func<IServiceProvider, string, IProgress<OperationProgress>, CancellationToken, Task<T>> work,
        Action<T> publish,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(work);
        ArgumentNullException.ThrowIfNull(publish);
        Session session;
        long generation;
        CancellationTokenSource linked;
        lock (_sync)
        {
            if (_disposed || _session is null) return false;
            session = _session;
            generation = _generation;
            linked = CancellationTokenSource.CreateLinkedTokenSource(session.Cancellation.Token, cancellationToken);
        }
        using (linked)
        {
            bool entered = false;
            long operation = 0;
            try
            {
                await _gate.WaitAsync(linked.Token).ConfigureAwait(false);
                entered = true;
                lock (_sync)
                {
                    if (_disposed || generation != _generation) return false;
                    _active = linked;
                    operation = ++_operation;
                }
                await UpdateAsync(generation, () => { IsBusy = true; IsCancelling = false; Progress = null; Failure = null; }).ConfigureAwait(false);
                using var progress = new CoalescingProgress(ui, value =>
                {
                    lock (_sync)
                    {
                        if (!_disposed && generation == _generation && operation == _operation && _active == linked)
                            Progress = value;
                    }
                });
                var result = await Task.Run(async () =>
                {
                    linked.Token.ThrowIfCancellationRequested();
                    using var scope = scopes.Create();
                    return await work(scope.Services, session.Root, progress, linked.Token).ConfigureAwait(false);
                }, linked.Token).ConfigureAwait(false);
                bool delivered = false;
                await UpdateAsync(generation, () =>
                {
                    // A successful backend return may represent durable publication despite a late cancel request.
                    publish(result);
                    delivered = true;
                }).ConfigureAwait(false);
                return delivered;
            }
            catch (Exception ex)
            {
                if (entered) await UpdateAsync(generation, () => Failure = OperationFailure.FromException(ex)).ConfigureAwait(false);
                return false;
            }
            finally
            {
                if (entered)
                {
                    lock (_sync) { if (_active == linked) _active = null; }
                    await UpdateAsync(generation, () => { IsBusy = false; IsCancelling = false; }).ConfigureAwait(false);
                    _gate.Release();
                }
            }
        }
    }

    public void Cancel()
    {
        long generation;
        CancellationTokenSource? active;
        CancellationTokenSource? opening;
        lock (_sync) { active = _active; opening = _opening; generation = _generation; }
        if (active is null && opening is null) return;
        TryCancel(active);
        TryCancel(opening);
        ui.Post(() =>
        {
            lock (_sync)
            {
                if (!_disposed && generation == _generation && IsBusy &&
                    ((active is not null && _active == active) || (opening is not null && _opening == opening))) IsCancelling = true;
            }
        });
    }

    private static void TryCancel(CancellationTokenSource? source)
    {
        try { source?.Cancel(); }
        catch (ObjectDisposedException) { /* The operation finished between capture and cancellation. */ }
    }

    public ValueTask DisposeAsync()
    {
        lock (_sync) return new ValueTask(_disposal ??= DisposeCoreAsync());
    }

    private async Task DisposeCoreAsync()
    {
        Session? session;
        lock (_sync)
        {
            _disposed = true;
            ++_generation;
            session = _session;
            _session = null;
            session?.Cancellation.Cancel();
            _shutdown.Cancel();
        }
        await _gate.WaitAsync().ConfigureAwait(false);
        session?.Dispose();
        await ui.InvokeAsync(() => { NotifySession(); IsBusy = false; IsCancelling = false; Progress = null; Failure = null; }).ConfigureAwait(false);
        _gate.Release();
        _gate.Dispose();
        _shutdown.Dispose();
    }

    private bool IsGeneration(long generation) { lock (_sync) return !_disposed && generation == _generation; }
    private Task UpdateAsync(long generation, Action update) => ui.InvokeAsync(() =>
    {
        lock (_sync) { if (!_disposed && generation == _generation) update(); }
    });
    private void NotifySession() { OnPropertyChanged(nameof(RepositoryPath)); OnPropertyChanged(nameof(SessionId)); }

    private sealed class CoalescingProgress(IUiDispatcher dispatcher, Action<OperationProgress> consume) : IProgress<OperationProgress>, IDisposable
    {
        private readonly object _sync = new();
        private OperationProgress? _latest;
        private long _last;
        private bool _posted;
        private bool _closed;
        public void Report(OperationProgress value)
        {
            lock (_sync)
            {
                if (_closed) return;
                _latest = value;
                if (_posted || (_last != 0 && Stopwatch.GetElapsedTime(_last).TotalMilliseconds < 50)) return;
                _posted = true;
            }
            dispatcher.Post(() =>
            {
                OperationProgress? latest;
                lock (_sync)
                {
                    _posted = false;
                    _last = Stopwatch.GetTimestamp();
                    latest = _closed ? null : _latest;
                }
                if (latest is not null) consume(latest);
            });
        }
        public void Dispose() { lock (_sync) _closed = true; }
    }
}
