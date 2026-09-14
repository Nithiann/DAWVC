using DawVcs.Domain.Metadata;

namespace DawVcs.Domain.Dependencies;

/// <summary>
/// Content die via een plugin wordt geladen, zoals sample libraries of wavetables (FR-DEP-001, TD §11.9).
/// </summary>
public sealed record PluginContentDependency : Dependency
{
    public PluginIdentity Plugin { get; init; }
    public string ContentName { get; init; }
    public string ContentType { get; init; }

    public PluginContentDependency(
        DependencyId id,
        PluginIdentity plugin,
        string contentName,
        string contentType,
        DependencyRequirement requirement = DependencyRequirement.Required,
        DependencySource source = DependencySource.Inference,
        PortabilityPolicy? portability = null,
        MetadataObservation<string>? provenance = null)
        : base(
            id,
            DependencyKind.PluginContent,
            contentName,
            requirement,
            source,
            portability ?? PortabilityPolicy.UserChoiceDefault,
            provenance)
    {
        ArgumentNullException.ThrowIfNull(plugin);
        ArgumentException.ThrowIfNullOrWhiteSpace(contentName);

        Plugin = plugin;
        ContentName = contentName;
        ContentType = contentType;
    }
}
