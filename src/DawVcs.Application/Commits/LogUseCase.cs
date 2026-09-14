using DawVcs.Application.Common;
using DawVcs.Domain.Common;
using DawVcs.Domain.Repositories;

namespace DawVcs.Application.Commits;

public sealed record LogRequest(
    string RepositoryDirectory,
    int? Limit = null);

public sealed record CommitLogEntry(
    CommitId Id,
    SnapshotId SnapshotId,
    string Author,
    DateTimeOffset Timestamp,
    string Message,
    IReadOnlyList<CommitId> Parents);

public sealed record LogResult(
    IReadOnlyList<CommitLogEntry> Entries);

/// <summary>
/// Traverses commit history from HEAD along the first parent chain.
/// </summary>
public sealed class LogUseCase : IUseCase<LogRequest, LogResult>
{
    private readonly Func<string, IRepositoryContext> _contextFactory;

    public LogUseCase(Func<string, IRepositoryContext> contextFactory)
    {
        _contextFactory = contextFactory ?? throw new ArgumentNullException(nameof(contextFactory));
    }

    public async Task<LogResult> ExecuteAsync(LogRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var context = _contextFactory(request.RepositoryDirectory);
        var currentBranch = context.GetCurrentBranch();
        var headCommitId = context.GetBranchCommit(currentBranch);

        if (!headCommitId.HasValue)
        {
            return new LogResult(Array.Empty<CommitLogEntry>());
        }

        var entries = new List<CommitLogEntry>();
        var currentId = headCommitId;

        while (currentId.HasValue && (!request.Limit.HasValue || entries.Count < request.Limit.Value))
        {
            var commit = await context.LoadCommitAsync(currentId.Value, cancellationToken).ConfigureAwait(false);
            if (commit is null)
            {
                break;
            }

            entries.Add(new CommitLogEntry(
                commit.Id,
                commit.SnapshotId,
                commit.Author,
                commit.Timestamp,
                commit.Message,
                commit.Parents));

            currentId = commit.Parents.Count > 0 ? commit.Parents[0] : null;
        }

        return new LogResult(entries);
    }
}
