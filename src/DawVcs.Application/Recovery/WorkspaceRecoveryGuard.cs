using DawVcs.Domain.Repositories;

namespace DawVcs.Application.Recovery;

public static class WorkspaceRecoveryGuard
{
    public static bool HasPending(string root)
    {
        var directory = Path.Combine(Path.GetFullPath(root), ".dawvc", "transactions");
        return Directory.Exists(directory) && Directory.EnumerateFileSystemEntries(directory).Any();
    }

    public static void ThrowIfPending(string root)
    {
        if (HasPending(root))
            throw new WorkspaceRecoveryRequiredException("Workspace publication is active or unfinished. Await active operations, then inspect 'dawvc recover' before further mutations; use 'dawvc recover --rollback' to restore its previous state.");
    }
}
