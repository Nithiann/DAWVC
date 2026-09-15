using DawVcs.Application.Common;
using DawVcs.Domain.Artifacts;
using DawVcs.Domain.Common;
using DawVcs.Domain.Configuration;
using DawVcs.Domain.Repositories;

namespace DawVcs.Application.Repositories;

public sealed record InitRequest(
    string TargetDirectory,
    string? ProjectName = null,
    string? PrimaryArtifactPath = null);

public sealed record InitResult(
    RepositoryId RepositoryId,
    string ProjectName,
    ArtifactPath PrimaryArtifact,
    BranchName DefaultBranch,
    bool WasAlreadyInitialized = false);

/// <summary>
/// Implements repository initialization use case (FR-REP-001 through FR-REP-010).
/// </summary>
public sealed class InitRepositoryUseCase : IUseCase<InitRequest, InitResult>
{
    private readonly Func<string, IRepositoryContext> _contextFactory;
    private readonly Adapters.IDawAdapterRegistry? _adapterRegistry;

    public InitRepositoryUseCase(
        Func<string, IRepositoryContext> contextFactory,
        Adapters.IDawAdapterRegistry? adapterRegistry = null)
    {
        _contextFactory = contextFactory;
        _adapterRegistry = adapterRegistry;
    }

    public Task<InitResult> ExecuteAsync(InitRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var targetDir = Path.GetFullPath(request.TargetDirectory);
        Directory.CreateDirectory(targetDir);

        var context = _contextFactory(targetDir);
        var dotDawvc = Path.Combine(targetDir, ".dawvc");
        var configPath = Path.Combine(targetDir, "dawvc.yaml");

        // Idempotency check: if valid repo already exists, return existing
        if (Directory.Exists(dotDawvc) && File.Exists(configPath))
        {
            try
            {
                var existingConfig = context.LoadConfig();
                var currentBranch = context.GetCurrentBranch();
                return Task.FromResult(new InitResult(
                    existingConfig.RepositoryId,
                    existingConfig.ProjectName,
                    existingConfig.PrimaryArtifact,
                    currentBranch,
                    WasAlreadyInitialized: true));
            }
            catch
            {
                // Re-initialize if corrupt
            }
        }

        var projectName = !string.IsNullOrWhiteSpace(request.ProjectName)
            ? request.ProjectName
            : Path.GetFileName(targetDir);

        ArtifactPath primaryArtifact;

        if (!string.IsNullOrWhiteSpace(request.PrimaryArtifactPath))
        {
            var explicitPath = request.PrimaryArtifactPath.Trim();
            var fullCandidatePath = Path.IsPathRooted(explicitPath)
                ? explicitPath
                : Path.Combine(targetDir, explicitPath);

            var relativeCandidate = Path.GetRelativePath(targetDir, fullCandidatePath).Replace('\\', '/');
            primaryArtifact = new ArtifactPath(relativeCandidate);

            if (!File.Exists(fullCandidatePath))
            {
                throw new FileNotFoundException($"Specified primary project artifact '{primaryArtifact.Value}' does not exist in '{targetDir}'.");
            }
        }
        else
        {
            // Auto-discovery of candidate project files via registered DAW adapters (FR-REP-009 / FR-REP-010)
            IReadOnlyList<string> candidateFiles;
            if (_adapterRegistry != null)
            {
                candidateFiles = _adapterRegistry.FindCandidateProjectFiles(targetDir);
            }
            else
            {
                candidateFiles = Directory.GetFiles(targetDir, "*.flp", SearchOption.TopDirectoryOnly);
            }

            if (candidateFiles.Count == 1)
            {
                primaryArtifact = new ArtifactPath(Path.GetFileName(candidateFiles[0]));
            }
            else if (candidateFiles.Count > 1)
            {
                throw new InvalidOperationException(
                    $"Multiple project files found in '{targetDir}'. Please specify the primary artifact explicitly with --primary <file>.");
            }
            else
            {
                throw new InvalidOperationException(
                    $"No DAW project file found in '{targetDir}'. Please create a project or specify the primary artifact explicitly with --primary <file>.");
            }
        }

        var repositoryId = RepositoryId.New();
        var config = new RepositoryConfig(
            repositoryId,
            projectName,
            primaryArtifact,
            BranchName.Main);

        context.SaveConfig(config);
        context.SetCurrentBranch(BranchName.Main);

        return Task.FromResult(new InitResult(
            repositoryId,
            projectName,
            primaryArtifact,
            BranchName.Main,
            WasAlreadyInitialized: false));
    }
}
