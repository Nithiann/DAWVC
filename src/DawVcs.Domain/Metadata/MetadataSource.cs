namespace DawVcs.Domain.Metadata;

/// <summary>
/// Bron waaruit een project- of omgevingsmetadata-waarde is geëxtraheerd.
/// </summary>
public enum MetadataSource
{
    NativeProjectParser,
    DawExport,
    ProjectDataFolder,
    FileSystemScan,
    PluginScan,
    PluginState,
    UserInput,
    Inference
}
