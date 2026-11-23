using DawVcs.Domain.Common;
using DawVcs.Domain.Configuration;

namespace DawVcs.Domain.Remotes;

/// <summary>
/// Handshake metadata exchanged when connecting to a remote repository (FR-FET-001).
/// </summary>
public sealed record RemoteHandshake(
    int SchemaVersion,
    RepositoryId RepositoryId,
    string? DawvcVersion = null)
{
    /// <summary>Optional versioned configuration, read without opening a writable repository context.</summary>
    public RepositoryConfig? Configuration { get; init; }
}
