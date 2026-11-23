using System.Collections.ObjectModel;

using DawVcs.Domain.Common;
using DawVcs.Domain.Remotes;

namespace DawVcs.Application.Remotes;

/// <summary>A pinned immutable advertisement and verified parent overlay, without paths or handles.</summary>
public sealed class FetchPreview
{
    public IReadOnlyList<RemoteRef> RemoteRefs { get; }
    public IReadOnlyDictionary<CommitId, IReadOnlyList<CommitId>> Parents { get; }

    public FetchPreview(IEnumerable<RemoteRef> remoteRefs, IReadOnlyDictionary<CommitId, IReadOnlyList<CommitId>> parents)
    {
        ArgumentNullException.ThrowIfNull(remoteRefs);
        ArgumentNullException.ThrowIfNull(parents);
        RemoteRefs = Array.AsReadOnly(remoteRefs.ToArray());
        Parents = new ReadOnlyDictionary<CommitId, IReadOnlyList<CommitId>>(parents.ToDictionary(
            entry => entry.Key, entry => (IReadOnlyList<CommitId>)Array.AsReadOnly(entry.Value.ToArray())));
    }
}
