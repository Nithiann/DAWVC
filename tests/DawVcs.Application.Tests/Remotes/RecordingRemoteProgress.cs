using DawVcs.Application.Common;

namespace DawVcs.Application.Tests.Remotes;

internal sealed class RecordingRemoteProgress : IProgress<OperationProgress>
{
    public List<OperationProgress> Reports { get; } = [];
    public OperationStage? ThrowOn { get; init; }
    public InvalidOperationException ObserverFailure { get; } = new("Observer failed");

    public void Report(OperationProgress value)
    {
        if (value.Stage == ThrowOn) throw ObserverFailure;
        Reports.Add(value);
    }
}
