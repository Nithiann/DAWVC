namespace DawVcs.Application.Exceptions;

/// <summary>
/// Wordt gegooid wanneer een artifact- of bestandsnaam veiligheidsregels schendt (FR-CHK-012).
/// Bijvoorbeeld: directory traversal, absolute paden, case collisions of symlinks.
/// </summary>
public sealed class PathSecurityException : Exception
{
    public string Path { get; }

    public PathSecurityException(string path, string reason)
        : base($"Security violation for path '{path}': {reason}")
    {
        Path = path;
    }
}
