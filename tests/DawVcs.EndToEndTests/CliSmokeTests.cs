using FluentAssertions;

using Xunit;

namespace DawVcs.EndToEndTests;

public class CliSmokeTests
{
    [Fact]
    public async Task Cli_WhenInvokedWithVersion_ShouldReturnZero()
    {
        var exitCode = await DawVcs.Cli.Program.Main(["--version"]);
        exitCode.Should().Be(0);
    }

    [Fact]
    public async Task Cli_WhenInvokedWithHelp_ShouldReturnZero()
    {
        var exitCode = await DawVcs.Cli.Program.Main(["--help"]);
        exitCode.Should().Be(0);
    }
}
