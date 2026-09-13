namespace DawVcs.Domain.Hashing;

/// <summary>
/// Incremental streaming content hasher.
/// </summary>
public interface IContentHasher : IDisposable
{
    /// <summary>
    /// Updates the running hash state with the provided buffer slice.
    /// </summary>
    void Update(ReadOnlySpan<byte> data);

    /// <summary>
    /// Streams content from the given stream and updates the hash state incrementally.
    /// </summary>
    Task UpdateAsync(Stream stream, CancellationToken cancellationToken = default);

    /// <summary>
    /// Finalizes and outputs the resulting 32-byte content hash.
    /// </summary>
    ContentHash FinalizeHash();

    /// <summary>
    /// Resets the hasher back to initial state for reuse.
    /// </summary>
    void Reset();
}
