using DawVcs.Application.Checkouts;
using DawVcs.Application.Common;
using DawVcs.Application.Diagnostics;
using DawVcs.Domain.Artifacts;
using DawVcs.Domain.Common;
using DawVcs.Domain.Configuration;
using DawVcs.Domain.Dependencies;
using DawVcs.Domain.Remotes;
using DawVcs.Domain.Repositories;

namespace DawVcs.Application.Remotes;

public sealed record CloneRequest(
    string Url,
    string? TargetDirectory = null,
    bool Force = false);

public sealed record CloneResult(
    string TargetDirectory,
    string ProjectName,
    BranchName DefaultBranch,
    CommitId? HeadCommit,
    int ObjectsFetched,
    long BytesFetched,
    IReadOnlyList<string> MaterializedFiles,
    DoctorReportSummary? DoctorSummary);

/// <summary>
/// Clones a remote repository into a new local directory, configures 'origin',
/// fetches all remote branches, and materializes the default branch (FR-REM-001..012, FR-PUL-001..012).
/// </summary>
public sealed class CloneUseCase
{
    private readonly Func<string, IRepositoryContext> _contextFactory;
    private readonly IRemoteTransportFactory _transportFactory;
    private readonly FetchUseCase _fetchUseCase;
    private readonly CheckoutUseCase _checkoutUseCase;
    private readonly DoctorUseCase? _doctorUseCase;

    public CloneUseCase(
        Func<string, IRepositoryContext> contextFactory,
        IRemoteTransportFactory transportFactory,
        FetchUseCase fetchUseCase,
        CheckoutUseCase checkoutUseCase,
        DoctorUseCase? doctorUseCase = null)
    {
        _contextFactory = contextFactory ?? throw new ArgumentNullException(nameof(contextFactory));
        _transportFactory = transportFactory ?? throw new ArgumentNullException(nameof(transportFactory));
        _fetchUseCase = fetchUseCase ?? throw new ArgumentNullException(nameof(fetchUseCase));
        _checkoutUseCase = checkoutUseCase ?? throw new ArgumentNullException(nameof(checkoutUseCase));
        _doctorUseCase = doctorUseCase;
    }

    public Task<CloneResult> ExecuteAsync(CloneRequest request, CancellationToken cancellationToken = default)
        => ExecuteAsync(request, cancellationToken, null);

    public async Task<CloneResult> ExecuteAsync(
        CloneRequest request, CancellationToken cancellationToken, IProgress<OperationProgress>? progress)
    {
        var result = await ExecuteCoreAsync(request, cancellationToken, progress).ConfigureAwait(false);
        progress?.Report(new OperationProgress(OperationStage.Completed));
        return result;
    }

