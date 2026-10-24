using System.Runtime.InteropServices;

using DawVcs.Adapters.Abstractions;
using DawVcs.Application.Adapters;
using DawVcs.Application.Common;
using DawVcs.Application.Dependencies;
using DawVcs.Domain.Dependencies;
using DawVcs.Domain.Entities;
using DawVcs.Domain.Repositories;

namespace DawVcs.Application.Diagnostics;

public sealed record DoctorRequest(string RepositoryDirectory);

/// <summary>
/// Voert een 3-domeinen integriteitsdiagnose uit: Artifact, Dependency en Environment (FR-DOC-001..010, TD §31).
/// </summary>
public sealed class DoctorUseCase
{
    private readonly Func<string, IRepositoryContext> _contextFactory;
    private readonly IDawAdapterRegistry? _adapterRegistry;

    public DoctorUseCase(
        Func<string, IRepositoryContext> contextFactory,
        IDawAdapterRegistry? adapterRegistry = null)
    {
        _contextFactory = contextFactory ?? throw new ArgumentNullException(nameof(contextFactory));
        _adapterRegistry = adapterRegistry;
    }

    public Task<DoctorReport> ExecuteAsync(DoctorRequest request, CancellationToken cancellationToken = default)
        => ExecuteAsync(request, cancellationToken, null);

