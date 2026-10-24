using DawVcs.Application.Checkouts;
using DawVcs.Application.Diagnostics;
using DawVcs.Domain.Common;
using DawVcs.Domain.Dependencies;
using DawVcs.Domain.Remotes;
using DawVcs.Domain.Repositories;

namespace DawVcs.Application.Remotes;

public sealed record PullRequest(
    string RepositoryDirectory,
    string? RemoteName = null,
    string? Branch = null,
    bool Force = false,
    bool FfOnly = true,
    bool DryRun = false);

public sealed record DoctorReportSummary(
    double ReproducibilityScore,
    int RequiredPluginsCount,
    int InstalledPluginsCount,
    int MissingPluginsCount,
    int ResolvedAssetsCount,
    int MissingAssetsCount);

public sealed record PullResult(
    string RemoteName,
    BranchName Branch,
    SyncAncestryState State,
    CommitId? OldCommit,
    CommitId? NewCommit,
    int ObjectsFetched,
    long BytesFetched,
    IReadOnlyList<string> MaterializedFiles,
    string? RecoveryDirectory,
    DoctorReportSummary? DoctorSummary,
    string Message,
    bool IsDryRun);

/// <summary>
/// Orchestrates safe remote synchronization: fetches changes into quarantine,
/// enforces fast-forward ancestry constraints, materializes workspace updates via journal,
/// and produces a non-blocking health summary (FR-PUL-001..012, AC-030..AC-037).
/// </summary>
public sealed class PullUseCase
{
    private readonly Func<string, IRepositoryContext> _contextFactory;
    private readonly FetchUseCase _fetchUseCase;
    private readonly CheckoutUseCase _checkoutUseCase;
    private readonly AncestryService _ancestryService;
    private readonly DoctorUseCase? _doctorUseCase;

    public PullUseCase(
        Func<string, IRepositoryContext> contextFactory,
        FetchUseCase fetchUseCase,
        CheckoutUseCase checkoutUseCase,
        AncestryService? ancestryService = null,
        DoctorUseCase? doctorUseCase = null)
    {
        _contextFactory = contextFactory ?? throw new ArgumentNullException(nameof(contextFactory));
        _fetchUseCase = fetchUseCase ?? throw new ArgumentNullException(nameof(fetchUseCase));
        _checkoutUseCase = checkoutUseCase ?? throw new ArgumentNullException(nameof(checkoutUseCase));
        _ancestryService = ancestryService ?? new AncestryService();
        _doctorUseCase = doctorUseCase;
    }

    public async Task<PullResult> ExecuteAsync(PullRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.RepositoryDirectory);

        // 1. Fetch remote changes into quarantine & update remote-tracking refs (FR-PUL-001)
        var fetchResult = await _fetchUseCase.ExecuteAsync(
            new FetchRequest(request.RepositoryDirectory, request.RemoteName, request.Branch, request.DryRun),
            cancellationToken).ConfigureAwait(false);

        using var context = _contextFactory(request.RepositoryDirectory);
        var currentBranch = context.GetCurrentBranch();

        var targetBranch = !string.IsNullOrWhiteSpace(request.Branch)
            ? BranchName.Parse(request.Branch)
            : currentBranch;

        var remoteName = request.RemoteName;
        BranchName? remoteBranch = null;

        if (string.IsNullOrWhiteSpace(remoteName))
        {
            var (upstreamRemote, upstreamBranch) = context.GetUpstream(targetBranch);
            remoteName = !string.IsNullOrWhiteSpace(upstreamRemote) ? upstreamRemote : "origin";
            remoteBranch = upstreamBranch;
        }

        var finalRemoteBranch = remoteBranch ?? targetBranch;

        var localTip = context.GetBranchCommit(targetBranch);
        var remoteTip = context.GetRemoteTrackingCommit(remoteName, finalRemoteBranch);

