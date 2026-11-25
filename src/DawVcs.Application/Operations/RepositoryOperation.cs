using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;

using DawVcs.Domain.Repositories;

namespace DawVcs.Application.Operations;

/// <summary>Explicit nested capabilities, never AsyncLocal/thread-inherited ownership.</summary>
internal sealed class RepositoryOperation : IDisposable
{
    private static readonly ConcurrentDictionary<string, Owner> Owners = new(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
    private readonly Owner _owner;
    private int _disposed;
    private RepositoryOperation(Owner owner) => _owner = owner;

    internal static RepositoryOperation Enter(string root, RepositoryOperation? parent = null,
        bool create = false, bool bareCreation = false, string? remoteMetadata = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var canonical = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        if (parent is not null)
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref parent._disposed) != 0, parent);
            if (!parent._owner.Virtual && !Same(parent._owner.Root, canonical))
                throw new ArgumentException("A nested operation capability cannot be used for another repository.");
            parent._owner.AddReference();
            return new RepositoryOperation(parent._owner);
        }
        var metadata = remoteMetadata ?? Path.Combine(canonical, ".dawvc");
        // Virtual/non-filesystem contexts have no disk lock domain. Real initialized repositories do.
        if (!create && remoteMetadata is null && !Directory.Exists(metadata) && !File.Exists(Path.Combine(canonical, "dawvc.yaml")))
            return new RepositoryOperation(new Owner(canonical, virtualOwner: true));
        if (remoteMetadata is null && !create && !Directory.Exists(metadata) && Directory.Exists(Path.Combine(canonical, "objects")))
            throw new ArgumentException("Select a working repository; bare storage has no working metadata directory.");
        var owner = new Owner(canonical, virtualOwner: false);
        if (!Owners.TryAdd(canonical, owner)) throw Busy();
        try
        {
            Guard(canonical);
            if (bareCreation || create)
            {
                var directory = Path.GetDirectoryName(canonical) ?? throw new ArgumentException("A repository destination cannot be a filesystem root.");
                Guard(directory);
                Directory.CreateDirectory(directory);
                var key = OperatingSystem.IsWindows() ? canonical.ToUpperInvariant() : canonical;
                var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key))).ToLowerInvariant();
                owner.Creation = Open(Path.Combine(directory, ".dawvc-create-" + hash + ".lock"));
            }
            if (!bareCreation)
            {
                Guard(metadata);
                Directory.CreateDirectory(metadata);
                owner.Metadata = Open(Path.Combine(metadata, "operation.lock"));
            }
            return new RepositoryOperation(owner);
        }
        catch { owner.Release(); throw; }
    }

    internal static bool OnlyCreationScaffold(string root)
    {
        if (!Directory.Exists(root)) return true;
        var entries = Directory.GetFileSystemEntries(root);
        if (entries.Length == 0) return true;
        if (entries.Length != 1 || !Same(entries[0], Path.Combine(root, ".dawvc")) || !Directory.Exists(entries[0])) return false;
        var metadata = Directory.GetFileSystemEntries(entries[0]);
        return metadata.Length == 1 && Same(metadata[0], Path.Combine(entries[0], "operation.lock")) && new FileInfo(metadata[0]).Length == 0;
    }

    private static bool Same(string left, string right) => string.Equals(left, right,
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
    private static RepositoryBusyException Busy(Exception? error = null) => new("Repository is busy with another operation. Await/cancel that operation and retry; never delete a live operation lock.", error);
    private static FileStream Open(string path)
    {
        Guard(path);
        FileStream stream;
        try { stream = new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
        catch (IOException error) when ((error.HResult & 0xffff) is 11 or 32 or 33) { throw Busy(error); }
        if (stream.Length == 0) return stream;
        stream.Dispose();
        throw new IOException("Unrecognized coordination-file contents; preserve the file for operator inspection.");
    }

    private static void Guard(string path)
    {
        for (var current = Path.GetFullPath(path); current is not null; current = Path.GetDirectoryName(current))
            if (new DirectoryInfo(current).LinkTarget is not null || new FileInfo(current).LinkTarget is not null
                || ((File.Exists(current) || Directory.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0))
                throw new ArgumentException("Repository operation paths must not contain links or junctions.");
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0) _owner.Release();
    }

    private sealed class Owner(string root, bool virtualOwner)
    {
        internal readonly string Root = root;
        internal readonly bool Virtual = virtualOwner;
        internal FileStream? Creation;
        internal FileStream? Metadata;
        private int _references = 1;
        internal void AddReference()
        {
            while (true)
            {
                var count = Volatile.Read(ref _references);
                ObjectDisposedException.ThrowIf(count == 0, this);
                if (Interlocked.CompareExchange(ref _references, count + 1, count) == count) return;
            }
        }
        internal void Release()
        {
            if (Interlocked.Decrement(ref _references) != 0) return;
            try { Metadata?.Dispose(); }
            finally
            {
                try { Creation?.Dispose(); }
                finally { if (!Virtual) Owners.TryRemove(Root, out _); }
            }
        }
    }
}
