using DawVcs.Domain.Common;
using DawVcs.Domain.Remotes;
using DawVcs.Domain.Repositories;

namespace DawVcs.Application.Remotes;

public sealed record RemoteAddRequest(string RepositoryDirectory, string Name, string Url);
public sealed record RemoteRemoveRequest(string RepositoryDirectory, string Name);
public sealed record RemoteListRequest(string RepositoryDirectory);
public sealed record RemoteListResult(IReadOnlyList<RemoteConfig> Remotes);

/// <summary>
/// Manages remote repository connections (add, remove, list) (FR-REM-001..004).
/// </summary>
public sealed class RemoteUseCase
{
    private readonly Func<string, IRepositoryContext> _contextFactory;

    public RemoteUseCase(Func<string, IRepositoryContext> contextFactory)
    {
        _contextFactory = contextFactory ?? throw new ArgumentNullException(nameof(contextFactory));
    }

    public RemoteConfig AddRemote(RemoteAddRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
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

        var config = new RemoteConfig(remoteName, request.Url.Trim());
        context.SaveRemote(config);
        return config;
    }

    public bool RemoveRemote(RemoteRemoveRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        using var context = _contextFactory(request.RepositoryDirectory);
        return context.RemoveRemote(request.Name);
    }

    public RemoteListResult ListRemotes(RemoteListRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        using var context = _contextFactory(request.RepositoryDirectory);
        return new RemoteListResult(context.GetRemotes());
    }
}
