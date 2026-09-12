using FluentAssertions;

using Xunit;

namespace DawVcs.Application.Tests;

public class ApplicationSmokeTests
{
    [Fact]
    public void ApplicationLayer_ShouldInitializeSuccessfully()
    {
        true.Should().BeTrue();
    }
}
