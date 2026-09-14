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

    public InitRepositoryUseCase(Func<string, IRepositoryContext> contextFactory)
    {
        _contextFactory = contextFactory;
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
                return Task.FromResult(new InitResult(
                    existingConfig.RepositoryId,
                    existingConfig.ProjectName,
                    existingConfig.PrimaryArtifact,
                    existingConfig.DefaultBranch,
                    WasAlreadyInitialized: true));
            }
            catch
            {
                // Re-initialize if corrupt
            }
        }

        // Determine project name
        var projectName = !string.IsNullOrWhiteSpace(request.ProjectName)
            ? request.ProjectName.Trim()
            : new DirectoryInfo(targetDir).Name;

        // Determine primary artifact
        ArtifactPath primaryArtifact;
        if (!string.IsNullOrWhiteSpace(request.PrimaryArtifactPath))
        {
            primaryArtifact = new ArtifactPath(request.PrimaryArtifactPath);
            var fullArtifactPath = Path.Combine(targetDir, primaryArtifact.Value);
            if (!File.Exists(fullArtifactPath))
            {
                throw new FileNotFoundException($"Specified primary project artifact '{primaryArtifact.Value}' does not exist in '{targetDir}'.");
            }
        }
        else
        {
            // Auto-discovery of candidate .flp in target directory (FR-REP-009 / FR-REP-010)
            var flpFiles = Directory.GetFiles(targetDir, "*.flp", SearchOption.TopDirectoryOnly);
            if (flpFiles.Length == 1)
            {
                primaryArtifact = new ArtifactPath(Path.GetFileName(flpFiles[0]));
            }
            else if (flpFiles.Length > 1)
            {
                throw new InvalidOperationException(
                    $"Multiple project files found in '{targetDir}'. Please specify the primary artifact explicitly with --primary <file>.");
            }
            else
            {
                throw new InvalidOperationException(
                    $"No .flp project file found in '{targetDir}'. Please create a project or specify the primary artifact explicitly with --primary <file>.");
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
