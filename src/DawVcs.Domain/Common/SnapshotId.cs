using DawVcs.Domain.Hashing;

namespace DawVcs.Domain.Common;

/// <summary>
/// Strongly typed identifier for a project snapshot manifest, backed by its cryptographic ContentHash.
/// </summary>
public readonly record struct SnapshotId : IEquatable<SnapshotId>, IComparable<SnapshotId>
{
    public ContentHash Value { get; }

    public SnapshotId(ContentHash value)
    {
        Value = value;
    }

    public static SnapshotId Parse(string hex) => new(ContentHash.Parse(hex));

    public static bool TryParse(string? hex, out SnapshotId id)
    {
        id = default;
        if (ContentHash.TryParse(hex, out var hash))
        {
            id = new SnapshotId(hash);
            return true;
        }

        return false;
    }

    public override string ToString() => Value.ToString();

    public int CompareTo(SnapshotId other) => Value.CompareTo(other.Value);

    public static bool operator <(SnapshotId left, SnapshotId right) => left.CompareTo(right) < 0;
    public static bool operator <=(SnapshotId left, SnapshotId right) => left.CompareTo(right) <= 0;
    public static bool operator >(SnapshotId left, SnapshotId right) => left.CompareTo(right) > 0;
    public static bool operator >=(SnapshotId left, SnapshotId right) => left.CompareTo(right) >= 0;

    public static implicit operator ContentHash(SnapshotId id) => id.Value;
    public static implicit operator SnapshotId(ContentHash hash) => new(hash);
}
