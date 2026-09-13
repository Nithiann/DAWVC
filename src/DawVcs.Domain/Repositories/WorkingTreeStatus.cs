using DawVcs.Domain.Artifacts;
using DawVcs.Domain.Common;
using DawVcs.Domain.Hashing;

namespace DawVcs.Domain.Repositories;

public enum WorkingTreeItemKind
{
    Staged,
    Modified,
    Untracked,
    Missing,
    Renamed
}

public sealed record WorkingTreeItem(
    ArtifactPath Path,
    WorkingTreeItemKind Kind,
    long Size,
    ContentHash? Hash = null,
    ArtifactPath? OldPath = null);

/// <summary>
/// Status of the active workspace and working tree relative to HEAD and staging area (FR-STG-007, FR-STG-008).
/// </summary>
public sealed record WorkingTreeStatus(
    BranchName CurrentBranch,
    CommitId? HeadCommit,
    IReadOnlyList<WorkingTreeItem> Staged,
    IReadOnlyList<WorkingTreeItem> Modified,
    IReadOnlyList<WorkingTreeItem> Untracked,
    IReadOnlyList<WorkingTreeItem> Missing,
    IReadOnlyList<WorkingTreeItem> Renamed)
{
    public bool IsClean => Staged.Count == 0 && Modified.Count == 0 && Untracked.Count == 0 && Missing.Count == 0 && Renamed.Count == 0;
    public bool HasStagedChanges => Staged.Count > 0;
    public bool HasUntrackedAssets => Untracked.Count > 0;
    public bool HasWorkingTreeModifications => Modified.Count > 0 || Missing.Count > 0;
}