    private async Task<CloneResult> ExecuteCoreAsync(
        CloneRequest request, CancellationToken cancellationToken, IProgress<OperationProgress>? progress)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Url);

        cancellationToken.ThrowIfCancellationRequested();
        var nestedProgress = progress is null ? null : new NestedOperationProgress(progress);
        progress?.Report(new OperationProgress(OperationStage.ConnectingRemote));
        var remoteUrl = request.Url.Trim();
        using var transport = _transportFactory.CreateTransport(remoteUrl);

        // 1. Handshake to ensure remote exists and is compatible
        var handshake = await transport.HandshakeAsync(cancellationToken).ConfigureAwait(false);
        if (handshake.SchemaVersion > RepositoryConfig.CurrentSchemaVersion)
        {
            throw new RemoteException($"Remote schemaVersion '{handshake.SchemaVersion}' is newer than supported version '{RepositoryConfig.CurrentSchemaVersion}'.");
        }

        // 2. Read remote config from remote transport or path
        progress?.Report(new OperationProgress(OperationStage.LoadingMetadata));
        RepositoryConfig remoteConfig = ReadRemoteConfig(remoteUrl, handshake.RepositoryId);

        // 3. Determine target directory
        var targetDir = !string.IsNullOrWhiteSpace(request.TargetDirectory)
            ? Path.GetFullPath(request.TargetDirectory)
            : DeriveTargetDirectory(remoteUrl, remoteConfig.ProjectName);

        if (Directory.Exists(targetDir) && Directory.EnumerateFileSystemEntries(targetDir).Any() && !request.Force)
        {
            throw new InvalidOperationException($"Destination path '{targetDir}' already exists and is not an empty directory.");
        }

        progress?.Report(new OperationProgress(OperationStage.OpeningRepository));
        Directory.CreateDirectory(targetDir);
        Directory.CreateDirectory(Path.Combine(targetDir, ".dawvc"));
        Directory.CreateDirectory(Path.Combine(targetDir, ".dawvc", "objects"));
        Directory.CreateDirectory(Path.Combine(targetDir, ".dawvc", "refs", "heads"));
        Directory.CreateDirectory(Path.Combine(targetDir, ".dawvc", "refs", "remotes"));

        // 4. Write local config & HEAD
        using var context = _contextFactory(targetDir);
        progress?.Report(new OperationProgress(OperationStage.UpdatingReferences));
        context.SaveConfig(remoteConfig);

        var defaultBranch = remoteConfig.DefaultBranch;
        var headPath = Path.Combine(targetDir, ".dawvc", "HEAD");
        await File.WriteAllTextAsync(headPath, $"ref: refs/heads/{defaultBranch.Value}\n", cancellationToken).ConfigureAwait(false);

        // 5. Configure 'origin' remote & upstream
        context.SaveRemote(new RemoteConfig(RemoteName.Origin, remoteUrl));
        context.SetUpstream(defaultBranch, "origin", defaultBranch);

        // 6. Fetch all remote branches into quarantine & objects
        var fetchRequest = new FetchRequest(targetDir, "origin");
        var fetchResult = nestedProgress is null
            ? await _fetchUseCase.ExecuteAsync(fetchRequest, cancellationToken).ConfigureAwait(false)
            : await _fetchUseCase.ExecuteAsync(fetchRequest, cancellationToken, nestedProgress).ConfigureAwait(false);

        // 7. Initial checkout of default branch
        var trackingCommit = context.GetRemoteTrackingCommit("origin", defaultBranch);
        var materializedFiles = new List<string>();

        if (trackingCommit.HasValue)
        {
            var target = new FastForwardTarget(targetDir, defaultBranch, null, trackingCommit.Value);
            var checkoutResult = nestedProgress is null
                ? await _checkoutUseCase.ExecuteFastForwardAsync(target, force: true, cancellationToken).ConfigureAwait(false)
                : await _checkoutUseCase.ExecuteFastForwardAsync(target, true, cancellationToken, nestedProgress).ConfigureAwait(false);
            materializedFiles.AddRange(checkoutResult.RestoredFiles);
        }

        // 8. Non-blocking Doctor summary
        DoctorReportSummary? doctorSummary = null;
        if (_doctorUseCase is not null)
        {
            progress?.Report(new OperationProgress(OperationStage.ProbingEnvironment));
            try
            {
                var docReport = await _doctorUseCase.ExecuteAsync(new DoctorRequest(targetDir), cancellationToken).ConfigureAwait(false);
                var plugins = docReport.DependencyHealth.Where(d => d.Category.Equals("Plugin", StringComparison.OrdinalIgnoreCase)).ToList();
                var assets = docReport.DependencyHealth.Where(d => d.Category.Equals("Asset", StringComparison.OrdinalIgnoreCase)).ToList();

                doctorSummary = new DoctorReportSummary(
                    docReport.ReproducibilityScore,
                    RequiredPluginsCount: plugins.Count,
                    InstalledPluginsCount: plugins.Count(p => p.Status == BindingStatus.Verified),
                    MissingPluginsCount: plugins.Count(p => p.Status != BindingStatus.Verified),
                    ResolvedAssetsCount: assets.Count(a => a.Status == BindingStatus.Verified),
                    MissingAssetsCount: assets.Count(a => a.Status != BindingStatus.Verified));
            }
            catch
            {
                // Non-blocking doctor report
            }
        }

        return new CloneResult(
            targetDir,
            remoteConfig.ProjectName,
            defaultBranch,
            trackingCommit,
            fetchResult.ObjectsFetched,
            fetchResult.BytesFetched,
            materializedFiles,
            doctorSummary);
    }

    private RepositoryConfig ReadRemoteConfig(string remoteUrl, RepositoryId fallbackRepoId)
    {
        var path = remoteUrl.Trim();
        if (path.StartsWith("file://", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                path = new Uri(path).LocalPath;
            }
            catch
            {
                // Fallback to raw path if URI parsing fails
            }
        }

        try
        {
            using var remoteContext = _contextFactory(path);
            var loaded = remoteContext?.LoadConfig();
            if (loaded != null)
            {
                return loaded;
            }
        }
        catch
        {
            // Fallback default config if remote dawvc.yaml was not directly loadable via context
        }

        var leaf = ExtractLeafName(path);
        var projectName = Path.GetFileNameWithoutExtension(leaf);
        if (string.IsNullOrWhiteSpace(projectName)) projectName = "Project";
        return new RepositoryConfig(fallbackRepoId, projectName, new ArtifactPath($"{projectName}.flp"), BranchName.Main);
    }

    private static string DeriveTargetDirectory(string remoteUrl, string projectName)
    {
        var leaf = ExtractLeafName(remoteUrl);
        if (leaf.EndsWith(".dawvc", StringComparison.OrdinalIgnoreCase) || leaf.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
        {
            leaf = Path.GetFileNameWithoutExtension(leaf);
        }

        if (string.IsNullOrWhiteSpace(leaf))
        {
            leaf = projectName;
        }

        return Path.Combine(Directory.GetCurrentDirectory(), leaf);
    }

    private static string ExtractLeafName(string path)
    {
        var trimmed = path.Trim().TrimEnd('/', '\\');
        int lastSeparator = Math.Max(trimmed.LastIndexOf('/'), trimmed.LastIndexOf('\\'));
        return lastSeparator >= 0 ? trimmed[(lastSeparator + 1)..] : trimmed;
    }
}
