using System.Diagnostics.CodeAnalysis;

namespace DawVcs.Domain.Hashing;

/// <summary>
/// Immutable 32-byte content hash representing the cryptographic identity of blobs and objects.
/// </summary>
public readonly struct ContentHash : IEquatable<ContentHash>, IComparable<ContentHash>
{
    public const int HashByteLength = 32;
    public const int HashHexLength = 64;

    private readonly byte[]? _bytes;

    public static ContentHash Empty { get; } = new(new byte[HashByteLength]);

    public ContentHash(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        if (bytes.Length != HashByteLength)
        {
            throw new ArgumentException($"ContentHash must be exactly {HashByteLength} bytes long.", nameof(bytes));
        }

        _bytes = (byte[])bytes.Clone();
    }

    public ContentHash(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length != HashByteLength)
        {
            throw new ArgumentException($"ContentHash must be exactly {HashByteLength} bytes long.", nameof(bytes));
        }

        _bytes = bytes.ToArray();
    }

    public ReadOnlySpan<byte> AsSpan() => _bytes ?? new byte[HashByteLength];

    public byte[] ToByteArray() => (byte[])(_bytes ?? new byte[HashByteLength]).Clone();

    public static ContentHash Parse(string hex)
    {
        ArgumentNullException.ThrowIfNull(hex);

        if (!TryParse(hex, out var hash))
        {
            throw new FormatException($"Invalid ContentHash hexadecimal format: '{hex}'. Must be 64 valid hex characters.");
        }

        return hash;
    }

    public static bool TryParse([NotNullWhen(true)] string? hex, out ContentHash hash)
    {
        hash = default;
        if (string.IsNullOrWhiteSpace(hex) || hex.Length != HashHexLength)
        {
            return false;
        }

        try
        {
            var bytes = Convert.FromHexString(hex);
            if (bytes.Length != HashByteLength)
            {
                return false;
            }

            hash = new ContentHash(bytes);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    public override string ToString() => Convert.ToHexString(AsSpan()).ToLowerInvariant();

    public bool Equals(ContentHash other) => AsSpan().SequenceEqual(other.AsSpan());

    public override bool Equals([NotNullWhen(true)] object? obj) => obj is ContentHash other && Equals(other);

    public override int GetHashCode()
    {
        var span = AsSpan();
        return HashCode.Combine(
            BitConverter.ToInt32(span[..4]),
            BitConverter.ToInt32(span.Slice(4, 4)),
            BitConverter.ToInt32(span.Slice(8, 4)),
            BitConverter.ToInt32(span.Slice(12, 4)));
    }

    public int CompareTo(ContentHash other) => AsSpan().SequenceCompareTo(other.AsSpan());

    public static bool operator ==(ContentHash left, ContentHash right) => left.Equals(right);

    public static bool operator !=(ContentHash left, ContentHash right) => !left.Equals(right);

    public static bool operator <(ContentHash left, ContentHash right) => left.CompareTo(right) < 0;

    public static bool operator <=(ContentHash left, ContentHash right) => left.CompareTo(right) <= 0;

    public static bool operator >(ContentHash left, ContentHash right) => left.CompareTo(right) > 0;

    public static bool operator >=(ContentHash left, ContentHash right) => left.CompareTo(right) >= 0;
}
