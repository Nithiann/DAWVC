using DawVcs.Application.Commits;
using DawVcs.Domain.Common;
using DawVcs.Domain.Dependencies;
using DawVcs.Domain.Repositories;
using DawVcs.Domain.Storage;

namespace DawVcs.Application.Viewing;

public sealed record ViewerMetadata(string Project, string Branch, CommitId? Head);
public sealed record HistoryPage(IReadOnlyList<CommitLogEntry> Entries, CommitId? Next);
public sealed record SnapshotFile(string Path, long Bytes, string Role, string Hash);
public sealed record CommitDetails(CommitLogEntry Commit, bool IsComplete, int TotalFiles, IReadOnlyList<SnapshotFile> Files, int? NextOffset);
public sealed record SnapshotDependency(string Name, string Kind, string Status, string Requirement, string Portability, string? Version);
public sealed record DependencyPage(CommitId? Commit, int Total, IReadOnlyList<SnapshotDependency> Entries, int? NextOffset);

/// <summary>Read-only viewer queries. Pagination pins immutable commit IDs; no status/index recovery is invoked.</summary>
public sealed class ViewerQueries(Func<string, IRepositoryContext> contexts)
{
    public Task<ViewerMetadata> MetadataAsync(string root, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var context = contexts(root);
        var config = context.LoadConfig();
        var branch = context.GetCurrentBranch();
        var head = context.GetBranchCommit(branch);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new ViewerMetadata(config.ProjectName, branch.Value, head));
    }

    public async Task<HistoryPage> HistoryAsync(string root, CommitId? cursor = null, int pageSize = 50, CancellationToken cancellationToken = default)
    {
        ValidatePage(0, pageSize, 100);
        cancellationToken.ThrowIfCancellationRequested();
        using var context = contexts(root);
        var current = cursor ?? context.GetBranchCommit(context.GetCurrentBranch());
        var entries = new List<CommitLogEntry>();
        var visited = new HashSet<CommitId>();
        while (current.HasValue && entries.Count < pageSize)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!visited.Add(current.Value)) throw new InvalidEnvelopeException("Commit history contains a cycle.");
            var commit = await context.LoadCommitAsync(current.Value, cancellationToken).ConfigureAwait(false)
                ?? throw new InvalidEnvelopeException("A history commit is missing.");
            entries.Add(new CommitLogEntry(commit.Id, commit.SnapshotId, commit.Author, commit.Timestamp, commit.Message, commit.Parents));
            current = commit.Parents.Count > 0 ? commit.Parents[0] : null;
        }
        return new HistoryPage(entries, current);
    }

    public async Task<CommitDetails> CommitAsync(string root, CommitId id, int offset = 0, int pageSize = 200, CancellationToken cancellationToken = default)
    {
        ValidatePage(offset, pageSize, 500);
        cancellationToken.ThrowIfCancellationRequested();
        using var context = contexts(root);
        var commit = await context.LoadCommitAsync(id, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidEnvelopeException("The selected commit is missing.");
        var snapshot = await context.LoadSnapshotAsync(commit.SnapshotId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidEnvelopeException("The selected snapshot is missing.");
        var count = 0;
        var files = new List<SnapshotFile>();
        foreach (var entry in snapshot.Project.Root.GetEntries())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (count >= offset && files.Count < pageSize) files.Add(new SnapshotFile(entry.Path.Value, entry.Size, entry.Role.ToString(), entry.Hash.ToString()));
            count++;
        }
        return new CommitDetails(new CommitLogEntry(commit.Id, commit.SnapshotId, commit.Author, commit.Timestamp, commit.Message, commit.Parents),
            snapshot.IsComplete, count, files, offset + files.Count < count ? offset + files.Count : null);
    }

    public async Task<DependencyPage> DependenciesAsync(string root, CommitId? id = null, int offset = 0, int pageSize = 200, CancellationToken cancellationToken = default)
    {
        ValidatePage(offset, pageSize, 500);
        cancellationToken.ThrowIfCancellationRequested();
        using var context = contexts(root);
        id ??= context.GetBranchCommit(context.GetCurrentBranch());
        if (!id.HasValue) return new DependencyPage(null, 0, [], null);
        var commit = await context.LoadCommitAsync(id.Value, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidEnvelopeException("The dependency commit is missing.");
        var snapshot = await context.LoadSnapshotAsync(commit.SnapshotId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidEnvelopeException("The dependency snapshot is missing.");
        var page = new List<SnapshotDependency>();
        foreach (var dependency in snapshot.Dependencies.Dependencies.Skip(offset).Take(pageSize))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var status = dependency is AssetDependency asset
                ? asset.IsMissing ? "Missing" : asset.Hash.HasValue ? "Recorded hash" : "Unresolved"
                : "Reference only";
            // Machine paths and provenance locators are deliberately excluded from the view projection.
            var name = dependency.Name.Replace('\\', '/').Split('/')[^1];
            page.Add(new SnapshotDependency(name, dependency.Kind.ToString(), status, dependency.Requirement.ToString(),
                dependency.Portability.Mode.ToString(), (dependency as PluginDependency)?.VersionRequirement));
        }
        var total = snapshot.Dependencies.Dependencies.Count;
        return new DependencyPage(id, total, page, offset + page.Count < total ? offset + page.Count : null);
    }

    private static void ValidatePage(int offset, int size, int maximum)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        if (size < 1 || size > maximum) throw new ArgumentOutOfRangeException(nameof(size));
    }
}
