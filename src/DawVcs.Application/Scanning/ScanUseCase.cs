using DawVcs.Adapters.Abstractions;
using DawVcs.Application.Adapters;
using DawVcs.Domain.Artifacts;
using DawVcs.Domain.Configuration;
using DawVcs.Domain.Repositories;

namespace DawVcs.Application.Scanning;

public sealed record ScanRequest(
    string WorkingDirectory,
    string? ExplicitArtifactPath = null,
    TimeSpan? Timeout = null);

public sealed record ScanResult(
    string ArtifactFullPath,
    ArtifactPath RelativeArtifactPath,
    ProjectDetectionResult Detection,
    bool RequiresOpaqueFallback,
    string? PrimaryDawName,
    TimeSpan Duration);

/// <summary>
/// Orchestreert de inspectie en validatie van DAW-projectbestanden met geïsoleerde adapter-executie en timeoutbeleid (FR-SCAN-001..007, IMP-0510).
/// </summary>
public sealed class ScanUseCase
{
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(5);

    private readonly Func<string, IRepositoryContext> _contextFactory;
    private readonly IDawAdapterRegistry _adapterRegistry;

    public ScanUseCase(
        Func<string, IRepositoryContext> contextFactory,
        IDawAdapterRegistry adapterRegistry)
    {
        _contextFactory = contextFactory ?? throw new ArgumentNullException(nameof(contextFactory));
        _adapterRegistry = adapterRegistry ?? throw new ArgumentNullException(nameof(adapterRegistry));
    }

    public async Task<ScanResult> ExecuteAsync(ScanRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var startTime = DateTimeOffset.UtcNow;
        var timeout = request.Timeout ?? DefaultTimeout;

        using var repo = _contextFactory(request.WorkingDirectory);

        string artifactFullPath;
        ArtifactPath relPath;

        if (!string.IsNullOrWhiteSpace(request.ExplicitArtifactPath))
        {
            artifactFullPath = Path.IsPathRooted(request.ExplicitArtifactPath)
                ? request.ExplicitArtifactPath
                : Path.GetFullPath(Path.Combine(request.WorkingDirectory, request.ExplicitArtifactPath));

            var relative = Path.GetRelativePath(request.WorkingDirectory, artifactFullPath);
            relPath = new ArtifactPath(relative);
        }
        else
        {
            RepositoryConfig? config = null;
            try
            {
                config = repo.LoadConfig();
            }
            catch
            {
                // Not an initialized repo or config not found
            }

            if (config != null)
            {
                relPath = config.PrimaryArtifact;
                artifactFullPath = Path.Combine(repo.RootPath, relPath.Value);
            }
            else
            {
                // Scan werkdirectory naar kandidaatproject
                var flpFiles = Directory.GetFiles(request.WorkingDirectory, "*.flp", SearchOption.TopDirectoryOnly);
                if (flpFiles.Length == 0)
                {
                    throw new FileNotFoundException($"Geen DAW-projectbestand (.flp) gevonden in '{request.WorkingDirectory}'.");
                }
                artifactFullPath = flpFiles[0];
                var relative = Path.GetRelativePath(request.WorkingDirectory, artifactFullPath);
                relPath = new ArtifactPath(relative);
            }
        }

        if (!File.Exists(artifactFullPath))
        {
            throw new FileNotFoundException($"Projectbestand niet gevonden op: '{artifactFullPath}'.", artifactFullPath);
        }

        var extension = Path.GetExtension(artifactFullPath);
        var adapter = _adapterRegistry.FindAdapterForExtension(extension);

        if (adapter == null)
        {
            var unknownResult = ProjectDetectionResult.Unknown(
                $"Geen DAW-adapter geregistreerd voor bestandsextensie '{extension}'. Project kan uitsluitend opaque worden beheerd.");

            return new ScanResult(
                artifactFullPath,
                relPath,
                unknownResult,
                RequiresOpaqueFallback: true,
                PrimaryDawName: null,
                DateTimeOffset.UtcNow - startTime);
        }

        // Bounded isolated execution via CancellationTokenSource timeout policy (IMP-0510)
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(timeout);

        ProjectDetectionResult detectionResult;
        try
        {
            using var readContext = new ArtifactReadContext(artifactFullPath);
            detectionResult = await adapter.DetectAsync(readContext, timeoutCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && timeoutCts.IsCancellationRequested)
        {
            // Timeout opgetreden in adapter inspectie
            detectionResult = ProjectDetectionResult.Invalid(
                adapter.DawName,
                $"Adapter inspectie van '{Path.GetFileName(artifactFullPath)}' is afgebroken wegens time-out ({timeout.TotalSeconds}s).");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Typed adapter error mapping (FR-SCAN-007)
            detectionResult = ProjectDetectionResult.Invalid(
                adapter.DawName,
                $"Fout tijdens uitvoeren van DAW-adapter '{adapter.DawName}': {ex.Message}");
        }

        var duration = DateTimeOffset.UtcNow - startTime;

        return new ScanResult(
            artifactFullPath,
            relPath,
            detectionResult,
            detectionResult.RequiresOpaqueFallback,
            adapter.DawName,
            duration);
    }
}
