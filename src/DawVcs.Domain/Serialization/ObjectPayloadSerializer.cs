using DawVcs.Domain.Artifacts;
using DawVcs.Domain.Common;
using DawVcs.Domain.Dependencies;
using DawVcs.Domain.Entities;
using DawVcs.Domain.Hashing;
using DawVcs.Domain.Metadata;

namespace DawVcs.Domain.Serialization;

public sealed class CommitPayloadDto
{
    public int SchemaVersion { get; set; }
    public string[]? Parents { get; set; }
    public string SnapshotId { get; set; } = string.Empty;
    public string Author { get; set; } = string.Empty;
    public long Timestamp { get; set; }
    public string Message { get; set; } = string.Empty;
}

public sealed class SnapshotPayloadDto
{
    public int SchemaVersion { get; set; }
    public string DawName { get; set; } = string.Empty;
    public int ContainerKind { get; set; }
    public string AggregateHash { get; set; } = string.Empty;
    public long CreatedAt { get; set; }
    public bool IsComplete { get; set; } = true;
    public string? IncompleteReason { get; set; }
    public EntryPayloadDto[]? Entries { get; set; }
    public DependencyPayloadDto[]? Dependencies { get; set; }
    public Dictionary<string, string>? Metadata { get; set; }
}

public sealed class EntryPayloadDto
{
    public string Path { get; set; } = string.Empty;
    public string BlobId { get; set; } = string.Empty;
    public string Hash { get; set; } = string.Empty;
    public long Size { get; set; }
    public int Role { get; set; }
}

public sealed class DependencyPayloadDto
{
    public string Id { get; set; } = string.Empty;
    public int Kind { get; set; }
    public string Name { get; set; } = string.Empty;
    public int Requirement { get; set; }
    public int Source { get; set; }
    public int Portability { get; set; }
    public string? Provenance { get; set; }

    // Asset properties
    public string? Hash { get; set; }
    public long? FileSize { get; set; }
    public string? OriginalPath { get; set; }
    public string? RelativePath { get; set; }
    public bool IsMissing { get; set; }

    // Plugin properties
    public string? Vendor { get; set; }
    public string? Product { get; set; }
    public int? Format { get; set; }
    public int? Role { get; set; }
    public string? VersionRequirement { get; set; }

    // Environment properties
    public string? EnvKey { get; set; }
    public string? EnvExpectedValue { get; set; }
}

/// <summary>
/// Helper for serializing and deserializing domain Commit and ProjectSnapshot payloads (v1).
/// </summary>
public static class ObjectPayloadSerializer
{
    public static Commit? DeserializeCommit(ReadOnlySpan<byte> payload, CommitId? id = null)
    {
        var dto = CanonicalJsonSerializer.Deserialize<CommitPayloadDto>(payload);
        if (dto is null) return null;

        var parents = (dto.Parents ?? Array.Empty<string>())
            .Select(p => CommitId.Parse(p))
            .ToList();

        var snapshotId = SnapshotId.Parse(dto.SnapshotId);
        var timestamp = DateTimeOffset.FromUnixTimeMilliseconds(dto.Timestamp);

        return new Commit(parents, snapshotId, dto.Author, timestamp, dto.Message, id);
    }

    public static CommitPayloadDto? ReadCommitDto(ReadOnlySpan<byte> payload)
    {
        return CanonicalJsonSerializer.Deserialize<CommitPayloadDto>(payload);
    }

    public static SnapshotPayloadDto? ReadSnapshotDto(ReadOnlySpan<byte> payload)
    {
        return CanonicalJsonSerializer.Deserialize<SnapshotPayloadDto>(payload);
    }

