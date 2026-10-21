using DawVcs.Domain.Common;

using FluentAssertions;

using Xunit;

namespace DawVcs.Domain.Tests.Common;

public sealed class RemoteNameTests
{
    [Theory]
    [InlineData("origin")]
    [InlineData("upstream")]
    [InlineData("backup_nas")]
    [InlineData("remote-server-1")]
    [InlineData("nas.backup")]
    public void Constructor_ValidName_CreatesSuccessfully(string valid)
    {
        var remote = new RemoteName(valid);
        remote.Value.Should().Be(valid);
        remote.ToString().Should().Be(valid);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(".hidden")]
    [InlineData("trailing.")]
    [InlineData("path/with/slashes")]
    [InlineData("path\\with\\backslashes")]
    [InlineData("has space")]
    [InlineData("has:colon")]
    [InlineData("has*star")]
    [InlineData("has?question")]
    [InlineData("double..dot")]
    public void Constructor_InvalidName_ThrowsArgumentException(string invalid)
    {
        var act = () => new RemoteName(invalid);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void TryCreate_InvalidName_ReturnsFalseWithError()
    {
        var success = RemoteName.TryCreate("invalid/name", out var remote, out var error);
        success.Should().BeFalse();
        error.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void Comparison_WorksCaseInsensitively()
    {
        var a = new RemoteName("alpha");
        var b = new RemoteName("beta");
        var c = new RemoteName("ALPHA");

        (a < b).Should().BeTrue();
        (b > a).Should().BeTrue();
        (a <= c).Should().BeTrue();
        (a >= c).Should().BeTrue();
        a.Equals(c).Should().BeFalse(); // record struct value equality is case-sensitive for string, but comparison is ordinal case-insensitive
    }
}
