using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json;
using System.Text.Json.Serialization;

using DawVcs.Application.Checkouts;
using DawVcs.Application.Exceptions;
using DawVcs.Domain.Artifacts;
using DawVcs.Domain.Checkouts;
using DawVcs.Domain.Common;
using DawVcs.Domain.Hashing;
using DawVcs.Domain.Repositories;
using DawVcs.Domain.Storage;

namespace DawVcs.Application.Recovery;

internal sealed record SavedStage(string Path, string Blob, string Hash, long Size, int Role)
{
    public static SavedStage From(ArtifactEntry entry) => new(entry.Path.Value, entry.Blob.ToString(), entry.Hash.ToString(), entry.Size, (int)entry.Role);
    public ArtifactEntry Entry() => new(new ArtifactPath(Path), BlobId.Parse(Blob), ContentHash.Parse(Hash), Size, (ArtifactRole)Role);
}

internal sealed record SavedFile(string Path, string? Before, string? After);
internal sealed record WorkspaceManifest(int SchemaVersion, string Id, string RepositoryId, string State,
    string BeforeHead, string? BeforeHeadTip, string AfterHead, string? AfterHeadTip,
    string? UpdatedBranch, string? BeforeTip, string? AfterTip, SavedFile[] Files, SavedStage[] Staged);

/// <summary>Write-ahead recovery data. Disposal releases ownership; it never silently rolls back live state.</summary>
internal sealed class WorkspaceTransaction : IAsyncDisposable
{
    internal static readonly JsonSerializerOptions JsonOptions = new() { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow };
    private readonly string _root;
    private readonly FileStream _ownership;
    private readonly string _directory;
    private WorkspaceManifest _manifest;
    private bool _terminal;
    private readonly Action<string>? _checkpoint;

    private WorkspaceTransaction(string root, string directory, FileStream ownership, WorkspaceManifest manifest, Action<string>? checkpoint)
    { _root = root; _directory = directory; _ownership = ownership; _manifest = manifest; _checkpoint = checkpoint; }

    public static async Task<WorkspaceTransaction> PrepareAsync(IRepositoryContext context, string staging,
        CheckoutPlan plan, BranchName afterHead, BranchName? updatedBranch, CommitId? beforeTip, CommitId? afterTip,
        IReadOnlyList<ArtifactEntry> staged, Action<string>? checkpoint, CancellationToken cancellationToken)
    {
        var root = context.RootPath;
        WorkspaceRecoveryGuard.ThrowIfPending(root);
        WorkspaceMutationGuard.Repository(root);
        WorkspaceMutationGuard.Artifacts(root, plan.Actions.Select(a => a.Path.Value));
        var ownership = Own(root);
        string? preparation = null;
        try
        {
            WorkspaceRecoveryGuard.ThrowIfPending(root);
            var id = Guid.NewGuid().ToString("N");
            preparation = Path.Combine(root, ".dawvc", "transaction-preparation", id);
            GuardDirectory(preparation);
            CreatePrivateDirectory(preparation);
            Directory.CreateDirectory(Path.Combine(preparation, "files"));
            var files = new List<SavedFile>();
            foreach (var action in plan.Actions.Where(a => a.Kind != CheckoutActionKind.Unchanged))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (files.Count >= 10_000) throw new InvalidEnvelopeException("Workspace transaction exceeds its file limit.");
                var target = Path.Combine(root, action.Path.Value);
                if (Directory.Exists(target)) throw new WorkspaceRecoveryRequiredException("A publication file path is an existing directory.");
                var before = File.Exists(target) ? (await Blake3ContentHasher.HashFileAsync(target, cancellationToken).ConfigureAwait(false)).ToString() : null;
                var after = action.Kind == CheckoutActionKind.Delete ? null : action.TargetHash?.ToString()
                    ?? throw new InvalidEnvelopeException("Missing publication target hash.");
                if (before is not null)
                {
                    var backup = Path.Combine(preparation, "files", files.Count.ToString("D5", System.Globalization.CultureInfo.InvariantCulture));
                    await CopyFlushedAsync(target, backup, cancellationToken).ConfigureAwait(false);
                    if ((await Blake3ContentHasher.HashFileAsync(backup, cancellationToken).ConfigureAwait(false)).ToString() != before)
                        throw new WorkspaceRecoveryRequiredException("Workspace changed while its recovery backup was being prepared.");
                }
                if (after is not null)
                {
                    var candidate = Path.Combine(staging, action.Path.Value);
                    if ((await Blake3ContentHasher.HashFileAsync(candidate, cancellationToken).ConfigureAwait(false)).ToString() != after)
                        throw new InvalidEnvelopeException("Publication candidate changed during recovery preparation.");
                    using var file = new FileStream(candidate, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
                    file.Flush(flushToDisk: true);
                }
                files.Add(new SavedFile(action.Path.Value, before, after));
            }
            var head = context.GetCurrentBranch();
            foreach (var branch in new[] { head, afterHead, updatedBranch ?? head }.Distinct())
            {
                WorkspaceMutationGuard.Reference(root, branch);
                var reference = Path.Combine(root, ".dawvc", "refs", "heads", branch.Value);
                if (File.Exists(reference) && !CommitId.TryParse(File.ReadAllText(reference).Trim(), out _))
                    throw new InvalidEnvelopeException("Cannot prepare recovery for a corrupt branch ref.");
            }
            var manifest = new WorkspaceManifest(1, id, context.LoadConfig().RepositoryId.ToString(), "Prepared",
                head.Value, context.GetBranchCommit(head)?.ToString(), afterHead.Value,
                updatedBranch == afterHead ? afterTip?.ToString() : context.GetBranchCommit(afterHead)?.ToString(),
                updatedBranch?.Value, beforeTip?.ToString(), afterTip?.ToString(), files.ToArray(), staged.Select(SavedStage.From).ToArray());
            Validate(manifest, id);
            checkpoint?.Invoke("backup-prepared");
            Save(preparation, manifest);
            var directory = Path.Combine(root, ".dawvc", "transactions", id);
            GuardDirectory(directory);
            Directory.CreateDirectory(Path.GetDirectoryName(directory)!);
            Directory.Move(preparation, directory);
            preparation = null;
            var transaction = new WorkspaceTransaction(root, directory, ownership, manifest, checkpoint);
            checkpoint?.Invoke("prepared");
            return transaction;
        }
        catch
        {
            ownership.Dispose();
            if (preparation is not null) TryCleanup(preparation);
            throw;
        }
    }

