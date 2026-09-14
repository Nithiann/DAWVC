using System.Globalization;

using DawVcs.Domain.Repositories;

namespace DawVcs.Application.Checkouts;

/// <summary>
/// Verantwoordelijk voor het veiligstellen van lokale werkbestanden vóór een geforceerde checkout (FR-CHK-010, FR-CHK-011, INV-012, AC-010).
/// </summary>
public static class RecoveryCopyService
{
    /// <summary>
    /// Maakt een volledige recovery copy van conflicterende en gewijzigde bestanden in .dawvc/recovery/recovery_<timestamp>_<guid:N>/.
    /// </summary>
    /// <param name="repositoryDirectory">Root directory van de repository.</param>
    /// <param name="filesToBackup">Lijst van relatieve paden van bestanden die gebackupt moeten worden.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Het absolute pad van de aangemaakte recovery directory.</returns>
    public static async Task<string> CreateRecoveryCopyAsync(
        string repositoryDirectory,
        IEnumerable<string> filesToBackup,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryDirectory);
        ArgumentNullException.ThrowIfNull(filesToBackup);

        var repoRoot = Path.GetFullPath(repositoryDirectory);
        var timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
        var recoveryDirName = $"recovery_{timestamp}_{Guid.NewGuid():N}";
        var recoveryDir = Path.Combine(repoRoot, ".dawvc", "recovery", recoveryDirName);

        Directory.CreateDirectory(recoveryDir);

        var manifestLines = new List<string>
        {
            $"# DAWVC Recovery Manifest",
            $"Created: {DateTimeOffset.UtcNow:O}",
            $"Source Repository: {repoRoot}",
            $"Files:"
        };

        foreach (var relativePath in filesToBackup.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var sourceFile = Path.Combine(repoRoot, relativePath);
            if (!File.Exists(sourceFile))
            {
                continue;
            }

            var destFile = Path.Combine(recoveryDir, relativePath);
            var destDir = Path.GetDirectoryName(destFile);
            if (!string.IsNullOrEmpty(destDir))
            {
                Directory.CreateDirectory(destDir);
            }

            // Veilige kopie met file stream share
            await using (var sourceStream = new FileStream(sourceFile, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, FileOptions.Asynchronous))
            await using (var destStream = new FileStream(destFile, FileMode.CreateNew, FileAccess.Write, FileShare.None, 64 * 1024, FileOptions.Asynchronous))
            {
                await sourceStream.CopyToAsync(destStream, cancellationToken).ConfigureAwait(false);
                await destStream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            manifestLines.Add($"- {relativePath}");
        }

        var manifestPath = Path.Combine(recoveryDir, "manifest.txt");
        await File.WriteAllLinesAsync(manifestPath, manifestLines, cancellationToken).ConfigureAwait(false);

        return recoveryDir;
    }
}
