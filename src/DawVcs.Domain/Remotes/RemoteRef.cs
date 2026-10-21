using DawVcs.Domain.Common;
using DawVcs.Domain.Hashing;

namespace DawVcs.Domain.Remotes;

/// <summary>
/// Reference head advertised by a remote repository (e.g. 'refs/heads/main' -> CommitId).
/// </summary>
public sealed record RemoteRef(
    string Name,
    CommitId HeadCommit);
