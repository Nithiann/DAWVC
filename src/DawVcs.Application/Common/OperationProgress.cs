namespace DawVcs.Application.Common;

/// <summary>
/// Stable presentation-independent stages. Counters apply to one stage, not the whole operation.
/// </summary>
public enum OperationStage
{
    OpeningRepository,
    LoadingMetadata,
    DiscoveringFiles,
    CheckingCache,
    HashingFiles,
    ClassifyingChanges,
    InspectingArtifact,
    Completed
}

public enum OperationProgressUnit
{
    Items,
    Bytes
}

/// <summary>
/// An immutable progress observation without paths or localized presentation text.
/// A null total means unknown; zero means a known empty stage. Completion is determined by
/// the operation's task, not by counters or delivery of a queued progress notification.
/// </summary>
public sealed record OperationProgress
{
    public OperationStage Stage { get; }
    public long Completed { get; }
    public long? Total { get; }
    public OperationProgressUnit Unit { get; }

    public OperationProgress(
        OperationStage stage,
        long completed = 0,
        long? total = null,
        OperationProgressUnit unit = OperationProgressUnit.Items)
    {
        if (!Enum.IsDefined(stage)) throw new ArgumentOutOfRangeException(nameof(stage));
        if (!Enum.IsDefined(unit)) throw new ArgumentOutOfRangeException(nameof(unit));
        ArgumentOutOfRangeException.ThrowIfNegative(completed);
        if (total < 0) throw new ArgumentOutOfRangeException(nameof(total));
        if (total.HasValue && completed > total.Value)
        {
            throw new ArgumentOutOfRangeException(nameof(completed), "Completed work cannot exceed the known total.");
        }

        Stage = stage;
        Completed = completed;
        Total = total;
        Unit = unit;
    }
}
