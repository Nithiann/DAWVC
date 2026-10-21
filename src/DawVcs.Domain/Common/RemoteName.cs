using System.Buffers;
using System.Diagnostics.CodeAnalysis;

namespace DawVcs.Domain.Common;

/// <summary>
/// Validated remote repository name (e.g. 'origin', 'backup').
/// </summary>
public readonly record struct RemoteName : IEquatable<RemoteName>, IComparable<RemoteName>
{
    private static readonly SearchValues<char> InvalidChars = SearchValues.Create([' ', '/', '\\', ':', '*', '?', '"', '<', '>', '|', '~', '^', '[', ']']);

    public static RemoteName Origin { get; } = new("origin");

    public static RemoteName Parse(string value) => new(value);

    public string Value { get; }

    public RemoteName(string value)
    {
        if (!Validate(value, out var error))
        {
            throw new ArgumentException(error, nameof(value));
        }

        Value = value;
    }

    public static bool TryCreate([NotNullWhen(true)] string? input, out RemoteName remoteName, [NotNullWhen(false)] out string? error)
    {
        remoteName = default;
        if (!Validate(input, out error))
        {
            return false;
        }

        remoteName = new RemoteName(input);
        return true;
    }

    private static bool Validate([NotNullWhen(true)] string? name, [NotNullWhen(false)] out string? error)
    {
        error = null;
        if (string.IsNullOrWhiteSpace(name))
        {
            error = "Remote name cannot be empty or whitespace.";
            return false;
        }

        if (name.StartsWith('.') || name.EndsWith('.'))
        {
            error = "Remote name cannot start or end with a dot.";
            return false;
        }

        if (name.StartsWith('-') || name.EndsWith('-'))
        {
            error = "Remote name cannot start or end with a hyphen.";
            return false;
        }

        if (name.Contains("..", StringComparison.Ordinal))
        {
            error = "Remote name cannot contain '..'.";
            return false;
        }

        if (name.AsSpan().IndexOfAny(InvalidChars) >= 0 || name.Any(char.IsControl))
        {
            error = "Remote name contains invalid characters or whitespace.";
            return false;
        }

        return true;
    }

    public override string ToString() => Value ?? string.Empty;

    public int CompareTo(RemoteName other) => string.Compare(Value, other.Value, StringComparison.OrdinalIgnoreCase);

    public static bool operator <(RemoteName left, RemoteName right) => left.CompareTo(right) < 0;
    public static bool operator <=(RemoteName left, RemoteName right) => left.CompareTo(right) <= 0;
    public static bool operator >(RemoteName left, RemoteName right) => left.CompareTo(right) > 0;
    public static bool operator >=(RemoteName left, RemoteName right) => left.CompareTo(right) >= 0;

    public static implicit operator string(RemoteName name) => name.Value;
}
