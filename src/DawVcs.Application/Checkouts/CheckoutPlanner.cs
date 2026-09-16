using DawVcs.Domain.Artifacts;
using DawVcs.Domain.Checkouts;
using DawVcs.Domain.Entities;
using DawVcs.Domain.Hashing;
using DawVcs.Domain.Repositories;

namespace DawVcs.Application.Checkouts;

/// <summary>
/// Computes an explicit, safe checkout plan before any file is touched on disk (FR-CHK-008..010).
/// Detects overwrites of untracked, modified, or staged files.
/// </summary>
public static class CheckoutPlanner
{
    public static async Task<CheckoutPlan> PlanAsync(
        string workspaceRoot,
        ProjectSnapshot? currentSnapshot,
        ProjectSnapshot targetSnapshot,
        WorkingTreeStatus workingTreeStatus,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);
        ArgumentNullException.ThrowIfNull(targetSnapshot);
        ArgumentNullException.ThrowIfNull(workingTreeStatus);

        var actions = new List<CheckoutAction>();
        var conflicts = new List<CheckoutConflict>();

        var targetEntries = targetSnapshot.Project.Root.GetEntries()
            .ToDictionary(e => e.Path.Value, e => e, StringComparer.OrdinalIgnoreCase);

        var currentEntries = currentSnapshot?.Project.Root.GetEntries()
            .ToDictionary(e => e.Path.Value, e => e, StringComparer.OrdinalIgnoreCase)
            ?? new Dictionary<string, ArtifactEntry>(StringComparer.OrdinalIgnoreCase);

        var untrackedPaths = workingTreeStatus.Untracked.Select(u => u.Path.Value)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var modifiedPaths = workingTreeStatus.Modified.Select(m => m.Path.Value)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var stagedPaths = workingTreeStatus.Staged.Select(s => s.Path.Value)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var renamedPaths = workingTreeStatus.Renamed.Select(r => r.Path.Value)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // 1. Evaluate all files in target snapshot (Create, Replace, Unchanged)
        foreach (var (pathVal, targetEntry) in targetEntries)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var localFilePath = Path.Combine(workspaceRoot, targetEntry.Path.Value.Replace('/', Path.DirectorySeparatorChar));

            if (!File.Exists(localFilePath))
            {
                actions.Add(new CheckoutAction(targetEntry.Path, CheckoutActionKind.Create, TargetHash: targetEntry.Hash));
                continue;
            }

            // Local file exists on disk
            var localHash = await Blake3ContentHasher.HashFileAsync(localFilePath, cancellationToken).ConfigureAwait(false);

            if (localHash == targetEntry.Hash)
            {
                actions.Add(new CheckoutAction(targetEntry.Path, CheckoutActionKind.Unchanged, TargetHash: targetEntry.Hash, CurrentHash: localHash));
                continue;
            }

            // Local content differs from target. Always register Replace action for plan execution (e.g. with --force)
            actions.Add(new CheckoutAction(targetEntry.Path, CheckoutActionKind.Replace, TargetHash: targetEntry.Hash, CurrentHash: localHash));

            if (untrackedPaths.Contains(pathVal))
            {
                conflicts.Add(new CheckoutConflict(targetEntry.Path, "Untracked local file would be overwritten by checkout."));
            }
            else if (modifiedPaths.Contains(pathVal))
            {
                conflicts.Add(new CheckoutConflict(targetEntry.Path, "Locally modified file would be overwritten by checkout."));
            }
            else if (stagedPaths.Contains(pathVal))
            {
                conflicts.Add(new CheckoutConflict(targetEntry.Path, "Staged file would be overwritten by checkout."));
            }
            else if (renamedPaths.Contains(pathVal))
            {
                conflicts.Add(new CheckoutConflict(targetEntry.Path, "Renamed file would be overwritten by checkout."));
            }
            else if (currentEntries.TryGetValue(pathVal, out var currentEntry))
            {
                if (localHash != currentEntry.Hash)
                {
                    conflicts.Add(new CheckoutConflict(targetEntry.Path, "File has uncommitted changes that would be overwritten by checkout."));
                }
            }
            else
            {
                // File exists on disk, not in currentSnapshot, differs from target
                conflicts.Add(new CheckoutConflict(targetEntry.Path, "Untracked local file would be overwritten by checkout."));
            }
        }

        // 2. Evaluate obsolete files present in current snapshot but absent in target (Delete)
        foreach (var (pathVal, currentEntry) in currentEntries)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (targetEntries.ContainsKey(pathVal))
            {
                continue;
            }

            var localFilePath = Path.Combine(workspaceRoot, currentEntry.Path.Value.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(localFilePath))
            {
                continue;
            }

            var localHash = await Blake3ContentHasher.HashFileAsync(localFilePath, cancellationToken).ConfigureAwait(false);
            actions.Add(new CheckoutAction(currentEntry.Path, CheckoutActionKind.Delete, TargetHash: null, CurrentHash: localHash));

            if (localHash != currentEntry.Hash || modifiedPaths.Contains(pathVal) || stagedPaths.Contains(pathVal))
            {
                conflicts.Add(new CheckoutConflict(currentEntry.Path, "Locally modified file would be deleted by checkout."));
            }
        }

        return new CheckoutPlan(actions, conflicts);
    }
}
