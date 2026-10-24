using DawVcs.Application.Common;
using DawVcs.Application.Diagnostics;

using FluentAssertions;

using Xunit;

namespace DawVcs.Application.Tests.Diagnostics;

public sealed class DiagnosticProgressTests
{
    [Theory]
    [InlineData(typeof(DoctorUseCase), typeof(DoctorRequest), typeof(DoctorReport))]
    [InlineData(typeof(FsckUseCase), typeof(FsckRequest), typeof(FsckReport))]
    public void ExecuteAsync_PreservesLegacySignatureAndRequiresProgressOverloadArguments(Type useCase, Type request, Type report)
    {
        var legacy = useCase.GetMethod("ExecuteAsync", [request, typeof(CancellationToken)])!;
        legacy.ReturnType.Should().Be(typeof(Task<>).MakeGenericType(report));
        legacy.GetParameters()[1].IsOptional.Should().BeTrue();

        var progress = useCase.GetMethod("ExecuteAsync", [request, typeof(CancellationToken), typeof(IProgress<OperationProgress>)])!;
        progress.ReturnType.Should().Be(legacy.ReturnType);
        progress.GetParameters().Should().OnlyContain(parameter => !parameter.IsOptional);
        typeof(FsckRequest).GetProperty(nameof(FsckRequest.CheckArtifacts)).Should().NotBeNull();
    }
}

internal sealed class DiagnosticProgress(Action<OperationProgress>? onReport = null) : IProgress<OperationProgress>
{
    public List<OperationProgress> Reports { get; } = [];

    public void Report(OperationProgress value)
    {
        Reports.Add(value);
        onReport?.Invoke(value);
    }
}
