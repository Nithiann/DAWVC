using DawVcs.Domain.Hashing;

namespace DawVcs.Domain.Storage;

/// <summary>
/// Domain port for interaction with the content-addressed object store.
/// </summary>
public interface IObjectStore
{
    /// <summary>
    /// Writes a raw binary blob to the object store with an envelope in a streaming fashion.
    /// Deduplicates identical content and returns its ContentHash.
    /// </summary>
    Task<ContentHash> WriteBlobAsync(Stream payloadStream, CancellationToken cancellationToken = default);

    /// <summary>
    /// Writes a metadata object (Tree, Snapshot, Commit) with the specified type and payload bytes.
    /// Deduplicates identical content and returns its ContentHash.
    /// </summary>
    Task<ContentHash> WriteObjectAsync(ObjectType type, ReadOnlyMemory<byte> payload, CancellationToken cancellationToken = default);

    /// <summary>
    /// Opens a read stream for the payload of the specified object, verifying its header and stream integrity.
    /// </summary>
    Task<Stream> OpenPayloadStreamAsync(ContentHash hash, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads and verifies the complete uncompressed payload into memory.
    /// </summary>
    Task<byte[]> ReadObjectPayloadAsync(ContentHash hash, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads and validates the 56-byte envelope header for the object.
    /// </summary>
    Task<ObjectEnvelopeHeader> ReadHeaderAsync(ContentHash hash, CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks whether an object with the given hash exists in the store.
    /// </summary>
    bool Exists(ContentHash hash);

    /// <summary>
    /// Returns all stored object entries present in the object store.
    /// </summary>
    IReadOnlyList<StoredObjectEntry> EnumerateStoredObjects();

    /// <summary>
    /// Fully streams and validates the envelope header and BLAKE3 payload hash for the specified object.
    /// </summary>
    Task VerifyObjectIntegrityAsync(ContentHash hash, CancellationToken cancellationToken = default);
}

public sealed record StoredObjectEntry(string RelativePath, ContentHash? Hash, bool HasValidName);

public class HashCollisionException : Exception
{
    public HashCollisionException(string message) : base(message) { }
}
