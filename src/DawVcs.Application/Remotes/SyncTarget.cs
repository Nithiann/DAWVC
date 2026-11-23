using DawVcs.Domain.Common;
using DawVcs.Domain.Repositories;

namespace DawVcs.Application.Remotes;

/// <summary>Resolve once before transfer. Pull always integrates into the active local branch.</summary>
internal sealed record SyncTarget(BranchName LocalBranch, string RemoteName, BranchName RemoteBranch)
{
    public static SyncTarget ForPush(IRepositoryContext context, string? remote, string? branch)
    {
        var local = branch is null ? context.GetCurrentBranch() : BranchName.Parse(branch);
        return Resolve(context, local, remote, branch);
    }

    public static SyncTarget ForPull(IRepositoryContext context, string? remote, string? branch)
        => Resolve(context, context.GetCurrentBranch(), remote, branch);

    private static SyncTarget Resolve(IRepositoryContext context, BranchName local, string? remote, string? branch)
    {
        var upstream = context.GetUpstream(local);
        var remoteName = DawVcs.Domain.Common.RemoteName.Parse(remote ?? upstream.Remote ?? "origin").Value;
        var mappedBranch = string.Equals(remoteName, upstream.Remote, StringComparison.OrdinalIgnoreCase)
            ? upstream.RemoteBranch ?? local : local;
        return new SyncTarget(local, remoteName, branch is null ? mappedBranch : BranchName.Parse(branch));
    }
}
