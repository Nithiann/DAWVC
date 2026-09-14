using System.Text;

using DawVcs.Domain.Hashing;

namespace DawVcs.Domain.Artifacts;

/// <summary>
/// Abstract base representing the root of a project artifact container.
/// </summary>
public abstract record ArtifactRoot(ArtifactKind Kind)
{
    public abstract IReadOnlyList<ArtifactEntry> GetEntries();

    /// <summary>
    /// Computes the deterministic aggregate hash for this artifact root.
    /// </summary>
    public abstract ContentHash ComputeAggregateHash();
}

public sealed record SingleFileArtifact(ArtifactEntry File) : ArtifactRoot(ArtifactKind.SingleFile)
{
    public override IReadOnlyList<ArtifactEntry> GetEntries() => [File];

    public override ContentHash ComputeAggregateHash()
    {
        using var hasher = new Blake3ContentHasher();
        hasher.Update(Encoding.UTF8.GetBytes($"kind:SingleFile;path:{File.Path.Value};role:{(int)File.Role};hash:"));
        hasher.Update(File.Hash.AsSpan());
        return hasher.FinalizeHash();
    }
}

public sealed record DirectoryArtifact(IReadOnlyList<ArtifactEntry> Entries) : ArtifactRoot(ArtifactKind.Directory)
{
    public override IReadOnlyList<ArtifactEntry> GetEntries() => Entries;

    public override ContentHash ComputeAggregateHash() => ArtifactTreeHasher.Compute(Kind, Entries);
}

public sealed record PackageArtifact(IReadOnlyList<ArtifactEntry> Entries) : ArtifactRoot(ArtifactKind.Package)
{
    public override IReadOnlyList<ArtifactEntry> GetEntries() => Entries;

    public override ContentHash ComputeAggregateHash() => ArtifactTreeHasher.Compute(Kind, Entries);
}

public sealed record ArchiveArtifact(
    ArtifactEntry Archive,
    IReadOnlyList<ArchiveIndexEntry>? InspectedEntries = null)
    : ArtifactRoot(ArtifactKind.Archive)
{
    public override IReadOnlyList<ArtifactEntry> GetEntries() => [Archive];

    public override ContentHash ComputeAggregateHash()
    {
        // Aggregate hash is strictly based on the archive blob and container type, not on derived inspection indices.
        using var hasher = new Blake3ContentHasher();
        hasher.Update(Encoding.UTF8.GetBytes($"kind:Archive;path:{Archive.Path.Value};role:{(int)Archive.Role};hash:"));
        hasher.Update(Archive.Hash.AsSpan());
        return hasher.FinalizeHash();
    }
}

internal static class ArtifactTreeHasher
{
    public static ContentHash Compute(ArtifactKind kind, IReadOnlyList<ArtifactEntry> entries)
    {
        using var hasher = new Blake3ContentHasher();
        hasher.Update(Encoding.UTF8.GetBytes($"kind:{kind};count:{entries.Count}\n"));

        // Sort entries deterministically by canonical path
        var sorted = entries.OrderBy(e => e.Path.Value, StringComparer.Ordinal);

        foreach (var entry in sorted)
        {
            hasher.Update(Encoding.UTF8.GetBytes($"entry:{entry.Path.Value}:{(int)entry.Role}:{entry.Size}:"));
            hasher.Update(entry.Hash.AsSpan());
            hasher.Update(Encoding.UTF8.GetBytes("\n"));
        }

        return hasher.FinalizeHash();
    }
}
