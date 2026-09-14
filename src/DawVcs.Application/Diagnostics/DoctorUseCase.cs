using System.Runtime.InteropServices;

using DawVcs.Adapters.Abstractions;
using DawVcs.Application.Adapters;
using DawVcs.Application.Dependencies;
using DawVcs.Domain.Dependencies;
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

    public async Task<DoctorReport> ExecuteAsync(DoctorRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.RepositoryDirectory);

        var context = _contextFactory(request.RepositoryDirectory);
        var config = context.LoadConfig();

        var artifactHealthList = new List<ArtifactHealth>();
        var dependencyHealthList = new List<DependencyHealth>();
        ProjectDetectionResult? primaryDetection = null;

        // 1. Domein 1: Artifact Integrity
        var primaryRel = config.PrimaryArtifact.Value;
        var primaryFull = Path.Combine(context.RootPath, primaryRel);

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
                        Findings: detectResult.Findings));
                }
                catch (Exception ex)
                {
                    artifactHealthList.Add(new ArtifactHealth(
                        Path: primaryRel,
                        DawName: adapter.DawName,
                        DetectedVersion: null,
                        Status: HealthStatus.Error,
                        IsBlocking: true,
                        Findings: [$"Adapter inspection failed with error: {ex.Message}"]));
                }
            }
        }

        // 2. Domein 2: Dependency Integrity & Reproduceerbaarheid
        if (primaryDetection != null)
        {
            var depGraph = await DependencyDiscoveryService.DiscoverAsync(
                context.RootPath,
                primaryDetection,
                cancellationToken).ConfigureAwait(false);

            foreach (var dep in depGraph.Dependencies)
            {
                var binding = await DependencyResolverPipeline.ResolveAsync(
                    context.RootPath,
                    dep,
                    context.LocalBindings,
                    cancellationToken: cancellationToken).ConfigureAwait(false);

                var isResolved = binding.Status == BindingStatus.Verified;
                var isBlocking = dep.Requirement == DependencyRequirement.Required && !isResolved;

                dependencyHealthList.Add(new DependencyHealth(
                    Id: dep.Id.Value,
                    Name: dep.Name,
                    Category: dep is AssetDependency ? "Asset" : "Plugin",
                    Requirement: dep.Requirement,
                    Status: binding.Status,
                    IsBlocking: isBlocking,
                    Locator: binding.Locator,
                    Details: binding.Notes));
            }
        }

        double reproducibilityScore = 100.0;
        if (dependencyHealthList.Count > 0)
        {
            var resolvedCount = dependencyHealthList.Count(d => d.Status == BindingStatus.Verified);
            reproducibilityScore = Math.Round((double)resolvedCount / dependencyHealthList.Count * 100.0, 1);
        }

        // 3. Domein 3: Environment & DAW Installatie
        var dawInstallations = FlStudioDetector.Detect();
        var envFindings = new List<string>();
        if (dawInstallations.Count == 0 && RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            envFindings.Add("No standard FL Studio installation detected in default paths or registry.");
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
