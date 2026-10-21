using DawVcs.Domain.Hashing;

namespace DawVcs.Domain.Remotes;

/// <summary>
/// Domain interface for temporary storage of unverified objects before promotion to the main object store.
/// Satisfies FR-FET-003, FR-FET-007, and quarantine security invariants.
/// </summary>
public interface IQuarantineStore : IDisposable
{
    string SessionDirectory { get; }

    string GetIncomingPath(ContentHash hash);

    Task<Stream> CreateIncomingWriteStreamAsync(ContentHash hash, CancellationToken cancellationToken = default);

    Task PromoteAsync(VerifiedIncomingObject verifiedObject, CancellationToken cancellationToken = default);

    Task PromoteAllAsync(IEnumerable<VerifiedIncomingObject> verifiedObjects, CancellationToken cancellationToken = default);

    void PruneStaleDirectories(TimeSpan maxAge);

    void DiscardSession();
}
