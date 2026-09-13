using DawVcs.Domain.Artifacts;
using DawVcs.Domain.Common;

namespace DawVcs.Domain.Configuration;

/// <summary>
/// Domain model for repository configuration stored in dawvc.yaml (Schema v1).
/// </summary>
public sealed record RepositoryConfig
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;
    public RepositoryId RepositoryId { get; init; }
    public string ProjectName { get; init; }
    public ArtifactPath PrimaryArtifact { get; init; }
    public BranchName DefaultBranch { get; init; } = BranchName.Main;
    public RepositoryPolicies Policies { get; init; } = new();
    public IDictionary<string, object>? ExtraFields { get; init; }

    public RepositoryConfig(
        RepositoryId repositoryId,
        string projectName,
        ArtifactPath primaryArtifact,
        BranchName? defaultBranch = null,
        RepositoryPolicies? policies = null,
        int schemaVersion = CurrentSchemaVersion,
        IDictionary<string, object>? extraFields = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectName);

        RepositoryId = repositoryId;
        ProjectName = projectName.Trim();
        PrimaryArtifact = primaryArtifact;
        DefaultBranch = defaultBranch ?? BranchName.Main;
        Policies = policies ?? new RepositoryPolicies();
        SchemaVersion = schemaVersion;
        ExtraFields = extraFields;
    }
}

public sealed record RepositoryPolicies
{
    public string NewDependencies { get; init; } = "require-add";
    public string MissingBundledDependencies { get; init; } = "block";
    public string UnknownProjectFormat { get; init; } = "allow-opaque";
    public string InvalidProjectArtifact { get; init; } = "require-confirmation";
}
