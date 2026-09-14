namespace DawVcs.Application.Diagnostics;

public sealed record IntegrityIssue(
    string Code,
    string Target,
    string Message,
    bool IsError);

public sealed record RepositoryIntegrityReport(
    int TotalObjectsScanned,
    int ValidObjectsCount,
    int CorruptObjectsCount,
    int MissingObjectsCount,
    int OrphanObjectsCount,
    IReadOnlyList<IntegrityIssue> Errors,
    IReadOnlyList<IntegrityIssue> Warnings)
{
    public bool IsIntact => Errors.Count == 0;
}
