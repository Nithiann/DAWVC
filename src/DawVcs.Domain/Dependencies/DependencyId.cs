using System.Text;

using DawVcs.Domain.Hashing;

namespace DawVcs.Domain.Dependencies;

/// <summary>
/// Unieke, stabiele identifier voor een project- of omgevingsdependency (FR-DEP-002).
/// Identiteit van assets is strikt content-based (asset:blake3:<hash>).
/// </summary>
public readonly record struct DependencyId : IEquatable<DependencyId>, IComparable<DependencyId>
{
    public string Value { get; }

    public DependencyId(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        Value = value.Trim();
    }

    public static DependencyId New() => new(Guid.NewGuid().ToString("N"));

    public static DependencyId ForAsset(ContentHash hash) => new($"asset:blake3:{hash.ToString().ToLowerInvariant()}");

    public static DependencyId ForUnresolvedAsset(string rawLocator)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rawLocator);
        var locatorHash = Blake3ContentHasher.Hash(Encoding.UTF8.GetBytes(rawLocator.Trim()));
        return new($"asset:unresolved:{locatorHash.ToString().ToLowerInvariant()}");
    }

    public static DependencyId ForPlugin(string vendor, string product, string format) =>
        new($"plugin:{vendor.ToLowerInvariant()}:{product.ToLowerInvariant()}:{format.ToLowerInvariant()}");

    public int CompareTo(DependencyId other) => string.CompareOrdinal(Value, other.Value);

    public static bool operator <(DependencyId left, DependencyId right) => left.CompareTo(right) < 0;
    public static bool operator <=(DependencyId left, DependencyId right) => left.CompareTo(right) <= 0;
    public static bool operator >(DependencyId left, DependencyId right) => left.CompareTo(right) > 0;
    public static bool operator >=(DependencyId left, DependencyId right) => left.CompareTo(right) >= 0;

    public override string ToString() => Value;
}
