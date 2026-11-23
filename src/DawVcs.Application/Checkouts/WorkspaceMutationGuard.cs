using DawVcs.Application.Exceptions;
using DawVcs.Domain.Common;

namespace DawVcs.Application.Checkouts;

/// <summary>Opt-in mutation guards for explicit merge; not a lock against concurrent path replacement.</summary>
internal static class WorkspaceMutationGuard
{
    public static void Repository(string root)
    {
        foreach (var relative in new[] { "dawvc.yaml", ".dawvc/objects/.tmp", ".dawvc/refs/heads", ".dawvc/HEAD", ".dawvc/index.db", ".dawvc/bindings.json" })
            Ancestry(Path.Combine(root, relative));
    }

    public static void Reference(string root, BranchName branch) => Ancestry(Path.Combine(root, ".dawvc", "refs", "heads", branch.Value));

    public static void ObjectWrite(string root, CommitId id)
    {
        var hex = id.ToString();
        Ancestry(Path.Combine(root, ".dawvc", "objects", hex[..2], hex[2..]));
    }

    public static void Artifacts(string root, IEnumerable<string> paths)
    {
        foreach (var path in paths)
        {
            PathSecurityGuard.ValidatePath(path);
            var first = path.Replace('\\', '/').Split('/')[0];
            if (first.Equals(".dawvc", StringComparison.OrdinalIgnoreCase) || first.Equals(".git", StringComparison.OrdinalIgnoreCase)
                || first.Equals("dawvc.yaml", StringComparison.OrdinalIgnoreCase))
                throw new PathSecurityException(path, "Project artifacts cannot replace repository metadata.");
            Ancestry(Path.Combine(root, path));
        }
    }

    private static void Ancestry(string path)
    {
        for (var current = Path.GetFullPath(path); current is not null; current = Path.GetDirectoryName(current))
        {
            if (new DirectoryInfo(current).LinkTarget is not null || new FileInfo(current).LinkTarget is not null
                || ((Directory.Exists(current) || File.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0))
                throw new PathSecurityException(path, "Merge paths must not contain symbolic links or junctions.");
        }
    }
}
