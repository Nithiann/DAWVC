using DawVcs.Domain.Metadata;

namespace DawVcs.Domain.Dependencies;

/// <summary>
/// Representeert een instrument- of effectplugin (FR-DEP-001, FR-DEP-004, TD §11.8).
/// Pluginbinaries worden principieel nooit gebundeld (FR-DEP-006).
/// </summary>
public sealed record PluginDependency : Dependency
{
    public PluginIdentity Plugin { get; init; }
    public string? VersionRequirement { get; init; }
    public PluginRole Role { get; init; }

    public PluginDependency(
        DependencyId id,
        PluginIdentity plugin,
        PluginRole role = PluginRole.Unknown,
        string? versionRequirement = null,
        DependencyRequirement requirement = DependencyRequirement.Required,
        DependencySource source = DependencySource.NativeProjectParser,
        PortabilityPolicy? portability = null,
        MetadataObservation<string>? provenance = null)
        : base(
            id,
            DependencyKind.Plugin,
            plugin?.Product ?? "Unknown Plugin",
            requirement,
            source,
            portability ?? PortabilityPolicy.ReferenceOnlyDefault,
            provenance)
    {
        ArgumentNullException.ThrowIfNull(plugin);
        Plugin = plugin;
        Role = role;
        VersionRequirement = versionRequirement;
    }
}
