using DawVcs.Application.Exceptions;
using DawVcs.Domain.Checkouts;

namespace DawVcs.Application.Checkouts;

/// <summary>
/// Journalized atomic publisher that applies multi-file checkout plans with automatic rollback on partial failure (FR-CHK-006, ADR-IO-001).
/// </summary>
public sealed class PublicationJournal : IAsyncDisposable
{
    private enum JournalActionKind
    {
        Created,
        Replaced,
        Deleted
    }

    private sealed record JournalEntry(JournalActionKind Kind, string TargetPath, string? BackupPath);

    private readonly string _targetDir;
    private readonly string _backupDir;
    private readonly List<JournalEntry> _appliedEntries = [];
    private bool _committed;

    public PublicationJournal(string targetDir, string workspaceDotDawvcDir)
    {
        _targetDir = targetDir;
        var backupBase = Directory.Exists(workspaceDotDawvcDir) ? workspaceDotDawvcDir : targetDir;
        _backupDir = Path.Combine(backupBase, $".journal_backup_{Guid.NewGuid():N}");
    }

    public async Task<IReadOnlyList<string>> ExecutePlanAsync(
        string stagingDir,
        CheckoutPlan plan,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stagingDir);
        ArgumentNullException.ThrowIfNull(plan);

        Directory.CreateDirectory(_backupDir);
        var modifiedFiles = new List<string>();

        try
        {
            foreach (var action in plan.Actions)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var rel = action.Path.Value.Replace('/', Path.DirectorySeparatorChar);
                var finalPath = Path.Combine(_targetDir, rel);
                var stagedPath = Path.Combine(stagingDir, rel);

                switch (action.Kind)
                {
                    case CheckoutActionKind.Create:
                        {
                            var dir = Path.GetDirectoryName(finalPath);
                            if (!string.IsNullOrEmpty(dir))
                            {
                                Directory.CreateDirectory(dir);
                            }

                            if (!File.Exists(stagedPath))
                            {
                                throw new FileNotFoundException($"Staged artifact missing: {stagedPath}");
                            }

                            File.Move(stagedPath, finalPath, overwrite: false);
                            _appliedEntries.Add(new JournalEntry(JournalActionKind.Created, finalPath, null));
                            modifiedFiles.Add(action.Path.Value);
                            break;
                        }

                    case CheckoutActionKind.Replace:
                        {
                            var backupFilePath = Path.Combine(_backupDir, rel);
                            var backupDir = Path.GetDirectoryName(backupFilePath);
                            if (!string.IsNullOrEmpty(backupDir))
                            {
                                Directory.CreateDirectory(backupDir);
                            }

                            // Take backup before replacing
                            File.Copy(finalPath, backupFilePath, overwrite: true);

                            if (!File.Exists(stagedPath))
                            {
                                throw new FileNotFoundException($"Staged artifact missing: {stagedPath}");
                            }

                            File.Move(stagedPath, finalPath, overwrite: true);
                            _appliedEntries.Add(new JournalEntry(JournalActionKind.Replaced, finalPath, backupFilePath));
                            modifiedFiles.Add(action.Path.Value);
                            break;
                        }

                    case CheckoutActionKind.Delete:
                        {
                            if (File.Exists(finalPath))
                            {
                                var backupFilePath = Path.Combine(_backupDir, rel);
                                var backupDir = Path.GetDirectoryName(backupFilePath);
                                if (!string.IsNullOrEmpty(backupDir))
                                {
                                    Directory.CreateDirectory(backupDir);
                                }

                                // Take backup before deleting
                                File.Copy(finalPath, backupFilePath, overwrite: true);
                                File.Delete(finalPath);
                                _appliedEntries.Add(new JournalEntry(JournalActionKind.Deleted, finalPath, backupFilePath));
                                modifiedFiles.Add(action.Path.Value);
                            }
                            break;
                        }

                    case CheckoutActionKind.Unchanged:
                        {
                            // File is already identical, keep it in modifiedFiles for reporting
                            modifiedFiles.Add(action.Path.Value);
                            break;
                        }
                }
            }

            _committed = true;
            return modifiedFiles;
        }
        catch (Exception ex)
        {
            await RollbackAsync().ConfigureAwait(false);
            throw new CheckoutStagingException($"Multi-file checkout publication failed midway and was automatically rolled back: {ex.Message}", ex);
        }
    }

    private async Task RollbackAsync()
    {
        // Revert applied actions in reverse chronological order
        for (int i = _appliedEntries.Count - 1; i >= 0; i--)
        {
            var entry = _appliedEntries[i];
            try
            {
                switch (entry.Kind)
                {
                    case JournalActionKind.Created:
                        if (File.Exists(entry.TargetPath))
                        {
                            File.Delete(entry.TargetPath);
                        }
                        break;

                    case JournalActionKind.Replaced:
                    case JournalActionKind.Deleted:
                        if (entry.BackupPath != null && File.Exists(entry.BackupPath))
                        {
                            var targetDir = Path.GetDirectoryName(entry.TargetPath);
                            if (!string.IsNullOrEmpty(targetDir))
                            {
                                Directory.CreateDirectory(targetDir);
                            }
                            File.Copy(entry.BackupPath, entry.TargetPath, overwrite: true);
                        }
                        break;
                }
            }
            catch
            {
                // Best-effort rollback
            }
        }

        await CleanupBackupDirAsync().ConfigureAwait(false);
    }

    private Task CleanupBackupDirAsync()
    {
        if (Directory.Exists(_backupDir))
        {
            try
            {
                Directory.Delete(_backupDir, recursive: true);
            }
            catch
            {
                // Best-effort cleanup
            }
        }
        return Task.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        if (_committed)
        {
            await CleanupBackupDirAsync().ConfigureAwait(false);
        }
    }
}
