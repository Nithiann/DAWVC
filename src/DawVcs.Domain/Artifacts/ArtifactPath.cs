using System.Diagnostics.CodeAnalysis;
using System.Text;

namespace DawVcs.Domain.Artifacts;

/// <summary>
/// Canonicalized relative artifact path within a project or repository.
/// Guarantees Unicode NFC normalization, forward slashes, and traversal safety.
/// </summary>
public readonly record struct ArtifactPath : IEquatable<ArtifactPath>, IComparable<ArtifactPath>
{
    public string Value { get; }

    public ArtifactPath(string path)
    {
        if (!TryNormalize(path, out var normalized, out var error))
        {
            throw new ArgumentException(error, nameof(path));
        }

        Value = normalized;
    }

    public static bool TryCreate([NotNullWhen(true)] string? path, out ArtifactPath artifactPath, [NotNullWhen(false)] out string? error)
    {
        artifactPath = default;
        if (!TryNormalize(path, out var normalized, out error))
        {
            return false;
        }

        artifactPath = new ArtifactPath(normalized);
        return true;
    }

    private static bool TryNormalize([NotNullWhen(true)] string? rawPath, [NotNullWhen(true)] out string? normalized, [NotNullWhen(false)] out string? error)
    {
        normalized = null;
        error = null;

        if (string.IsNullOrWhiteSpace(rawPath))
        {
            error = "ArtifactPath cannot be null, empty, or whitespace.";
            return false;
        }

        if (rawPath.Contains('\0'))
        {
            error = "ArtifactPath cannot contain null bytes.";
            return false;
        }

        // Unicode Form C normalization
        var path = rawPath.Normalize(NormalizationForm.FormC).Trim();

        // Convert backslashes to forward slashes
        path = path.Replace('\\', '/');

        // Check for Windows drive letters (e.g. C:)
        if (path.Length >= 2 && char.IsAsciiLetter(path[0]) && path[1] == ':')
        {
            error = $"ArtifactPath cannot be absolute with a drive letter: '{rawPath}'.";
            return false;
        }

        // Cannot start with a slash
        if (path.StartsWith('/'))
        {
            error = $"ArtifactPath cannot start with a leading slash: '{rawPath}'.";
            return false;
        }

        // Split into segments and validate
        var rawSegments = path.Split('/', StringSplitOptions.None);
        var cleanSegments = new List<string>(rawSegments.Length);

        foreach (var segment in rawSegments)
        {
            var trimmed = segment.Trim();
            if (trimmed.Length == 0)
            {
                error = $"ArtifactPath cannot contain empty segments or consecutive slashes: '{rawPath}'.";
                return false;
            }

            if (trimmed == "..")
            {
                error = $"Path traversal '..' is strictly forbidden: '{rawPath}'.";
                return false;
            }

            if (trimmed == ".")
            {
                // Skip self references unless it's the entire path
                continue;
            }

            cleanSegments.Add(trimmed);
        }

        if (cleanSegments.Count == 0)
        {
            error = "ArtifactPath cannot resolve to empty root.";
            return false;
        }

        normalized = string.Join('/', cleanSegments);
        return true;
    }

    public string FileName
    {
        get
        {
            var val = Value ?? string.Empty;
            int idx = val.LastIndexOf('/');
            return idx >= 0 ? val[(idx + 1)..] : val;
        }
    }

    public string? Extension
    {
        get
        {
            var fn = FileName;
            int idx = fn.LastIndexOf('.');
            return idx >= 0 ? fn[idx..] : null;
        }
    }

    public string? DirectoryName
    {
        get
        {
            var val = Value ?? string.Empty;
            int idx = val.LastIndexOf('/');
            return idx >= 0 ? val[..idx] : null;
        }
    }

    public ArtifactPath Combine(string relativeChild)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relativeChild);
        return new ArtifactPath($"{Value}/{relativeChild}");
    }

    public override string ToString() => Value ?? string.Empty;

    public int CompareTo(ArtifactPath other) => string.Compare(Value, other.Value, StringComparison.Ordinal);

    public static bool operator <(ArtifactPath left, ArtifactPath right) => left.CompareTo(right) < 0;
    public static bool operator <=(ArtifactPath left, ArtifactPath right) => left.CompareTo(right) <= 0;
    public static bool operator >(ArtifactPath left, ArtifactPath right) => left.CompareTo(right) > 0;
    public static bool operator >=(ArtifactPath left, ArtifactPath right) => left.CompareTo(right) >= 0;

    public static implicit operator string(ArtifactPath path) => path.Value;
}
