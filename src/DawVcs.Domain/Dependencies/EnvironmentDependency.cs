using DawVcs.Domain.Metadata;

namespace DawVcs.Domain.Dependencies;

/// <summary>
/// Representeert een systeem- of DAW-omgevingsvereiste (bijv. FL Studio 2026.x, Windows 11 x64).
/// </summary>
public sealed record EnvironmentDependency : Dependency
{
    public string Key { get; init; }
    public string ExpectedValue { get; init; }

    public EnvironmentDependency(
        DependencyId id,
        string key,
        string expectedValue,
        DependencyRequirement requirement = DependencyRequirement.Required,
        DependencySource source = DependencySource.Inference,
        MetadataObservation<string>? provenance = null)
        : base(
            id,
            DependencyKind.Environment,
            $"{key}={expectedValue}",
            requirement,
            source,
            PortabilityPolicy.ReferenceOnlyDefault,
            provenance)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        Key = key;
        ExpectedValue = expectedValue;
    }
}