        if (!remoteTip.HasValue)
        {
            throw new RemoteException($"No remote tracking branch '{remoteName}/{finalRemoteBranch.Value}' found. Ensure remote has advertised this branch.");
        }

        // 2. Classify sync ancestry state (FR-PUL-003..005)
        var classification = await _ancestryService.DetermineSyncStateAsync(
            localTip,
            remoteTip.Value,
            context,
            cancellationToken).ConfigureAwait(false);

        switch (classification.State)
        {
            case SyncAncestryState.UpToDate:
                return new PullResult(
                    remoteName,
                    targetBranch,
                    SyncAncestryState.UpToDate,
                    localTip,
                    remoteTip,
                    fetchResult.ObjectsFetched,
                    fetchResult.BytesFetched,
                    Array.Empty<string>(),
                    RecoveryDirectory: null,
                    DoctorSummary: null,
                    Message: "Already up to date.",
                    IsDryRun: request.DryRun);

            case SyncAncestryState.LocalAhead:
                return new PullResult(
                    remoteName,
                    targetBranch,
                    SyncAncestryState.LocalAhead,
                    localTip,
                    remoteTip,
                    fetchResult.ObjectsFetched,
                    fetchResult.BytesFetched,
                    Array.Empty<string>(),
                    RecoveryDirectory: null,
                    DoctorSummary: null,
                    Message: $"Local branch '{targetBranch.Value}' is ahead of '{remoteName}/{finalRemoteBranch.Value}' by {classification.CommitsAhead} commit(s). Nothing to pull.",
                    IsDryRun: request.DryRun);

            case SyncAncestryState.Diverged:
                // Fast-forward only guard (Exit Code 9, FR-PUL-003, AC-033)
                throw new RejectedNonFastForwardException(remoteName, targetBranch.Value, localTip!.Value, remoteTip.Value);

            case SyncAncestryState.FastForward:
                if (request.DryRun)
                {
                    return new PullResult(
                        remoteName,
                        targetBranch,
                        SyncAncestryState.FastForward,
                        localTip,
                        remoteTip,
                        fetchResult.ObjectsFetched,
                        fetchResult.BytesFetched,
                        Array.Empty<string>(),
                        RecoveryDirectory: null,
                        DoctorSummary: null,
                        Message: $"Would fast-forward '{targetBranch.Value}' from {localTip?.ToString()[..8] ?? "<none>"} to {remoteTip.Value.ToString()[..8]} ({classification.CommitsBehind} commit(s)).",
                        IsDryRun: true);
                }

                // 3. Fast-forward workspace update via PublicationJournal (FR-PUL-006..008, AC-030)
                var ffTarget = new FastForwardTarget(request.RepositoryDirectory, targetBranch, localTip, remoteTip.Value);
                var checkoutResult = await _checkoutUseCase.ExecuteFastForwardAsync(ffTarget, request.Force, cancellationToken).ConfigureAwait(false);

                // 4. Non-blocking Doctor summary (FR-PUL-009)
                DoctorReportSummary? doctorSummary = null;
                if (_doctorUseCase is not null)
                {
                    try
                    {
                        var docReport = await _doctorUseCase.ExecuteAsync(new DoctorRequest(request.RepositoryDirectory), cancellationToken).ConfigureAwait(false);
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
                        // Diagnostic evaluation must not block successful pull
                    }
                }

                return new PullResult(
                    remoteName,
                    targetBranch,
                    SyncAncestryState.FastForward,
                    localTip,
                    remoteTip,
                    fetchResult.ObjectsFetched,
                    fetchResult.BytesFetched,
                    checkoutResult.RestoredFiles,
                    checkoutResult.RecoveryDirectory,
                    doctorSummary,
                    Message: $"Successfully fast-forwarded '{targetBranch.Value}' to {remoteTip.Value.ToString()[..8]} ({classification.CommitsBehind} commit(s)).",
                    IsDryRun: false);

            default:
                throw new InvalidOperationException($"Unhandled sync state: {classification.State}");
        }
    }
}
