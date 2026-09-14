using System.Text.RegularExpressions;

namespace DawVcs.Application.Common;

/// <summary>
/// Redacteert lokale gebruikerspaden en gevoelige machine-informatie voor logging en diagnostiek (NFR-SEC-007, IMP-0908).
/// </summary>
public static partial class PathRedactor
{
    private static readonly Lazy<string?> UserProfilePath = new(() =>
    {
        var path = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return string.IsNullOrWhiteSpace(path) ? null : Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    });

    private static readonly Regex WindowsUserRegex = new(@"[A-Za-z]:\\Users\\[^\\]+", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex UnixUserRegex = new(@"/home/[^/]+", RegexOptions.Compiled);
    private static readonly Regex CredentialRegex = new(@"://[^:]+:[^@]+@", RegexOptions.Compiled);

    public static string Redact(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        var result = path;

        // 1. Redact credentials if present (user:pass@host)
        result = CredentialRegex.Replace(result, "://<redacted>@");

        // 2. Redact current user's profile path if matched
        var currentProfile = UserProfilePath.Value;
        if (currentProfile != null && result.Contains(currentProfile, StringComparison.OrdinalIgnoreCase))
        {
            var userPrefix = Path.GetDirectoryName(currentProfile) ?? "C:\\Users";
            result = result.Replace(currentProfile, Path.Combine(userPrefix, "<user>"), StringComparison.OrdinalIgnoreCase);
        }

        // 3. Redact generic C:\Users\<name> or /home/<name>
        result = WindowsUserRegex.Replace(result, m =>
        {
            var drive = m.Value[..2];
            return $@"{drive}\Users\<user>";
        });

        result = UnixUserRegex.Replace(result, "/home/<user>");

        return result;
    }

    public static IReadOnlyList<string> RedactAll(IEnumerable<string> paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        return paths.Select(Redact).ToList();
    }
}
