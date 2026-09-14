using DawVcs.Domain.Repositories;

namespace DawVcs.Application.Exceptions;

/// <summary>
/// Wordt gegooid wanneer een checkout naar de actieve workspace wordt geweigerd wegens lokale wijzigingen (FR-CHK-008, AC-009).
/// Vertaalt naar exitcode 7 in de CLI.
/// </summary>
public sealed class DirtyWorkspaceException : Exception
{
    public WorkingTreeStatus Status { get; }

    public DirtyWorkspaceException(string message, WorkingTreeStatus status)
        : base(message)
    {
        Status = status;
    }

    public DirtyWorkspaceException(WorkingTreeStatus status)
        : base(FormatMessage(status))
    {
        Status = status;
    }

    private static string FormatMessage(WorkingTreeStatus status)
    {
        var parts = new List<string>();
        if (status.Staged.Count > 0)
        {
            parts.Add($"{status.Staged.Count} staged changes");
        }
        if (status.Modified.Count > 0)
        {
            parts.Add($"{status.Modified.Count} modified files");
        }
        if (status.Missing.Count > 0)
        {
            parts.Add($"{status.Missing.Count} missing tracked files");
        }

        var details = parts.Count > 0 ? string.Join(", ", parts) : "uncommitted changes";
        return $"Cannot checkout: workspace contains {details}. Commit or stash your changes, or use --force to create a recovery copy, or use --restore-to <dir> to restore to a separate directory.";
    }
}
