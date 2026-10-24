namespace DawVcs.Application.Common;

/// <summary>
/// Child completion is not completion of the enclosing operation.
/// </summary>
internal sealed class NestedOperationProgress(IProgress<OperationProgress> parent) : IProgress<OperationProgress>
{
    public void Report(OperationProgress value)
    {
        if (value.Stage != OperationStage.Completed) parent.Report(value);
    }
}
