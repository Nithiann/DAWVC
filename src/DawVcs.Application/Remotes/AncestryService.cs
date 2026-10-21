using DawVcs.Domain.Common;
using DawVcs.Domain.Repositories;

namespace DawVcs.Application.Remotes;

public enum SyncAncestryState
{
    UpToDate,
    FastForward,
    LocalAhead,
    Diverged
}

public sealed record SyncAncestryClassification(
    SyncAncestryState State,
    int CommitsAhead = 0,
    int CommitsBehind = 0);

/// <summary>
/// Classifies commit ancestry relationship between local tip and remote-tracking tip (FR-PUL-003..005).
/// Determines if an update is a fast-forward, already up-to-date, local ahead, or diverged.
/// </summary>
public class AncestryService
{
    public virtual async Task<SyncAncestryClassification> DetermineSyncStateAsync(
        CommitId? localTip,
        CommitId remoteTip,
        IRepositoryContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (!localTip.HasValue)
        {
            // Initial checkout / fresh clone: always fast-forward to remote tip
            return new SyncAncestryClassification(SyncAncestryState.FastForward);
        }

        if (localTip.Value == remoteTip)
        {
            return new SyncAncestryClassification(SyncAncestryState.UpToDate);
        }

        var isLocalAncestorOfRemote = await IsAncestorAsync(localTip.Value, remoteTip, context, cancellationToken).ConfigureAwait(false);
        if (isLocalAncestorOfRemote)
        {
            var count = await CountDistanceAsync(localTip.Value, remoteTip, context, cancellationToken).ConfigureAwait(false);
            return new SyncAncestryClassification(SyncAncestryState.FastForward, CommitsBehind: count);
        }

        var isRemoteAncestorOfLocal = await IsAncestorAsync(remoteTip, localTip.Value, context, cancellationToken).ConfigureAwait(false);
        if (isRemoteAncestorOfLocal)
        {
            var count = await CountDistanceAsync(remoteTip, localTip.Value, context, cancellationToken).ConfigureAwait(false);
            return new SyncAncestryClassification(SyncAncestryState.LocalAhead, CommitsAhead: count);
        }

        return new SyncAncestryClassification(SyncAncestryState.Diverged);
    }

    public virtual async Task<bool> IsAncestorAsync(
        CommitId candidateAncestor,
        CommitId descendant,
        IRepositoryContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (candidateAncestor == descendant) return true;

        var visited = new HashSet<CommitId>();
        var queue = new Queue<CommitId>();
        queue.Enqueue(descendant);
        visited.Add(descendant);

        while (queue.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var currentId = queue.Dequeue();

            var commit = await context.LoadCommitAsync(currentId, cancellationToken).ConfigureAwait(false);
            if (commit is null) continue;

            foreach (var parent in commit.Parents)
            {
                if (parent == candidateAncestor) return true;
                if (visited.Add(parent))
                {
                    queue.Enqueue(parent);
                }
            }
        }

        return false;
    }

    private static async Task<int> CountDistanceAsync(
        CommitId ancestor,
        CommitId descendant,
        IRepositoryContext context,
        CancellationToken cancellationToken)
    {
        var queue = new Queue<(CommitId Id, int Dist)>();
        var visited = new HashSet<CommitId> { descendant };
        queue.Enqueue((descendant, 0));

        while (queue.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var (currentId, dist) = queue.Dequeue();

            var commit = await context.LoadCommitAsync(currentId, cancellationToken).ConfigureAwait(false);
            if (commit is null) continue;

            foreach (var parent in commit.Parents)
            {
                if (parent == ancestor) return dist + 1;
                if (visited.Add(parent))
                {
                    queue.Enqueue((parent, dist + 1));
                }
            }
        }

        return 1;
    }
}
