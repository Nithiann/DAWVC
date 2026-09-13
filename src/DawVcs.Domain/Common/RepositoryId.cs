namespace DawVcs.Domain.Common;

/// <summary>
/// Strongly typed UUID identifier for a DAWVC repository.
/// </summary>
public readonly record struct RepositoryId : IEquatable<RepositoryId>, IComparable<RepositoryId>
{
    public Guid Value { get; }

    public RepositoryId(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("RepositoryId cannot be empty Guid.", nameof(value));
        }

        Value = value;
    }

    public static RepositoryId New() => new(Guid.NewGuid());

    public static RepositoryId Parse(string input)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(input);
        return new RepositoryId(Guid.Parse(input));
    }

    public static bool TryParse(string? input, out RepositoryId id)
    {
        id = default;
        if (Guid.TryParse(input, out var guid) && guid != Guid.Empty)
        {
            id = new RepositoryId(guid);
            return true;
        }

        return false;
    }

    public override string ToString() => Value.ToString("D");

    public int CompareTo(RepositoryId other) => Value.CompareTo(other.Value);

    public static bool operator <(RepositoryId left, RepositoryId right) => left.CompareTo(right) < 0;
    public static bool operator <=(RepositoryId left, RepositoryId right) => left.CompareTo(right) <= 0;
    public static bool operator >(RepositoryId left, RepositoryId right) => left.CompareTo(right) > 0;
    public static bool operator >=(RepositoryId left, RepositoryId right) => left.CompareTo(right) >= 0;
}
