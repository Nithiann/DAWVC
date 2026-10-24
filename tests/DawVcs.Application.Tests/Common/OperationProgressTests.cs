using DawVcs.Application.Common;

using FluentAssertions;

using Xunit;

namespace DawVcs.Application.Tests.Common;

public sealed class OperationProgressTests
{
    [Fact]
    public void UnknownTotal_IsDistinctFromKnownEmptyStage()
    {
        var unknown = new OperationProgress(OperationStage.DiscoveringFiles);
        var empty = new OperationProgress(OperationStage.HashingFiles, total: 0);

        unknown.Total.Should().BeNull();
        empty.Total.Should().Be(0);
        unknown.Completed.Should().Be(0);
        unknown.Unit.Should().Be(OperationProgressUnit.Items);
    }

    [Fact]
    public void UnknownTotal_AllowsObservedWorkWithoutInventingADenominator()
    {
        var progress = new OperationProgress(OperationStage.DiscoveringFiles, long.MaxValue);

        progress.Completed.Should().Be(long.MaxValue);
        progress.Total.Should().BeNull();
    }

    [Theory]
    [InlineData(0L, 0L)]
    [InlineData(0L, 10L)]
    [InlineData(10L, 10L)]
    [InlineData(long.MaxValue, long.MaxValue)]
    public void KnownTotal_AcceptsValidLongCounters(long completed, long total)
    {
        var progress = new OperationProgress(OperationStage.HashingFiles, completed, total, OperationProgressUnit.Bytes);

        progress.Completed.Should().Be(completed);
        progress.Total.Should().Be(total);
        progress.Unit.Should().Be(OperationProgressUnit.Bytes);
    }

    [Theory]
    [InlineData(-1L, null, "completed")]
    [InlineData(0L, -1L, "total")]
    [InlineData(1L, 0L, "completed")]
    [InlineData(11L, 10L, "completed")]
    public void InvalidCounters_AreRejected(long completed, long? total, string parameter)
    {
        var act = () => new OperationProgress(OperationStage.HashingFiles, completed, total);

        act.Should().Throw<ArgumentOutOfRangeException>().WithParameterName(parameter);
    }

    [Fact]
    public void UndefinedStage_IsRejected()
    {
        var act = () => new OperationProgress((OperationStage)(-1));

        act.Should().Throw<ArgumentOutOfRangeException>().WithParameterName("stage");
    }

    [Fact]
    public void UndefinedUnit_IsRejected()
    {
        var act = () => new OperationProgress(OperationStage.HashingFiles, unit: (OperationProgressUnit)(-1));

        act.Should().Throw<ArgumentOutOfRangeException>().WithParameterName("unit");
    }

    [Fact]
    public void EquivalentObservations_HaveValueEquality()
    {
        var first = new OperationProgress(OperationStage.CheckingCache, 2, 5);
        var second = new OperationProgress(OperationStage.CheckingCache, 2, 5);

        first.Should().Be(second);
    }
}
