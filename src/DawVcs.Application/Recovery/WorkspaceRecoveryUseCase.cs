using System.Text;

using DawVcs.Application.Checkouts;
using DawVcs.Domain.Common;
using DawVcs.Domain.Hashing;
using DawVcs.Domain.Repositories;

namespace DawVcs.Application.Recovery;

public sealed record WorkspaceRecoveryInfo(string Id, string State, int Files, int StagedEntries);
public sealed record WorkspaceRecoveryResult(int RolledBack, int CommittedCleaned);

/// <summary>Explicit, restartable rollback of interrupted workspace publication; no force-overwrite mode.</summary>
public sealed class WorkspaceRecoveryUseCase
{
    private readonly Func<string, IRepositoryContext> _factory;
    private readonly Action<string>? _checkpoint;
    public WorkspaceRecoveryUseCase(Func<string, IRepositoryContext> factory) : this(factory, null) { }
    internal WorkspaceRecoveryUseCase(Func<string, IRepositoryContext> factory, Action<string>? checkpoint)
    { _factory = factory; _checkpoint = checkpoint; }

    public static Task<IReadOnlyList<WorkspaceRecoveryInfo>> InspectAsync(string repositoryDirectory, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var directories = Directories(Path.GetFullPath(repositoryDirectory));
        IReadOnlyList<WorkspaceRecoveryInfo> result = directories.Select(path =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var manifest = WorkspaceTransaction.Load(path);
            return new WorkspaceRecoveryInfo(manifest.Id, manifest.State, manifest.Files.Length, manifest.Staged.Length);
        }).ToArray();
        return Task.FromResult(result);
    }

    public async Task<WorkspaceRecoveryResult> RollbackAsync(string repositoryDirectory, CancellationToken cancellationToken = default)
    {
        var root = Path.GetFullPath(repositoryDirectory);
        cancellationToken.ThrowIfCancellationRequested();
        WorkspaceMutationGuard.Repository(root);
        var directories = Directories(root);
        if (directories.Length == 0) return new WorkspaceRecoveryResult(0, 0);
        if (directories.Length != 1)
            throw new WorkspaceRecoveryRequiredException("Multiple pending transactions require manual inspection; recovery will not guess their order.");
        using var ownership = WorkspaceTransaction.Own(root);
        var directory = directories[0];
        var manifest = WorkspaceTransaction.Load(directory);
        using var context = _factory(root);
        if (context.LoadConfig().RepositoryId.ToString() != manifest.RepositoryId)
            throw new WorkspaceRecoveryRequiredException("Recovery manifest belongs to another repository. Preserve it for inspection.");
        WorkspaceMutationGuard.Reference(root, BranchName.Parse(manifest.BeforeHead));
        WorkspaceMutationGuard.Reference(root, BranchName.Parse(manifest.AfterHead));
        if (manifest.UpdatedBranch is not null) WorkspaceMutationGuard.Reference(root, BranchName.Parse(manifest.UpdatedBranch));
        WorkspaceMutationGuard.Artifacts(root, manifest.Files.Select(f => f.Path));
        // Ref/HEAD writers cooperate with this existing lock. No nested context ref writes below.
        WorkspaceTransaction.GuardDirectory(Path.Combine(root, ".dawvc", "ref-write.lock"));
        using var refOwnership = new FileStream(Path.Combine(root, ".dawvc", "ref-write.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        var committed = manifest.State == "Committed";
        ValidateReferences(context, manifest, committed);
        // Preflight ALL targets and backup identities before the first recovery mutation.
        for (var index = 0; index < manifest.Files.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var file = manifest.Files[index];
            var target = Path.Combine(root, file.Path);
            if (Directory.Exists(target)) throw Conflict();
            var actual = File.Exists(target) ? (await Blake3ContentHasher.HashFileAsync(target, cancellationToken).ConfigureAwait(false)).ToString() : null;
            if (committed ? actual != file.After : actual != file.Before && actual != file.After) throw Conflict();
            if (!committed && file.Before is not null)
            {
                var backup = Backup(directory, index);
                WorkspaceTransaction.GuardDirectory(backup);
                if (!File.Exists(backup) || (await Blake3ContentHasher.HashFileAsync(backup, cancellationToken).ConfigureAwait(false)).ToString() != file.Before)
                    throw new WorkspaceRecoveryRequiredException("Recovery backup is missing or corrupt. No recovery writes were started; retain all recovery data.");
            }
        }
        var current = (await context.StagingIndex.GetStagedEntriesAsync(cancellationToken).ConfigureAwait(false)).Select(SavedStage.From).ToArray();
        if (committed ? current.Length != 0 : current.Any(entry => !manifest.Staged.Contains(entry))) throw Conflict();
        if (committed)
        {
            if (!WorkspaceTransaction.FinalizeDirectory(directory, _checkpoint)) throw new WorkspaceRecoveryRequiredException("Publication is committed but recovery metadata cleanup failed. Retain it and fix local permissions before retrying.");
            return new WorkspaceRecoveryResult(0, 1);
        }
        _checkpoint?.Invoke("recovery-preflighted");
        for (var index = manifest.Files.Length - 1; index >= 0; index--)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var file = manifest.Files[index];
            var target = Path.Combine(root, file.Path);
            await CheckCurrentAsync(target, file, cancellationToken).ConfigureAwait(false);
            if (file.Before is null)
            {
                if (File.Exists(target)) File.Delete(target);
            }
            else
            {
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                var temporary = Path.Combine(directory, "restore-" + Guid.NewGuid().ToString("N"));
                try
                {
                    await WorkspaceTransaction.CopyFlushedAsync(Backup(directory, index), temporary, cancellationToken).ConfigureAwait(false);
                    if ((await Blake3ContentHasher.HashFileAsync(temporary, cancellationToken).ConfigureAwait(false)).ToString() != file.Before)
                        throw new WorkspaceRecoveryRequiredException("Recovery backup changed during restoration; retain the transaction.");
                    await CheckCurrentAsync(target, file, cancellationToken).ConfigureAwait(false);
                    File.Move(temporary, target, overwrite: true);
                }
                finally { if (File.Exists(temporary)) File.Delete(temporary); }
            }
            _checkpoint?.Invoke("restored-file");
        }
        ValidateReferences(context, manifest, committed: false);
        if (manifest.UpdatedBranch is not null)
        {
            var reference = Path.Combine(root, ".dawvc", "refs", "heads", manifest.UpdatedBranch);
            if (manifest.BeforeTip is null) { if (File.Exists(reference)) File.Delete(reference); }
            else WorkspaceTransaction.WriteAtomic(reference, Encoding.UTF8.GetBytes(manifest.BeforeTip + "\n"));
        }
        WorkspaceTransaction.WriteAtomic(Path.Combine(root, ".dawvc", "HEAD"), Encoding.UTF8.GetBytes("ref: refs/heads/" + manifest.BeforeHead + "\n"));
        _checkpoint?.Invoke("restored-refs");
        current = (await context.StagingIndex.GetStagedEntriesAsync(cancellationToken).ConfigureAwait(false)).Select(SavedStage.From).ToArray();
        if (current.Any(entry => !manifest.Staged.Contains(entry))) throw Conflict();
        if (current.Length != manifest.Staged.Length || current.Any(entry => !manifest.Staged.Contains(entry)))
        {
            await context.StagingIndex.ClearStagedEntriesAsync(cancellationToken).ConfigureAwait(false);
            foreach (var entry in manifest.Staged)
            {
                await context.StagingIndex.StageEntryAsync(entry.Entry(), cancellationToken).ConfigureAwait(false);
                _checkpoint?.Invoke("restored-stage-entry");
            }
        }
        if (!WorkspaceTransaction.FinalizeDirectory(directory, _checkpoint)) throw new WorkspaceRecoveryRequiredException("Previous state restored, but recovery metadata cleanup failed. Retain it and retry after fixing permissions.");
        return new WorkspaceRecoveryResult(1, 0);
    }

    private static async Task CheckCurrentAsync(string path, SavedFile file, CancellationToken cancellationToken)
    {
        WorkspaceTransaction.GuardDirectory(path);
        if (Directory.Exists(path)) throw Conflict();
        var current = File.Exists(path) ? (await Blake3ContentHasher.HashFileAsync(path, cancellationToken).ConfigureAwait(false)).ToString() : null;
        if (current != file.Before && current != file.After) throw Conflict();
    }

    private static void ValidateReferences(IRepositoryContext context, WorkspaceManifest manifest, bool committed)
    {
        string head;
        try { head = context.GetCurrentBranch().Value; }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or ArgumentException) { throw Conflict(); }
        foreach (var name in new[] { manifest.BeforeHead, manifest.AfterHead, manifest.UpdatedBranch }.Where(name => name is not null).Distinct(StringComparer.Ordinal))
        {
            var reference = Path.Combine(context.RootPath, ".dawvc", "refs", "heads", name!);
            if (File.Exists(reference) && !CommitId.TryParse(File.ReadAllText(reference).Trim(), out _)) throw Conflict();
        }
        if (committed ? head != manifest.AfterHead : head != manifest.BeforeHead && head != manifest.AfterHead) throw Conflict();
        foreach (var name in new[] { manifest.BeforeHead, manifest.AfterHead }.Distinct(StringComparer.Ordinal))
        {
            if (name == manifest.UpdatedBranch) continue;
            var expected = name == manifest.BeforeHead ? manifest.BeforeHeadTip : manifest.AfterHeadTip;
            if (context.GetBranchCommit(BranchName.Parse(name))?.ToString() != expected) throw Conflict();
        }
        if (manifest.UpdatedBranch is not null)
        {
            var actual = context.GetBranchCommit(BranchName.Parse(manifest.UpdatedBranch))?.ToString();
            if (committed ? actual != manifest.AfterTip : actual != manifest.BeforeTip && actual != manifest.AfterTip) throw Conflict();
        }
    }

    private static string Backup(string directory, int index) => Path.Combine(directory, "files", index.ToString("D5", System.Globalization.CultureInfo.InvariantCulture));
    private static WorkspaceRecoveryRequiredException Conflict() => new("Recovery found unexpected workspace/ref/staging changes. No forced overwrite is available. Preserve your edits and all transaction backups, inspect them, then retry.");
    private static string[] Directories(string root)
    {
        if (!Directory.Exists(Path.Combine(root, ".dawvc")))
            throw new WorkspaceRecoveryRequiredException("Select an existing working repository for workspace recovery; bare storage has no workspace journal.");
        var path = Path.Combine(root, ".dawvc", "transactions");
        WorkspaceTransaction.GuardDirectory(path);
        if (!Directory.Exists(path)) return [];
        var entries = Directory.EnumerateFileSystemEntries(path).Take(3).ToArray();
        if (entries.Any(entry => !Directory.Exists(entry))) throw new WorkspaceRecoveryRequiredException("Unknown recovery metadata must be inspected before proceeding.");
        return entries;
    }
}
