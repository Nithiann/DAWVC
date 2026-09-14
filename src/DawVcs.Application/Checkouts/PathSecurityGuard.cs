using DawVcs.Application.Exceptions;

namespace DawVcs.Application.Checkouts;

/// <summary>
/// Beveiligt checkout en restore tegen directory traversal, absolute manifestpaden,
/// case collisions en corrupte artifactpaden (FR-CHK-012, TD §24.1).
/// </summary>
public static class PathSecurityGuard
{
    private static readonly char[] InvalidChars = Path.GetInvalidPathChars()
        .Concat([':', '*', '?', '"', '<', '>', '|'])
        .Distinct()
        .ToArray();

    /// <summary>
    /// Valideert een enkel artifactpad tegen traversal, absolute paden en ongeldige tekens.
    /// </summary>
    public static void ValidatePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new PathSecurityException(path ?? "<null>", "Path cannot be null or empty.");
        }

        // Paden mogen niet beginnen met slash of backslash (geen absolute paden)
        if (path.StartsWith('/') || path.StartsWith('\\'))
        {
            throw new PathSecurityException(path, "Path cannot start with a directory separator (must be relative).");
        }

        // Geen Windows drive-specifiers (C:)
        if (path.Length >= 2 && char.IsLetter(path[0]) && path[1] == ':')
        {
            throw new PathSecurityException(path, "Path cannot contain drive letters or colons.");
        }

        // Geen ongeldige tekens
        if (path.IndexOfAny(InvalidChars) >= 0)
        {
            throw new PathSecurityException(path, "Path contains invalid filename or path characters.");
        }

        // Split in segmenten en controleer traversal (..) en lege componenten
        var segments = path.Split(['/', '\\'], StringSplitOptions.None);
        foreach (var segment in segments)
        {
            if (string.IsNullOrEmpty(segment))
            {
                throw new PathSecurityException(path, "Path contains empty directory segments.");
            }

            if (segment == ".." || segment == ".")
            {
                throw new PathSecurityException(path, "Path cannot contain '.' or '..' traversal components.");
            }
        }
    }

    /// <summary>
    /// Valideert een verzameling paden tegen individuele schendingen én duplicate genormaliseerde case collisions (TD §24.1).
    /// </summary>
    public static void ValidateAll(IEnumerable<string> paths)
    {
        ArgumentNullException.ThrowIfNull(paths);

        var seen = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var path in paths)
        {
            ValidatePath(path);

            var normalized = path.Replace('\\', '/').Trim();
            if (seen.TryGetValue(normalized, out var existing))
            {
                if (!string.Equals(existing, path, StringComparison.Ordinal))
                {
                    throw new PathSecurityException(path, $"Case collision detected on case-insensitive filesystem with '{existing}'.");
                }
                throw new PathSecurityException(path, "Duplicate path entry detected.");
            }

            seen[normalized] = path;
        }
    }
}
