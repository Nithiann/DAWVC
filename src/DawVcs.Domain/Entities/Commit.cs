using DawVcs.Domain.Common;
using DawVcs.Domain.Hashing;
using DawVcs.Domain.Serialization;

namespace DawVcs.Domain.Entities;

/// <summary>
/// Immutable commit entity in the repository history.
/// </summary>
public sealed record Commit
{
    public int SchemaVersion { get; init; } = 1;
    public CommitId Id { get; init; }
    public IReadOnlyList<CommitId> Parents { get; init; }
    public SnapshotId SnapshotId { get; init; }
    public string Author { get; init; }
    public DateTimeOffset Timestamp { get; init; }
    public string Message { get; init; }

    public Commit(
        IReadOnlyList<CommitId> parents,
        SnapshotId snapshotId,
        string author,
        DateTimeOffset timestamp,
        string message,
        CommitId? id = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(author);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);

        var trimmedMessage = message.Trim();
        if (trimmedMessage.Length == 0)
        {
            throw new ArgumentException("Commit message must contain at least one visible character after trimming.", nameof(message));
        }

        Parents = parents ?? Array.Empty<CommitId>();
        SnapshotId = snapshotId;
        Author = author.Trim();
        Timestamp = timestamp;
        Message = trimmedMessage;

        Id = id ?? ComputeId(this);
    }

    public byte[] ToCanonicalBytes()
    {
        return CanonicalJsonSerializer.SerializeCanonical(new
        {
            schemaVersion = SchemaVersion,
            parents = Parents.Select(p => p.ToString()).ToArray(),
            snapshotId = SnapshotId.ToString(),
            author = Author,
            timestamp = Timestamp.ToUnixTimeMilliseconds(),
            message = Message
        });
    }

    public static CommitId ComputeId(Commit commit)
    {
        ArgumentNullException.ThrowIfNull(commit);
        var canonicalBytes = commit.ToCanonicalBytes();
        var hash = Blake3ContentHasher.Hash(canonicalBytes);
        return new CommitId(hash);
    }
}