    public static ProjectSnapshot? DeserializeSnapshot(ReadOnlySpan<byte> payload, SnapshotId? id = null)
    {
        var dto = CanonicalJsonSerializer.Deserialize<SnapshotPayloadDto>(payload);
        if (dto is null) return null;

        var entries = (dto.Entries ?? Array.Empty<EntryPayloadDto>()).Select(e => new ArtifactEntry(
            new ArtifactPath(e.Path),
            BlobId.Parse(e.BlobId),
            ContentHash.Parse(e.Hash),
            e.Size,
            (ArtifactRole)e.Role
        )).ToList();

        ArtifactRoot root = (ArtifactKind)dto.ContainerKind switch
        {
            ArtifactKind.SingleFile => new SingleFileArtifact(entries.First()),
            ArtifactKind.Directory => new DirectoryArtifact(entries),
            ArtifactKind.Package => new PackageArtifact(entries),
            _ => new SingleFileArtifact(entries.First())
        };

        var project = new ProjectArtifact(dto.DawName, root);
        var createdAt = DateTimeOffset.FromUnixTimeMilliseconds(dto.CreatedAt);

        var depList = new List<Dependency>();
        if (dto.Dependencies != null)
        {
            foreach (var d in dto.Dependencies)
            {
                var portMode = (PortabilityMode)d.Portability;
                var policy = new PortabilityPolicy(portMode);
                var source = Enum.IsDefined(typeof(DependencySource), d.Source) ? (DependencySource)d.Source : DependencySource.NativeProjectParser;
                var requirement = Enum.IsDefined(typeof(DependencyRequirement), d.Requirement) ? (DependencyRequirement)d.Requirement : DependencyRequirement.Required;
                var provenance = !string.IsNullOrWhiteSpace(d.Provenance) ? new MetadataObservation<string>(d.Provenance, "Snapshot") : null;

                if ((DependencyKind)d.Kind == DependencyKind.Plugin)
                {
                    var vendor = !string.IsNullOrWhiteSpace(d.Vendor) ? d.Vendor : "Unknown";
                    var product = !string.IsNullOrWhiteSpace(d.Product) ? d.Product : (!string.IsNullOrWhiteSpace(d.Name) ? d.Name : "Unknown Plugin");
                    var format = d.Format.HasValue && Enum.IsDefined(typeof(PluginFormat), d.Format.Value)
                        ? (PluginFormat)d.Format.Value
                        : PluginFormat.Unknown;
                    var role = d.Role.HasValue && Enum.IsDefined(typeof(PluginRole), d.Role.Value)
                        ? (PluginRole)d.Role.Value
                        : PluginRole.Unknown;

                    depList.Add(new PluginDependency(
                        new DependencyId(d.Id),
                        new PluginIdentity(vendor, product, format),
                        role: role,
                        versionRequirement: d.VersionRequirement,
                        requirement: requirement,
                        source: source,
                        portability: policy,
                        provenance: provenance));
                }
                else if ((DependencyKind)d.Kind == DependencyKind.Environment)
                {
                    depList.Add(new EnvironmentDependency(
                        new DependencyId(d.Id),
                        d.EnvKey ?? "Unknown",
                        d.EnvExpectedValue ?? string.Empty,
                        requirement: requirement,
                        source: source,
                        provenance: provenance));
                }
                else
                {
                    ContentHash? hash = !string.IsNullOrWhiteSpace(d.Hash) && ContentHash.TryParse(d.Hash, out var parsedHash) ? parsedHash : null;
                    ArtifactPath? relPath = !string.IsNullOrWhiteSpace(d.RelativePath) ? new ArtifactPath(d.RelativePath) : null;

                    depList.Add(new AssetDependency(
                        new DependencyId(d.Id),
                        d.Name,
                        requirement,
                        source,
                        policy,
                        relativePath: relPath,
                        originalPath: d.OriginalPath,
                        hash: hash,
                        fileSize: d.FileSize,
                        isMissing: d.IsMissing,
                        provenance: provenance));
                }
            }
        }
        var depGraph = new DependencyGraph(depList);

        return new ProjectSnapshot(
            project,
            createdAt,
            dto.Metadata,
            id,
            isComplete: dto.IsComplete,
            incompleteReason: dto.IncompleteReason,
            dependencies: depGraph);
    }
}
