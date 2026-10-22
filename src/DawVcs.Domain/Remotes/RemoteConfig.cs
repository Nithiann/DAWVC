using DawVcs.Domain.Common;

namespace DawVcs.Domain.Remotes;

/// <summary>
/// Configuration for a named remote repository connection.
/// </summary>
public sealed record RemoteConfig(
    RemoteName Name,
    string Url,
    IReadOnlyList<string>? FetchSpecs = null);
