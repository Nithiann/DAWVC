using DawVcs.Domain.Artifacts;
using DawVcs.Domain.Common;
using DawVcs.Domain.Hashing;
using DawVcs.Domain.Serialization;

namespace DawVcs.Domain.Entities;

/// <summary>
/// Immutable snapshot entity capturing the state of project artifacts and metadata at a point in time.
/// </summary>
public sealed record ProjectSnapshot
{
    public int SchemaVersion { get; init; } = 1;
    public SnapshotId Id { get; init; }
    public ProjectArtifact Project { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public IReadOnlyDictionary<string, string> Metadata { get; init; }

    public ProjectSnapshot(
        ProjectArtifact project,
        DateTimeOffset createdAt,
        IReadOnlyDictionary<string, string>? metadata = null,
        SnapshotId? id = null)
    {
        ArgumentNullException.ThrowIfNull(project);

        Project = project;
        CreatedAt = createdAt;
        Metadata = metadata ?? new Dictionary<string, string>();

        Id = id ?? ComputeId(this);
    }

    public byte[] ToCanonicalBytes()
    {
        var entries = Project.Root.GetEntries().Select(e => new
        {
            path = e.Path.Value,
            blobId = e.Blob.ToString(),
            hash = e.Hash.ToString(),
            size = e.Size,
            role = (int)e.Role
        }).ToArray();

        return CanonicalJsonSerializer.SerializeCanonical(new
        {
            schemaVersion = SchemaVersion,
            dawName = Project.DawName,
            containerKind = (int)Project.Root.Kind,
            aggregateHash = Project.AggregateHash.ToString(),
            createdAt = CreatedAt.ToUnixTimeMilliseconds(),
            entries = entries,
            metadata = Metadata
        });
    }

    public static SnapshotId ComputeId(ProjectSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var canonicalBytes = snapshot.ToCanonicalBytes();
        var hash = Blake3ContentHasher.Hash(canonicalBytes);
        return new SnapshotId(hash);
    }
}
