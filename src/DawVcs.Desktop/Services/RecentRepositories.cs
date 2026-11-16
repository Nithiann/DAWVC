using System.Text.Json;

namespace DawVcs.Desktop.Services;

public sealed record RecentRepository(string Path)
{
    public string Name => System.IO.Path.GetFileName(Path.TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar));
}

public interface IRecentRepositories
{
    Task<IReadOnlyList<RecentRepository>> LoadAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(IReadOnlyList<RecentRepository> repositories, CancellationToken cancellationToken = default);
}

/// <summary>User-local, bounded MRU list. No paths are written to shared repository data.</summary>
public sealed class RecentRepositories(string filePath) : IRecentRepositories
{
    public static string DefaultPath
    {
        get
        {
            var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolderOption.DoNotVerify);
            if (!Path.IsPathFullyQualified(local)) throw new InvalidOperationException("No user-local application data location is available.");
            return Path.Combine(local, "DAWVC", "recent-repositories.json");
        }
    }

    public async Task<IReadOnlyList<RecentRepository>> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(filePath)) return Array.Empty<RecentRepository>();
        if (new FileInfo(filePath).Length > 64 * 1024) throw new InvalidDataException("Recent repository preferences exceed the size limit.");
        await using var stream = File.OpenRead(filePath);
        var paths = await JsonSerializer.DeserializeAsync<string[]>(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
        return Normalize(paths ?? []);
    }

    public async Task SaveAsync(IReadOnlyList<RecentRepository> repositories, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repositories);
        var directory = Path.GetDirectoryName(Path.GetFullPath(filePath))!;
        var data = JsonSerializer.SerializeToUtf8Bytes(Normalize(repositories.Select(r => r.Path)).Select(r => r.Path).ToArray());
        if (data.Length > 64 * 1024) throw new InvalidDataException("Recent repository preferences exceed the size limit.");
        Directory.CreateDirectory(directory);
        var temporary = Path.Combine(directory, $".recents-{Guid.NewGuid():N}.tmp");
        try
        {
            var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None, Options = FileOptions.Asynchronous };
            if (!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
            await using (var stream = new FileStream(temporary, options))
                await stream.WriteAsync(data, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporary, filePath, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private static RecentRepository[] Normalize(IEnumerable<string> paths) => paths
        .Where(path => !string.IsNullOrWhiteSpace(path) && Path.IsPathFullyQualified(path))
        .Select(Path.GetFullPath)
        .Distinct(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal)
        .Take(20).Select(path => new RecentRepository(path)).ToArray();
}
