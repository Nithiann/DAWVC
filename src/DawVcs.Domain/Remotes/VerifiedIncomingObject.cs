using DawVcs.Domain.Hashing;
using DawVcs.Domain.Storage;

namespace DawVcs.Domain.Remotes;

/// <summary>
/// Represents an object in quarantine that has passed envelope, payload hash, and security verification.
/// </summary>
public sealed record VerifiedIncomingObject(
    ContentHash Hash,
    ObjectType ObjectType,
    long PayloadLength,
    string QuarantineFilePath);
