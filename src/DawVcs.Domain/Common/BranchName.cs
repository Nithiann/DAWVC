using System.Buffers;
using System.Diagnostics.CodeAnalysis;

namespace DawVcs.Domain.Common;

/// <summary>
/// Validated branch name according to repository ref invariants.
/// </summary>
public readonly record struct BranchName : IEquatable<BranchName>, IComparable<BranchName>
{
    private static readonly SearchValues<char> InvalidChars = SearchValues.Create([' ', '~', '^', ':', '?', '*', '[', '\\']);

    public static BranchName Main { get; } = new("main");

    public string Value { get; }

    public BranchName(string value)
    {
        if (!Validate(value, out var error))
        {
            throw new ArgumentException(error, nameof(value));
        }

        Value = value;
    }

    public static bool TryCreate([NotNullWhen(true)] string? input, out BranchName branchName, [NotNullWhen(false)] out string? error)
    {
        branchName = default;
        if (!Validate(input, out error))
        {
            return false;
        }

        branchName = new BranchName(input);
        return true;
    }

    private static bool Validate([NotNullWhen(true)] string? name, [NotNullWhen(false)] out string? error)
    {
        error = null;
        if (string.IsNullOrWhiteSpace(name))
        {
            error = "Branch name cannot be empty or whitespace.";
            return false;
        }

        if (name.StartsWith('/') || name.EndsWith('/') || name.Contains("//", StringComparison.Ordinal))
        {
            error = "Branch name cannot start or end with '/' or contain consecutive slashes.";
            return false;
        }

        if (name.Contains("..", StringComparison.Ordinal))
        {
            error = "Branch name cannot contain '..'.";
            return false;
        }

        if (name.EndsWith(".lock", StringComparison.OrdinalIgnoreCase) || name.EndsWith('.'))
        {
            error = "Branch name cannot end with '.' or '.lock'.";
            return false;
        }

        if (name.AsSpan().IndexOfAny(InvalidChars) >= 0 || name.Any(char.IsControl))
        {
            error = "Branch name contains invalid characters or whitespace.";
            return false;
        }

        return true;
    }

    public override string ToString() => Value ?? string.Empty;

    public int CompareTo(BranchName other) => string.Compare(Value, other.Value, StringComparison.Ordinal);

    public static bool operator <(BranchName left, BranchName right) => left.CompareTo(right) < 0;
    public static bool operator <=(BranchName left, BranchName right) => left.CompareTo(right) <= 0;
    public static bool operator >(BranchName left, BranchName right) => left.CompareTo(right) > 0;
    public static bool operator >=(BranchName left, BranchName right) => left.CompareTo(right) >= 0;

    public static implicit operator string(BranchName name) => name.Value;
}
