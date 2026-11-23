using System.Text.Json;

using DawVcs.Application.Checkouts;
using DawVcs.Domain.Common;
using DawVcs.Domain.Hashing;
using DawVcs.Domain.Serialization;
using DawVcs.Domain.Storage;

namespace DawVcs.Application.Remotes;

public sealed record PushObject(ObjectEnvelopeHeader Header, byte[]? Metadata);
public sealed record PushClosureResult(IReadOnlyList<ObjectEnvelopeHeader> Objects, IReadOnlySet<ContentHash> Commits);

/// <summary>Bounded full-closure traversal shared by sender planning and receiver publication.</summary>
public static class PushClosure
{
    public static async Task<PushClosureResult> ReadAsync(CommitId tip,
        Func<ContentHash, ObjectType, CancellationToken, Task<PushObject>> readVerified,
        CancellationToken cancellationToken = default)
    {
        var objects = new Dictionary<ContentHash, ObjectEnvelopeHeader>();
        var commits = new HashSet<ContentHash>();
        var pending = new Queue<(ContentHash Hash, ObjectType Type, long? Size)>();
        pending.Enqueue((tip.Value, ObjectType.Commit, null));
        while (pending.TryDequeue(out var next))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (objects.TryGetValue(next.Hash, out var existing))
            {
                ValidateHeader(existing, next.Type, next.Size);
                continue;
            }
            if (objects.Count >= 1_000_000 || commits.Count >= 50_000)
                throw new InvalidEnvelopeException("Push closure exceeds traversal safety limits.");
            var item = await readVerified(next.Hash, next.Type, cancellationToken).ConfigureAwait(false);
            ValidateHeader(item.Header, next.Type, next.Size);
            if (item.Header.PayloadHash != next.Hash) throw new InvalidEnvelopeException("Object identity mismatch in push closure.");
            objects.Add(next.Hash, item.Header);
            try
            {
                if (next.Type == ObjectType.Commit)
                {
                    commits.Add(next.Hash);
                    var commit = ObjectPayloadSerializer.ReadCommitDto(item.Metadata!)
                        ?? throw new InvalidEnvelopeException("Missing commit metadata.");
                    if (commit.SchemaVersion != 1) throw new InvalidEnvelopeException("Unsupported commit schema.");
                    pending.Enqueue((ParseHash(commit.SnapshotId), ObjectType.Snapshot, null));
                    foreach (var parent in commit.Parents ?? []) pending.Enqueue((ParseHash(parent), ObjectType.Commit, null));
                }
                else if (next.Type == ObjectType.Snapshot)
                {
                    var snapshot = ObjectPayloadSerializer.ReadSnapshotDto(item.Metadata!)
                        ?? throw new InvalidEnvelopeException("Missing snapshot metadata.");
                    if (snapshot.SchemaVersion != 1 || snapshot.Entries is null)
                        throw new InvalidEnvelopeException("Invalid snapshot schema or entries.");
                    PathSecurityGuard.ValidateAll(snapshot.Entries.Select(e => e.Path));
                    foreach (var dependency in snapshot.Dependencies ?? [])
                        if (!string.IsNullOrWhiteSpace(dependency.RelativePath)) PathSecurityGuard.ValidatePath(dependency.RelativePath);
                    foreach (var entry in snapshot.Entries)
                    {
                        var hash = ParseHash(entry.Hash);
                        if (entry.Size < 0 || ParseHash(entry.BlobId) != hash)
                            throw new InvalidEnvelopeException("Snapshot blob identity or size is inconsistent.");
                        pending.Enqueue((hash, ObjectType.Blob, entry.Size));
                    }
                }
            }
            catch (JsonException ex) { throw new InvalidEnvelopeException($"Malformed push metadata: {ex.Message}"); }
        }
        return new PushClosureResult(objects.Values.ToList(), commits);
    }

    private static ContentHash ParseHash(string text) => ContentHash.TryParse(text, out var hash)
        ? hash : throw new InvalidEnvelopeException("Invalid object reference in push metadata.");

    private static void ValidateHeader(ObjectEnvelopeHeader header, ObjectType expected, long? size)
    {
        if (header.ObjectType != expected || (size.HasValue && header.PayloadLength != (ulong)size.Value))
            throw new InvalidEnvelopeException("Push object type or size disagrees with its reference.");
        if (expected != ObjectType.Blob && header.PayloadLength > (ulong)IncomingObjectVerifier.MaxMetadataPayloadBytes)
            throw new InvalidEnvelopeException("Push metadata exceeds the supported size limit.");
    }
}
