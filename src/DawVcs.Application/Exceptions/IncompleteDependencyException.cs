using DawVcs.Domain.Dependencies;

namespace DawVcs.Application.Exceptions;

/// <summary>
/// Wordt gegooid wanneer verplichte Bundle-dependencies ontbreken en --allow-incomplete niet is opgegeven (FR-DEP-012, AC-005).
/// Leidt in de CLI tot exitcode 5.
/// </summary>
public sealed class IncompleteDependencyException : Exception
{
    public IReadOnlyList<AssetDependency> MissingDependencies { get; }

    public IncompleteDependencyException(IEnumerable<AssetDependency> missingDependencies)
        : base(FormatMessage(missingDependencies))
    {
        MissingDependencies = missingDependencies?.ToList() ?? [];
    }

    private static string FormatMessage(IEnumerable<AssetDependency>? missing)
    {
        var list = missing?.ToList() ?? [];
        if (list.Count == 0)
        {
            return "Verplichte bundle dependencies ontbreken.";
        }

        var names = string.Join(", ", list.Select(m => $"'{m.Name}'"));
        return $"Commit geweigerd wegens {list.Count} ontbrekende verplichte bundle-dependenc{(list.Count == 1 ? "y" : "ies")}: {names}. Gebruik --allow-incomplete om bewust een incomplete snapshot vast te leggen.";
    }
}
