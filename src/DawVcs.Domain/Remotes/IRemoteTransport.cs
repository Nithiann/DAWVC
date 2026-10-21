using DawVcs.Domain.Hashing;

namespace DawVcs.Domain.Remotes;

/// <summary>
/// Transport contract for communicating with a remote DAWVC repository (FR-REM-001..012, FR-FET-001).
/// </summary>
public interface IRemoteTransport : IDisposable
{
    string RemoteUrl { get; }

    /// <summary>
    /// Performs handshake validation against the remote (validates repositoryId, schemaVersion, layout).
    /// </summary>
    Task<RemoteHandshake> HandshakeAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists all advertised branch reference heads from the remote repository.
    /// </summary>
    Task<IReadOnlyList<RemoteRef>> ListRefsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Opens a read-only stream to the raw object envelope on the remote object store.
    /// </summary>
    Task<Stream?> OpenObjectPayloadStreamAsync(ContentHash hash, CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks whether an object exists on the remote.
    /// </summary>
    Task<bool> ObjectExistsAsync(ContentHash hash, CancellationToken cancellationToken = default);
}
