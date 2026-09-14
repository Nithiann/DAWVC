namespace DawVcs.Infrastructure.FileSystem;

/// <summary>
/// Provides atomic file write and replacement primitives on the same filesystem volume.
/// Ensures that incomplete writes never overwrite or corrupt existing files.
/// </summary>
public static class AtomicFileWriter
{
    private const int BufferSize = 64 * 1024;

    /// <summary>
    /// Atomically writes content to the destination path using a same-directory temporary file
    /// with flush-to-disk and atomic replacement.
    /// </summary>
    /// <param name="destinationPath">Target file path.</param>
    /// <param name="writeAction">Async callback that streams data into the temporary file stream.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public static async Task WriteAtomicAsync(
        string destinationPath,
        Func<Stream, Task> writeAction,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);
        ArgumentNullException.ThrowIfNull(writeAction);

        var fullPath = Path.GetFullPath(destinationPath);
        var directory = Path.GetDirectoryName(fullPath)
            ?? throw new ArgumentException($"Invalid destination path: {destinationPath}", nameof(destinationPath));

        Directory.CreateDirectory(directory);

        // Same-directory temp file guarantees same filesystem / NTFS volume
        var tempPath = Path.Combine(directory, $".tmp_{Path.GetFileName(fullPath)}_{Guid.NewGuid():N}");

        try
        {
            var fileStreamOptions = new FileStreamOptions
            {
                Mode = FileMode.CreateNew,
                Access = FileAccess.Write,
                Share = FileShare.None,
                BufferSize = BufferSize,
                Options = FileOptions.Asynchronous
            };

            await using (var tempStream = new FileStream(tempPath, fileStreamOptions))
            {
                await writeAction(tempStream).ConfigureAwait(false);
                // Ensure data and filesystem metadata are physically flushed to disk
                tempStream.Flush(flushToDisk: true);
            }

            // Atomic rename/replace on Windows (MoveFileEx with MOVEFILE_REPLACE_EXISTING)
            File.Move(tempPath, fullPath, overwrite: true);
        }
        catch
        {
            // Clean up temporary file on failure
            TryDeleteFile(tempPath);
            throw;
        }
    }

    /// <summary>
    /// Atomically writes data from a source stream into the destination path.
    /// </summary>
    public static async Task WriteAtomicAsync(
        string destinationPath,
        Stream sourceStream,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sourceStream);

        await WriteAtomicAsync(
            destinationPath,
            async targetStream =>
            {
                await sourceStream.CopyToAsync(targetStream, BufferSize, cancellationToken).ConfigureAwait(false);
            },
            cancellationToken).ConfigureAwait(false);
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Suppress cleanup exceptions to avoid masking the primary error
        }
    }
}
