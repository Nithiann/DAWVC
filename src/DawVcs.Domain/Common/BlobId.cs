using DawVcs.Domain.Hashing;

namespace DawVcs.Domain.Common;

/// <summary>
/// Strongly typed identifier for a raw blob, backed by its cryptographic ContentHash.
/// </summary>
public readonly record struct BlobId : IEquatable<BlobId>, IComparable<BlobId>
{
    public ContentHash Value { get; }

    public BlobId(ContentHash value)
    {
        Value = value;
    }

    public static BlobId Parse(string hex) => new(ContentHash.Parse(hex));

    public static bool TryParse(string? hex, out BlobId id)
    {
        id = default;
        if (ContentHash.TryParse(hex, out var hash))
        {
            id = new BlobId(hash);
            return true;
        }

        return false;
    }

    public override string ToString() => Value.ToString();

    public int CompareTo(BlobId other) => Value.CompareTo(other.Value);

    public static bool operator <(BlobId left, BlobId right) => left.CompareTo(right) < 0;
    public static bool operator <=(BlobId left, BlobId right) => left.CompareTo(right) <= 0;
    public static bool operator >(BlobId left, BlobId right) => left.CompareTo(right) > 0;
    public static bool operator >=(BlobId left, BlobId right) => left.CompareTo(right) >= 0;

    public static implicit operator ContentHash(BlobId id) => id.Value;
    public static implicit operator BlobId(ContentHash hash) => new(hash);
}
