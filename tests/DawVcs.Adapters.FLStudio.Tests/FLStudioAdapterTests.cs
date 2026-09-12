using FluentAssertions;

using Xunit;

namespace DawVcs.Adapters.FLStudio.Tests;

public class FLStudioAdapterTests
{
    [Fact]
    public void FLStudioAdapter_ShouldExpose_CorrectMetadata()
    {
        var adapter = new FLStudioAdapter();

        adapter.DawName.Should().Be("FL Studio");
        adapter.SupportedExtensions.Should().Contain(".flp");
    }
}
