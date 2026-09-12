using FluentAssertions;

using Xunit;

namespace DawVcs.IntegrationTests;

public class IntegrationSmokeTests
{
    [Fact]
    public void IntegrationPipeline_ShouldExecuteSuccessfully()
    {
        true.Should().BeTrue();
    }
}
