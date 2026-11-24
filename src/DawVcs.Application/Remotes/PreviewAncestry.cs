using DawVcs.Domain.Common;
using DawVcs.Domain.Repositories;
using DawVcs.Domain.Storage;

namespace DawVcs.Application.Remotes;

internal static class PreviewAncestry
{
    public static async Task<SyncAncestryClassification> ClassifyAsync(CommitId? local, CommitId remote,
        IRepositoryContext context, FetchPreview preview, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (local is null) return new(SyncAncestryState.FastForward);
        if (local == remote) return new(SyncAncestryState.UpToDate);
        var cache = new Dictionary<CommitId, IReadOnlyList<CommitId>>();
        var behind = await DistanceAsync(local.Value, remote).ConfigureAwait(false);
        if (behind.HasValue) return new(SyncAncestryState.FastForward, CommitsBehind: behind.Value);
        var ahead = await DistanceAsync(remote, local.Value).ConfigureAwait(false);
        return ahead.HasValue ? new(SyncAncestryState.LocalAhead, CommitsAhead: ahead.Value) : new(SyncAncestryState.Diverged);

        async Task<int?> DistanceAsync(CommitId ancestor, CommitId descendant)
        {
            var queue = new Queue<(CommitId Id, int Distance)>();
            var visited = new HashSet<CommitId> { descendant };
            queue.Enqueue((descendant, 0));
            while (queue.TryDequeue(out var next))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (visited.Count > FetchPreviewPlanner.MaxCommits) throw new InvalidEnvelopeException("Preview ancestry exceeds safety limits.");
                if (!cache.TryGetValue(next.Id, out var parents))
                {
                    if (!preview.Parents.TryGetValue(next.Id, out parents))
                    {
                        var commit = await context.LoadCommitAsync(next.Id, cancellationToken).ConfigureAwait(false)
                            ?? throw new InvalidEnvelopeException("Local history required for preview is incomplete.");
                        parents = commit.Parents;
                    }
                    if (cache.Count >= FetchPreviewPlanner.MaxCommits) throw new InvalidEnvelopeException("Preview ancestry exceeds safety limits.");
                    cache.Add(next.Id, parents);
                }
                foreach (var parent in parents)
                {
                    if (parent == ancestor) return next.Distance + 1;
                    if (visited.Add(parent)) queue.Enqueue((parent, next.Distance + 1));
                }
            }
            return null;
        }
    }
}
