using DawVcs.Application.Remotes;
using DawVcs.Domain.Remotes;
using DawVcs.Infrastructure.Remotes.Ssh;

namespace DawVcs.Infrastructure.Remotes;

/// <summary>
/// Resolves filesystem/file:// paths and dawvc:// addresses over OpenSSH.
/// </summary>
public sealed class DefaultRemoteTransportFactory : IRemoteTransportFactory
{
    public IRemoteTransport CreateTransport(string remoteUrl)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(remoteUrl);

        var trimmed = remoteUrl.Trim();
        if (trimmed.StartsWith("dawvc://", StringComparison.OrdinalIgnoreCase)) return new SshRemoteTransport(trimmed);
        if (trimmed.Contains("://", StringComparison.Ordinal) && !trimmed.StartsWith("file://", StringComparison.OrdinalIgnoreCase))
            throw new NotSupportedException("Unsupported remote scheme. Use dawvc:// over SSH, file:// or a filesystem path.");
        var path = trimmed;
        if (path.StartsWith("file://", StringComparison.OrdinalIgnoreCase))
        {
            path = new Uri(path).LocalPath;
        }

        return new FileSystemRemoteTransport(path);
    }
}
