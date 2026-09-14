using DawVcs.Domain.Artifacts;
using DawVcs.Domain.Common;
using DawVcs.Domain.Configuration;

using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace DawVcs.Infrastructure.Configuration;

/// <summary>
/// Reads, validates, and writes dawvc.yaml files conforming to Schema v1.
/// Preserves unknown fields and validates paths according to FR-CFG-001 through FR-CFG-004.
/// </summary>
public static class YamlRepositoryConfigManager
{
    public const string ConfigFileName = "dawvc.yaml";

    private static readonly IDeserializer Deserializer = new DeserializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();

    private static readonly ISerializer Serializer = new SerializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .Build();

    public static RepositoryConfig ReadFromDirectory(string directory)
    {
        var path = Path.Combine(directory, ConfigFileName);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"Configuration file '{ConfigFileName}' was not found in '{directory}'.", path);
        }

        var yaml = File.ReadAllText(path);
        return Parse(yaml);
    }

    public static RepositoryConfig Parse(string yaml)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(yaml);

        Dictionary<string, object> rawDict;
        try
        {
            rawDict = Deserializer.Deserialize<Dictionary<string, object>>(yaml)
                ?? throw new InvalidOperationException("Config file is empty.");
        }
        catch (Exception ex) when (ex is not InvalidOperationException)
        {
            throw new InvalidOperationException($"Failed to parse YAML configuration: {ex.Message}", ex);
        }

        if (!rawDict.TryGetValue("schemaVersion", out var schemaVersionObj) ||
            !int.TryParse(schemaVersionObj?.ToString(), out var schemaVersion))
        {
            throw new InvalidOperationException("Missing or invalid 'schemaVersion' in dawvc.yaml.");
        }

        if (schemaVersion > RepositoryConfig.CurrentSchemaVersion)
        {
            throw new InvalidOperationException($"Unsupported repository schemaVersion {schemaVersion}. Current supported version is {RepositoryConfig.CurrentSchemaVersion}.");
        }

        if (!rawDict.TryGetValue("repositoryId", out var repoIdObj) ||
            !RepositoryId.TryParse(repoIdObj?.ToString(), out var repoId))
        {
            throw new InvalidOperationException("Missing or invalid 'repositoryId' in dawvc.yaml.");
        }

        if (!rawDict.TryGetValue("projectName", out var projectNameObj) ||
            string.IsNullOrWhiteSpace(projectNameObj?.ToString()))
        {
            throw new InvalidOperationException("Missing or empty 'projectName' in dawvc.yaml.");
        }
        var projectName = projectNameObj.ToString()!;

        string? pathError = null;
        if (!rawDict.TryGetValue("primaryArtifact", out var primaryArtifactObj) ||
            !ArtifactPath.TryCreate(primaryArtifactObj?.ToString(), out var primaryArtifact, out pathError))
        {
            throw new InvalidOperationException($"Invalid 'primaryArtifact' in dawvc.yaml: {pathError ?? "Missing property"}");
        }

        BranchName defaultBranch = BranchName.Main;
        if (rawDict.TryGetValue("defaultBranch", out var defaultBranchObj) &&
            BranchName.TryCreate(defaultBranchObj?.ToString(), out var parsedBranch, out _))
        {
            defaultBranch = parsedBranch;
        }

        var policies = new RepositoryPolicies();
        if (rawDict.TryGetValue("policies", out var policiesObj) && policiesObj is Dictionary<object, object> polDict)
        {
            policies = new RepositoryPolicies
            {
                NewDependencies = polDict.TryGetValue("newDependencies", out var nd) ? nd?.ToString() ?? "require-add" : "require-add",
                MissingBundledDependencies = polDict.TryGetValue("missingBundledDependencies", out var mbd) ? mbd?.ToString() ?? "block" : "block",
                UnknownProjectFormat = polDict.TryGetValue("unknownProjectFormat", out var upf) ? upf?.ToString() ?? "allow-opaque" : "allow-opaque",
                InvalidProjectArtifact = polDict.TryGetValue("invalidProjectArtifact", out var ipa) ? ipa?.ToString() ?? "require-confirmation" : "require-confirmation"
            };
        }

        // Collect extra fields to preserve per FR-CFG-003
        var knownKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "schemaVersion", "repositoryId", "projectName", "primaryArtifact", "defaultBranch", "policies"
        };

        var extraFields = new Dictionary<string, object>();
        foreach (var kvp in rawDict)
        {
            if (!knownKeys.Contains(kvp.Key))
            {
                extraFields[kvp.Key] = kvp.Value;
            }
        }

        return new RepositoryConfig(
            repoId,
            projectName,
            primaryArtifact,
            defaultBranch,
            policies,
            schemaVersion,
            extraFields.Count > 0 ? extraFields : null);
    }

    public static void WriteToDirectory(string directory, RepositoryConfig config)
    {
        var path = Path.Combine(directory, ConfigFileName);
        var yaml = ToYaml(config);
        File.WriteAllText(path, yaml);
    }

    public static string ToYaml(RepositoryConfig config)
    {
        var dict = new Dictionary<string, object>
        {
            ["schemaVersion"] = config.SchemaVersion,
            ["repositoryId"] = config.RepositoryId.ToString(),
            ["projectName"] = config.ProjectName,
            ["primaryArtifact"] = config.PrimaryArtifact.Value,
            ["defaultBranch"] = config.DefaultBranch.Value,
            ["policies"] = new Dictionary<string, string>
            {
                ["newDependencies"] = config.Policies.NewDependencies,
                ["missingBundledDependencies"] = config.Policies.MissingBundledDependencies,
                ["unknownProjectFormat"] = config.Policies.UnknownProjectFormat,
                ["invalidProjectArtifact"] = config.Policies.InvalidProjectArtifact
            }
        };

        if (config.ExtraFields is not null)
        {
            foreach (var kvp in config.ExtraFields)
            {
                dict[kvp.Key] = kvp.Value;
            }
        }

        return Serializer.Serialize(dict);
    }
}
