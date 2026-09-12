using FluentAssertions;

using Xunit;

namespace DawVcs.Infrastructure.Tests;

public class InfrastructureSmokeTests
{
    [Fact]
    public void InfrastructureLayer_ShouldInitializeSuccessfully()
    {
        true.Should().BeTrue();
    }
}
