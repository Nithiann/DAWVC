using DawVcs.Application.Operations;
using DawVcs.Application.Recovery;
using DawVcs.Domain.Common;
using DawVcs.Domain.Remotes;
using DawVcs.Domain.Repositories;

namespace DawVcs.Application.Remotes;

public sealed record RemoteAddRequest(string RepositoryDirectory, string Name, string Url)
{
    internal RepositoryOperation? Operation { get; init; }
}
public sealed record RemoteRemoveRequest(string RepositoryDirectory, string Name)
{
    internal RepositoryOperation? Operation { get; init; }
}
public sealed record RemoteListRequest(string RepositoryDirectory);
public sealed record RemoteListResult(IReadOnlyList<RemoteConfig> Remotes);

/// <summary>
/// Manages remote repository connections (add, remove, list) (FR-REM-001..004).
/// </summary>
public sealed class RemoteUseCase
{
    private readonly Func<string, IRepositoryContext> _contextFactory;
    private readonly IRemoteTransportFactory? _transportFactory;

    public RemoteUseCase(Func<string, IRepositoryContext> contextFactory, IRemoteTransportFactory? transportFactory = null)
    {
        _contextFactory = contextFactory ?? throw new ArgumentNullException(nameof(contextFactory));
        _transportFactory = transportFactory;
    }

    public RemoteConfig AddRemote(RemoteAddRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        using var operation = RepositoryOperation.Enter(request.RepositoryDirectory, request.Operation);
        WorkspaceRecoveryGuard.ThrowIfPending(request.RepositoryDirectory);
        using var context = _contextFactory(request.RepositoryDirectory);

        if (!RemoteName.TryCreate(request.Name, out var remoteName, out var error))
        {
            throw new ArgumentException(error, nameof(request));
        }

        if (string.IsNullOrWhiteSpace(request.Url))
        {
            throw new ArgumentException("Remote URL cannot be empty.", nameof(request));
        }

        var existing = context.GetRemote(remoteName.Value);
        if (existing != null)
        {
            throw new InvalidOperationException($"Remote '{remoteName.Value}' already exists.");
        }

        var url = request.Url.Trim();
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri) && Uri.UnescapeDataString(uri.UserInfo).Contains(':'))
            throw new ArgumentException("Remote URLs must not contain passwords.", nameof(request));
        if (_transportFactory is not null)
        {
            using var validated = _transportFactory.CreateTransport(url);
        }
        var config = new RemoteConfig(remoteName, url);
        context.SaveRemote(config);
        return config;
    }

    public bool RemoveRemote(RemoteRemoveRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        using var operation = RepositoryOperation.Enter(request.RepositoryDirectory, request.Operation);
        WorkspaceRecoveryGuard.ThrowIfPending(request.RepositoryDirectory);
        using var context = _contextFactory(request.RepositoryDirectory);
        return context.RemoveRemote(request.Name);
    }

    public RemoteListResult ListRemotes(RemoteListRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        using var context = _contextFactory(request.RepositoryDirectory);
        return new RemoteListResult(context.GetRemotes());
    }

    public Task<RemoteConfig> AddRemoteAsync(RemoteAddRequest request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(AddRemote(request));
    }

    public Task<bool> RemoveRemoteAsync(RemoteRemoveRequest request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(RemoveRemote(request));
    }

    public Task<RemoteListResult> ListRemotesAsync(RemoteListRequest request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(ListRemotes(request));
    }
}