    public async Task<DoctorReport> ExecuteAsync(DoctorRequest request, CancellationToken cancellationToken, IProgress<OperationProgress>? progress)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.RepositoryDirectory);

        cancellationToken.ThrowIfCancellationRequested();
        progress?.Report(new OperationProgress(OperationStage.OpeningRepository));
        DoctorReport report;
        using (var context = _contextFactory(request.RepositoryDirectory))
        {
            report = await ExecuteCoreAsync(context, cancellationToken, progress).ConfigureAwait(false);
        }
        cancellationToken.ThrowIfCancellationRequested();
        progress?.Report(new OperationProgress(OperationStage.Completed));
        return report;
    }

    private async Task<DoctorReport> ExecuteCoreAsync(IRepositoryContext context, CancellationToken cancellationToken, IProgress<OperationProgress>? progress)
    {
        progress?.Report(new OperationProgress(OperationStage.LoadingMetadata));
        var config = context.LoadConfig();

        var artifactHealthList = new List<ArtifactHealth>();
        var dependencyHealthList = new List<DependencyHealth>();
        ProjectDetectionResult? primaryDetection = null;
        IDawAdapter? primaryAdapter = null;

        // 1. Domein 1: Artifact Integrity
        var primaryRel = config.PrimaryArtifact.Value;
        var primaryFull = Path.Combine(context.RootPath, primaryRel);

        progress?.Report(new OperationProgress(OperationStage.InspectingArtifacts, 0, 1));
        if (!File.Exists(primaryFull))
        {
            artifactHealthList.Add(new ArtifactHealth(
                Path: primaryRel,
                DawName: "Unknown",
                DetectedVersion: null,
                Status: HealthStatus.Error,
                IsBlocking: true,
                Findings: [$"Primary project file '{primaryRel}' was not found in repository."]));
        }
        else
        {
            var ext = Path.GetExtension(primaryFull);
            var adapter = _adapterRegistry?.FindAdapterForExtension(ext);
            primaryAdapter = adapter;

            if (adapter == null)
            {
                artifactHealthList.Add(new ArtifactHealth(
                    Path: primaryRel,
                    DawName: "Unknown",
                    DetectedVersion: null,
                    Status: HealthStatus.Error,
                    IsBlocking: true,
                    Findings: [$"No registered adapter found for extension '{ext}'."]));
            }
            else
            {
                try
                {
                    await using var fs = new FileStream(primaryFull, FileMode.Open, FileAccess.Read, FileShare.Read);
                    var detectResult = await adapter.DetectAsync(fs, cancellationToken).ConfigureAwait(false);
                    primaryDetection = detectResult;

                    var healthStatus = detectResult.Status switch
                    {
                        ProjectDetectionStatus.Valid => HealthStatus.Healthy,
                        ProjectDetectionStatus.Suspicious => HealthStatus.Warning,
                        ProjectDetectionStatus.Unsupported => HealthStatus.Warning,
                        _ => HealthStatus.Error
                    };

                    var isBlocking = detectResult.Status == ProjectDetectionStatus.Invalid;

                    artifactHealthList.Add(new ArtifactHealth(
                        Path: primaryRel,
                        DawName: detectResult.DawName,
                        DetectedVersion: detectResult.DetectedVersion,
                        Status: healthStatus,
                        IsBlocking: isBlocking,
                        Findings: PathRedactor.RedactAll(detectResult.Findings)));
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    artifactHealthList.Add(new ArtifactHealth(
                        Path: primaryRel,
                        DawName: adapter.DawName,
                        DetectedVersion: null,
                        Status: HealthStatus.Error,
                        IsBlocking: true,
                        Findings: [$"Adapter inspection failed with error: {PathRedactor.Redact(ex.Message)}"]));
                }
            }
        }

        progress?.Report(new OperationProgress(OperationStage.InspectingArtifacts, 1, 1));

        // 2. Domein 2: Dependency Integrity & Reproduceerbaarheid
        if (primaryDetection != null)
        {
            ProjectSnapshot? headSnapshot = null;
            try
            {
                var currentBranch = context.GetCurrentBranch();
                var headCommitId = context.GetBranchCommit(currentBranch);
                if (headCommitId.HasValue)
                {
                    var commit = await context.LoadCommitAsync(headCommitId.Value, cancellationToken).ConfigureAwait(false);
                    if (commit != null)
                    {
                        headSnapshot = await context.LoadSnapshotAsync(commit.SnapshotId, cancellationToken).ConfigureAwait(false);
                    }
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // In uncommitted / newly initialized repositories, headSnapshot may not exist yet
            }

            progress?.Report(new OperationProgress(OperationStage.DiscoveringDependencies));
            var depGraph = await DependencyDiscoveryService.DiscoverAsync(
                context.RootPath,
                primaryDetection,
                primaryAdapter,
                cancellationToken).ConfigureAwait(false);

            var dependencies = depGraph.Dependencies;
            var resolvedDependencies = 0;
            progress?.Report(new OperationProgress(OperationStage.ResolvingDependencies, 0, dependencies.Count));
            foreach (var rawDep in dependencies)
            {
                var dep = rawDep;

                // Als er een gecommitte snapshot is, gebruik dan de gecommitteerde vereisten (hashes en plugin requirements)
                if (headSnapshot != null)
                {
                    if (dep is PluginDependency p)
                    {
                        var snapshotPlugin = headSnapshot.Dependencies.Dependencies
                            .OfType<PluginDependency>()
                            .FirstOrDefault(sp =>
                                sp.Plugin.Product.Equals(p.Plugin.Product, StringComparison.OrdinalIgnoreCase) &&
                                (string.IsNullOrWhiteSpace(sp.Location) || string.IsNullOrWhiteSpace(p.Location) || sp.Location.Equals(p.Location, StringComparison.OrdinalIgnoreCase)));

                        if (snapshotPlugin != null && !string.IsNullOrWhiteSpace(snapshotPlugin.VersionRequirement))
                        {
                            dep = p with { VersionRequirement = snapshotPlugin.VersionRequirement };
                        }
                    }
                    else if (dep is AssetDependency a && !a.Hash.HasValue)
                    {
                        var snapshotAsset = headSnapshot.Dependencies.Dependencies
                            .OfType<AssetDependency>()
                            .FirstOrDefault(sa => sa.Name.Equals(a.Name, StringComparison.OrdinalIgnoreCase));

                        if (snapshotAsset != null && snapshotAsset.Hash.HasValue)
                        {
                            dep = a with { Hash = snapshotAsset.Hash, FileSize = snapshotAsset.FileSize };
                        }
                    }
                }

                var binding = await DependencyResolverPipeline.ResolveAsync(
                    context.RootPath,
                    dep,
                    context.LocalBindings,
                    adapter: primaryAdapter,
                    cancellationToken: cancellationToken).ConfigureAwait(false);

                var isResolved = binding.Status == BindingStatus.Verified;
                var isBlocking = dep.Requirement == DependencyRequirement.Required && !isResolved;

                string? expectedVersion = null;
                string? detectedVersion = null;
                string? location = null;

                if (dep is PluginDependency plug)
                {
                    expectedVersion = plug.VersionRequirement;
                    location = plug.Location;
                    var (_, _, ver) = DependencyResolverPipeline.CheckPluginInstalled(plug, primaryAdapter);
                    detectedVersion = ver;
                }

                dependencyHealthList.Add(new DependencyHealth(
                    Id: dep.Id.Value,
                    Name: dep.Name,
                    Category: dep is AssetDependency ? "Asset" : "Plugin",
                    Requirement: dep.Requirement,
                    Status: binding.Status,
                    IsBlocking: isBlocking,
                    Locator: PathRedactor.Redact(binding.Locator),
                    Details: PathRedactor.Redact(binding.Notes),
                    ExpectedVersion: expectedVersion,
                    DetectedVersion: detectedVersion,
                    Location: location));
                resolvedDependencies++;
                progress?.Report(new OperationProgress(OperationStage.ResolvingDependencies, resolvedDependencies, dependencies.Count));
            }
        }

        double reproducibilityScore = 100.0;
        if (dependencyHealthList.Count > 0)
        {
            var resolvedCount = dependencyHealthList.Count(d => d.Status == BindingStatus.Verified);
            reproducibilityScore = Math.Round((double)resolvedCount / dependencyHealthList.Count * 100.0, 1);
        }

        // 3. Domein 3: Environment & DAW Installatie via geregistreerde DAW adapters
        var dawInstallations = new List<DawInstallation>();
        var envFindings = new List<string>();

        var adapters = _adapterRegistry?.GetAllAdapters() ?? Array.Empty<IDawAdapter>();
        var probedAdapters = 0;
        progress?.Report(new OperationProgress(OperationStage.ProbingEnvironment, 0, adapters.Count));
        foreach (var adapter in adapters)
        {
            var findings = await adapter.ProbeEnvironmentAsync(cancellationToken).ConfigureAwait(false);
            foreach (var f in findings)
            {
                if (f.IsInstalled && !string.IsNullOrWhiteSpace(f.InstallationPath))
                {
                    dawInstallations.Add(new DawInstallation(
                        DawName: f.DawName,
                        Version: f.Version ?? "Unknown",
                        ExecutablePath: PathRedactor.Redact(f.InstallationPath),
                        IsDetected: true));
                }
                else if (!string.IsNullOrWhiteSpace(f.StatusMessage))
                {
                    envFindings.Add(PathRedactor.Redact(f.StatusMessage));
                }
            }
            probedAdapters++;
            progress?.Report(new OperationProgress(OperationStage.ProbingEnvironment, probedAdapters, adapters.Count));
        }

        var envHealth = new EnvironmentHealth(
            OperatingSystem: RuntimeInformation.OSDescription,
            DotNetVersion: Environment.Version.ToString(),
            Architecture: RuntimeInformation.OSArchitecture.ToString(),
            DawInstallations: dawInstallations,
            Findings: envFindings);

        // Aggregatie
        var blockingCount = artifactHealthList.Count(a => a.IsBlocking) + dependencyHealthList.Count(d => d.IsBlocking);
        var warningsCount = artifactHealthList.Count(a => a.Status == HealthStatus.Warning)
            + dependencyHealthList.Count(d => !d.IsBlocking && d.Status != BindingStatus.Verified)
            + (envFindings.Count > 0 ? 1 : 0);

        return new DoctorReport(
            SchemaVersion: 1,
            IsHealthy: blockingCount == 0,
            HasWarnings: warningsCount > 0,
            ReproducibilityScore: reproducibilityScore,
            ArtifactHealth: artifactHealthList,
            DependencyHealth: dependencyHealthList,
            EnvironmentHealth: envHealth,
            BlockingIssuesCount: blockingCount,
            WarningsCount: warningsCount);
    }
}
