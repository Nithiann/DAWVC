using DawVcs.Domain.Common;

namespace DawVcs.Domain.Remotes;

/// <summary>
/// Handshake metadata exchanged when connecting to a remote repository (FR-FET-001).
/// </summary>
public sealed record RemoteHandshake(
    int SchemaVersion,
    RepositoryId RepositoryId,
    string? DawvcVersion = null);
