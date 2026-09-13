using DawVcs.Domain.Hashing;

namespace DawVcs.Domain.Common;

/// <summary>
/// Strongly typed identifier for a commit, backed by its cryptographic ContentHash.
/// </summary>
public readonly record struct CommitId : IEquatable<CommitId>, IComparable<CommitId>
{
    public ContentHash Value { get; }

    public CommitId(ContentHash value)
    {
        Value = value;
    }

    public static CommitId Parse(string hex) => new(ContentHash.Parse(hex));

    public static bool TryParse(string? hex, out CommitId id)
    {
        id = default;
        if (ContentHash.TryParse(hex, out var hash))
        {
            id = new CommitId(hash);
            return true;
        }

        return false;
    }

    public override string ToString() => Value.ToString();

    public int CompareTo(CommitId other) => Value.CompareTo(other.Value);

    public static bool operator <(CommitId left, CommitId right) => left.CompareTo(right) < 0;
    public static bool operator <=(CommitId left, CommitId right) => left.CompareTo(right) <= 0;
    public static bool operator >(CommitId left, CommitId right) => left.CompareTo(right) > 0;
    public static bool operator >=(CommitId left, CommitId right) => left.CompareTo(right) >= 0;

    public static implicit operator ContentHash(CommitId id) => id.Value;
    public static implicit operator CommitId(ContentHash hash) => new(hash);
}
