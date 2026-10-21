using DawVcs.Application.Remotes;
using DawVcs.Domain.Remotes;

namespace DawVcs.Infrastructure.Remotes;

/// <summary>
/// Default implementation of IRemoteTransportFactory resolving filesystem paths, network shares, and file:// URLs.
/// </summary>
public sealed class DefaultRemoteTransportFactory : IRemoteTransportFactory
{
    public IRemoteTransport CreateTransport(string remoteUrl)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(remoteUrl);

        var trimmed = remoteUrl.Trim();
        if (trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            throw new NotSupportedException($"HTTPS remote transport is not supported in v0.2. Use filesystem paths, external drives, or network shares (UNC).");
        }

        var path = trimmed;
        if (path.StartsWith("file://", StringComparison.OrdinalIgnoreCase))
        {
            path = new Uri(path).LocalPath;
        }

        return new FileSystemRemoteTransport(path);
    }
}
