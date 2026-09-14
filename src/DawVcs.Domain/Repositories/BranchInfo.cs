using DawVcs.Domain.Common;

namespace DawVcs.Domain.Repositories;

/// <summary>
/// Bevat informatie over een branch in de repository (FR-BRA-001..003).
/// </summary>
public sealed record BranchInfo(
    BranchName Name,
    CommitId? CommitId,
    bool IsCurrent);
