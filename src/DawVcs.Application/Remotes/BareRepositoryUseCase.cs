using DawVcs.Application.Common;
using DawVcs.Application.Exceptions;
using DawVcs.Domain.Common;
using DawVcs.Domain.Configuration;
using DawVcs.Domain.Remotes;
using DawVcs.Domain.Repositories;

namespace DawVcs.Application.Remotes;

public sealed record BareRepositoryRequest(string Source, string TargetDirectory, bool CopyHistory = false);

public sealed record BareRepositoryResult(string TargetDirectory, RepositoryConfig Configuration,
    int BranchesCopied, int ObjectsFetched, long BytesFetched, CommitId? HeadCommit);

/// <summary>Provision an empty matching receiver or a verified bare copy, without a checkout.</summary>
public sealed class BareRepositoryUseCase(
    Func<string, IRepositoryContext> contextFactory,
    IRemoteTransportFactory transportFactory,
    FetchUseCase fetchUseCase)
{
    public Task<BareRepositoryResult> ExecuteAsync(BareRepositoryRequest request, CancellationToken cancellationToken = default)
        => ExecuteAsync(request, cancellationToken, null);

    public async Task<BareRepositoryResult> ExecuteAsync(BareRepositoryRequest request,
        CancellationToken cancellationToken, IProgress<OperationProgress>? progress)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Source);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.TargetDirectory);
        cancellationToken.ThrowIfCancellationRequested();
        if (request.TargetDirectory.Contains("://", StringComparison.Ordinal))
            throw new ArgumentException("Bare destination must be a local filesystem path, not a URL.");
        var target = Path.TrimEndingDirectorySeparator(Path.GetFullPath(request.TargetDirectory));
        var source = request.Source.Trim();
        if (!source.Contains("://", StringComparison.Ordinal)) source = Path.GetFullPath(source);
        ValidateDestination(target);
        progress?.Report(new OperationProgress(OperationStage.ConnectingRemote));
        RepositoryConfig config;
        using (var transport = transportFactory.CreateTransport(source))
        {
            var handshake = await transport.HandshakeAsync(cancellationToken).ConfigureAwait(false);
            config = handshake.Configuration
                ?? throw new IncompatibleRemoteRepositoryException("Bare provisioning requires source configuration in the handshake.");
            if (handshake.SchemaVersion != RepositoryConfig.CurrentSchemaVersion || config.SchemaVersion != handshake.SchemaVersion
                || config.RepositoryId != handshake.RepositoryId)
                throw new IncompatibleRemoteRepositoryException("Source configuration identity or schema does not match its handshake.");
        }

        cancellationToken.ThrowIfCancellationRequested();
        var parent = Path.GetDirectoryName(target)!;
        Directory.CreateDirectory(parent);
        // Same-volume isolated staging: publish the whole bare layout with one directory rename.
        var staging = Path.Combine(parent, ".dawvc-provision-" + Guid.NewGuid().ToString("N"));
        var work = Path.Combine(staging, "work");
        var bare = Path.Combine(staging, "bare");
        var branches = 0;
        var objects = 0;
        long bytes = 0;
        CommitId? headCommit = null;
        try
        {
            Directory.CreateDirectory(work);
            progress?.Report(new OperationProgress(OperationStage.OpeningRepository));
            using (var context = contextFactory(work))
            {
                context.SaveConfig(config);
                Directory.CreateDirectory(Path.Combine(work, ".dawvc", "objects"));
                Directory.CreateDirectory(Path.Combine(work, ".dawvc", "refs", "heads"));
                context.SetCurrentBranch(config.DefaultBranch);
                if (request.CopyHistory)
                {
                    context.SaveRemote(new RemoteConfig(RemoteName.Origin, source));
                    var fetched = await fetchUseCase.ExecuteAsync(new FetchRequest(work, "origin"), cancellationToken,
                        progress is null ? null : new NestedOperationProgress(progress)).ConfigureAwait(false);
                    objects = fetched.ObjectsFetched;
                    bytes = fetched.BytesFetched;
                    // Publish exactly the validated refs from this fetch, not a second advertisement.
                    foreach (var reference in fetched.UpdatedRefs)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        await context.UpdateBranchCommitAsync(reference.Branch, reference.NewCommit, cancellationToken).ConfigureAwait(false);
                        branches++;
                        if (reference.Branch == config.DefaultBranch) headCommit = reference.NewCommit;
                    }
                }
            }

            Directory.Move(Path.Combine(work, ".dawvc"), bare);
            File.Move(Path.Combine(work, "dawvc.yaml"), Path.Combine(bare, "dawvc.yaml"));
            progress?.Report(new OperationProgress(OperationStage.UpdatingReferences));
            cancellationToken.ThrowIfCancellationRequested();
            ValidateDestination(target);
            if (Directory.Exists(target)) Directory.Delete(target); // Empty only; never recursively replace a destination.
            Directory.Move(bare, target);
        }
        finally
        {
            // A process crash or denied cleanup can leave this isolated staging directory.
            try { if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        progress?.Report(new OperationProgress(OperationStage.Completed));
        return new BareRepositoryResult(target, config, branches, objects, bytes, headCommit);
    }

    private static void ValidateDestination(string target)
    {
        if (Path.GetDirectoryName(target) is null) throw new ArgumentException("A filesystem root cannot be a bare destination.");
        for (var path = target; path is not null; path = Path.GetDirectoryName(path))
        {
            // LinkTarget also identifies dangling links, which must not be followed or replaced.
            if (new DirectoryInfo(path).LinkTarget is not null || new FileInfo(path).LinkTarget is not null
                || ((Directory.Exists(path) || File.Exists(path)) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0))
                throw new PathSecurityException(target, "Bare destination paths must not contain symbolic links or junctions.");
        }
        if (File.Exists(target) || (Directory.Exists(target) && Directory.EnumerateFileSystemEntries(target).Any()))
            throw new ArgumentException("Bare destination must be absent or an empty directory; existing repositories are never reinitialized.");
    }
}