    public void Commit()
    {
        _manifest = _manifest with { State = "Committed" };
        Save(_directory, _manifest);
        _terminal = true;
        _checkpoint?.Invoke("committed");
    }

    public void RollbackConfirmed()
    {
        foreach (var file in _manifest.Files.Where(file => file.Before is not null))
        {
            using var restored = new FileStream(Path.Combine(_root, file.Path), FileMode.Open, FileAccess.ReadWrite, FileShare.Read);
            restored.Flush(flushToDisk: true);
        }
        _terminal = true;
    }

    public ValueTask DisposeAsync()
    {
        try
        {
            if (_terminal && !FinalizeDirectory(_directory, _checkpoint))
                throw new WorkspaceRecoveryRequiredException("Publication or rollback finished, but recovery-envelope finalization failed. Preserve it, inspect log/recover, and resolve recovery before modifying the workspace.");
        }
        finally { _ownership.Dispose(); }
        return ValueTask.CompletedTask;
    }

    internal static FileStream Own(string root)
    {
        var metadata = Path.Combine(Path.GetFullPath(root), ".dawvc");
        GuardDirectory(Path.Combine(metadata, "workspace-recovery.lock"));
        Directory.CreateDirectory(metadata);
        try { return new FileStream(Path.Combine(metadata, "workspace-recovery.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
        catch (IOException) { throw new WorkspaceRecoveryRequiredException("Workspace publication/recovery is active in another process. Await it before recovery."); }
    }

    private static void CreatePrivateDirectory(string directory)
    {
        if (OperatingSystem.IsWindows())
        {
            Directory.CreateDirectory(directory);
            using var identity = WindowsIdentity.GetCurrent();
            var owner = identity.User ?? throw new UnauthorizedAccessException("A recovery backup owner is required.");
            var security = new DirectorySecurity();
            security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
            security.AddAccessRule(new FileSystemAccessRule(owner, FileSystemRights.FullControl,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
            new DirectoryInfo(directory).SetAccessControl(security);
        }
        else Directory.CreateDirectory(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
    }

    internal static void GuardDirectory(string path)
    {
        for (var current = Path.GetFullPath(path); current is not null; current = Path.GetDirectoryName(current))
            if (new DirectoryInfo(current).LinkTarget is not null || new FileInfo(current).LinkTarget is not null
                || ((File.Exists(current) || Directory.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0))
                throw new WorkspaceRecoveryRequiredException("Recovery paths must not contain links or junctions.");
    }

    internal static WorkspaceManifest Load(string directory)
    {
        GuardDirectory(directory);
        GuardDirectory(Path.Combine(directory, "manifest.json"));
        try
        {
            var path = Path.Combine(directory, "manifest.json");
            if (new FileInfo(path).Length > 16 * 1024 * 1024) throw new InvalidEnvelopeException("Recovery manifest exceeds its size limit.");
            var manifest = JsonSerializer.Deserialize<WorkspaceManifest>(File.ReadAllBytes(path), JsonOptions)
                ?? throw new InvalidEnvelopeException("Missing recovery manifest.");
            Validate(manifest, Path.GetFileName(directory));
            return manifest;
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException or IOException or InvalidEnvelopeException or PathSecurityException)
        { throw new WorkspaceRecoveryRequiredException("Recovery metadata is invalid or unreadable. Preserve the transaction and backups for inspection."); }
    }

    internal static void Validate(WorkspaceManifest manifest, string id)
    {
        if (manifest.SchemaVersion != 1 || !Guid.TryParseExact(id, "N", out _) || manifest.Id != id
            || manifest.State is not ("Prepared" or "Committed") || manifest.Files is null || manifest.Staged is null
            || manifest.Files.Length > 10_000 || manifest.Staged.Length > 10_000
            || !RepositoryId.TryParse(manifest.RepositoryId, out _) || manifest.Files.Any(f => f is null) || manifest.Staged.Any(s => s is null))
            throw new InvalidEnvelopeException("Invalid recovery manifest shape/version.");
        _ = BranchName.Parse(manifest.BeforeHead); _ = BranchName.Parse(manifest.AfterHead);
        foreach (var tip in new[] { manifest.BeforeHeadTip, manifest.AfterHeadTip, manifest.BeforeTip, manifest.AfterTip })
            if (tip is not null) _ = CommitId.Parse(tip);
        if (manifest.UpdatedBranch is not null)
        {
            _ = BranchName.Parse(manifest.UpdatedBranch);
            if (manifest.AfterTip is null || manifest.AfterHead != manifest.BeforeHead
                || (manifest.UpdatedBranch == manifest.BeforeHead && (manifest.BeforeTip != manifest.BeforeHeadTip || manifest.AfterTip != manifest.AfterHeadTip))
                || (manifest.UpdatedBranch != manifest.BeforeHead && manifest.BeforeHeadTip != manifest.AfterHeadTip))
                throw new InvalidEnvelopeException("Inconsistent recovery ref update.");
        }
        else if (manifest.BeforeTip is not null || manifest.AfterTip is not null
            || (manifest.BeforeHead == manifest.AfterHead && manifest.BeforeHeadTip != manifest.AfterHeadTip))
            throw new InvalidEnvelopeException("Unexpected recovery ref data.");
        PathSecurityGuard.ValidateAll(manifest.Files.Select(f => f.Path));
        PathSecurityGuard.ValidateAll(manifest.Staged.Select(s => s.Path));
        foreach (var file in manifest.Files)
            foreach (var hash in new[] { file.Before, file.After })
                if (hash is not null) _ = ContentHash.Parse(hash);
        foreach (var entry in manifest.Staged)
            if (entry.Size < 0 || !Enum.IsDefined((ArtifactRole)entry.Role) || entry.Entry().Blob.Value != entry.Entry().Hash)
                throw new InvalidEnvelopeException("Invalid saved staging entry.");
    }

    private static void Save(string directory, WorkspaceManifest manifest)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(manifest, JsonOptions);
        if (bytes.Length > 16 * 1024 * 1024) throw new InvalidEnvelopeException("Recovery manifest exceeds its size limit.");
        WriteAtomic(Path.Combine(directory, "manifest.json"), bytes);
    }

    internal static void WriteAtomic(string path, byte[] bytes)
    {
        GuardDirectory(path);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + ".recovery-" + Guid.NewGuid().ToString("N");
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            { stream.Write(bytes); stream.Flush(flushToDisk: true); }
            File.Move(temporary, path, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    internal static async Task CopyFlushedAsync(string source, string target, CancellationToken cancellationToken)
    {
        await using var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read);
        var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None };
        if (!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        await using var output = new FileStream(target, options);
        await input.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
        await output.FlushAsync(cancellationToken).ConfigureAwait(false);
        output.Flush(flushToDisk: true);
    }

    internal static void RemoveUnbornReference(string root, BranchName branch)
    {
        WorkspaceMutationGuard.Reference(root, branch);
        using var ownership = new FileStream(Path.Combine(root, ".dawvc", "ref-write.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        File.Delete(Path.Combine(root, ".dawvc", "refs", "heads", branch.Value));
    }

    internal static bool FinalizeDirectory(string directory, Action<string>? checkpoint = null)
    {
        try
        {
            if (!Directory.Exists(directory)) return true;
            var cleanup = Path.Combine(Directory.GetParent(directory)!.Parent!.FullName, "transaction-cleanup", Path.GetFileName(directory));
            GuardDirectory(cleanup);
            Directory.CreateDirectory(Path.GetDirectoryName(cleanup)!);
            Directory.Move(directory, cleanup);
            checkpoint?.Invoke("terminal-moved");
            TryCleanup(cleanup); // A crash here leaves terminal garbage, never a half-deleted active manifest.
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return false; }
    }

    internal static bool TryCleanup(string directory)
    {
        try { if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true); return true; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return false; }
    }
}
