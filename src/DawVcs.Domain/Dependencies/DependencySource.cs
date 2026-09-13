namespace DawVcs.Domain.Dependencies;

public enum DependencySource
{
    NativeProjectParser,
    ManualStaging,
    Inference,
    Lockfile
}
