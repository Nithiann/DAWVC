using DawVcs.Domain.Artifacts;
using DawVcs.Domain.Common;
using DawVcs.Domain.Dependencies;
using DawVcs.Domain.Hashing;
using DawVcs.Domain.Serialization;

namespace DawVcs.Domain.Entities;

/// <summary>
/// Onveranderlijke snapshot entity die de staat van projectartifacts, metadata en dependencies vastlegt (FR-DEP-002, FR-DEP-013).
/// </summary>
public sealed record ProjectSnapshot
{
    public int SchemaVersion { get; init; } = 1;
    public SnapshotId Id { get; init; }
    public ProjectArtifact Project { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public IReadOnlyDictionary<string, string> Metadata { get; init; }
    public bool IsComplete { get; init; }
    public string? IncompleteReason { get; init; }
    public DependencyGraph Dependencies { get; init; }

    public ProjectSnapshot(
        ProjectArtifact project,
        DateTimeOffset createdAt,
        IReadOnlyDictionary<string, string>? metadata = null,
        SnapshotId? id = null,
        bool isComplete = true,
        string? incompleteReason = null,
        DependencyGraph? dependencies = null)
    {
        ArgumentNullException.ThrowIfNull(project);

        Project = project;
        CreatedAt = createdAt;
        Metadata = metadata ?? new Dictionary<string, string>();
        IsComplete = isComplete;
        IncompleteReason = incompleteReason;
        Dependencies = dependencies ?? DependencyGraph.Empty;

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

        var depEntries = Dependencies.Dependencies.Select(d => new
        {
            id = d.Id.Value,
            kind = (int)d.Kind,
            name = d.Name,
            requirement = (int)d.Requirement,
            portability = (int)d.Portability.Mode
        }).ToArray();

        return CanonicalJsonSerializer.SerializeCanonical(new
        {
            schemaVersion = SchemaVersion,
            dawName = Project.DawName,
            containerKind = (int)Project.Root.Kind,
            aggregateHash = Project.AggregateHash.ToString(),
            createdAt = CreatedAt.ToUnixTimeMilliseconds(),
            isComplete = IsComplete,
            incompleteReason = IncompleteReason,
            entries = entries,
            dependencies = depEntries,
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
