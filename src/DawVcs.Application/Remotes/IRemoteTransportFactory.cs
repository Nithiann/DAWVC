using DawVcs.Domain.Remotes;

namespace DawVcs.Application.Remotes;

/// <summary>
/// Factory for resolving and instantiating appropriate IRemoteTransport implementations for remote URLs.
/// </summary>
public interface IRemoteTransportFactory
{
    IRemoteTransport CreateTransport(string remoteUrl);
}
