using DawVcs.Domain.Artifacts;
using DawVcs.Domain.Hashing;

namespace DawVcs.Domain.Checkouts;

public enum CheckoutActionKind
{
    Create,
    Replace,
    Delete,
    Unchanged
}

public sealed record CheckoutAction(
    ArtifactPath Path,
    CheckoutActionKind Kind,
    ContentHash? TargetHash = null,
    ContentHash? CurrentHash = null);

public sealed record CheckoutConflict(
    ArtifactPath Path,
    string Reason);

public sealed record CheckoutPlan(
    IReadOnlyList<CheckoutAction> Actions,
    IReadOnlyList<CheckoutConflict> Conflicts)
{
    public bool HasConflicts => Conflicts.Count > 0;
}
