namespace DawVcs.Infrastructure.FileSystem;

/// <summary>Persistent file, exclusively opened while cooperating ref/HEAD writers run.</summary>
internal static class RepositoryRefLock
{
    public static FileStream Acquire(string metadataRoot)
    {
        return new FileStream(Path.Combine(metadataRoot, "ref-write.lock"), FileMode.OpenOrCreate,
            FileAccess.ReadWrite, FileShare.None);
    }
}
