using DawVcs.Application.Dependencies;
using DawVcs.Domain.Dependencies;

using FluentAssertions;

using Xunit;

namespace DawVcs.Application.Tests.Dependencies;

public sealed class PluginVersionResolverTests
{
    [Theory]
    [InlineData("1.5.0", "1.5.0", true)]
    [InlineData("1.5.0", "1.6.2", true)]
    [InlineData("1.5.0", "2.0.0", true)]
    [InlineData("1.5.0", "1.4.9", false)]
    [InlineData("1.5.0", "1.0.0", false)]
    [InlineData("v1.2", "1.3.0", true)]
    [InlineData("2.1.0", "v2.0.9", false)]
    [InlineData(null, "1.0.0", true)]
    [InlineData("1.0.0", null, true)]
    public void IsVersionCompatible_EvaluatesMinimumVersionCorrectly(string? required, string? installed, bool expectedCompatible)
    {
        var result = DependencyResolverPipeline.IsVersionCompatible(required, installed, out var explanation);

        result.Should().Be(expectedCompatible);
        explanation.Should().NotBeNullOrWhiteSpace();

        if (!expectedCompatible)
        {
            explanation.Should().Contain("older than project version");
        }
    }

    [Theory]
    [InlineData("1.2.3", 1, 2, 3, -1)]
    [InlineData("v2.0", 2, 0, -1, -1)]
    [InlineData("3", 3, 0, -1, -1)]
    [InlineData("1.2.3.4", 1, 2, 3, 4)]
    [InlineData("v1.3.6-rc1", 1, 3, 6, -1)]
    public void TryParseVersion_ParsesCommonPluginVersionStrings(string input, int major, int minor, int build, int revision)
    {
        var success = DependencyResolverPipeline.TryParseVersion(input, out var version);

        success.Should().BeTrue();
        version.Major.Should().Be(major);
        version.Minor.Should().Be(minor);
        if (build >= 0)
        {
            version.Build.Should().Be(build);
        }
        if (revision >= 0)
        {
            version.Revision.Should().Be(revision);
        }
    }

    [Fact]
    public void CheckPluginInstalled_NativeFlStudioPlugin_ReturnsTrueWithoutExternalFile()
    {
        var nativePlugin = new PluginDependency(
            DependencyId.ForPlugin("Image-Line", "Fruity Parametric EQ 2", "Native"),
            new PluginIdentity("Image-Line", "Fruity Parametric EQ 2", PluginFormat.Native));

        var (found, path, version) = DependencyResolverPipeline.CheckPluginInstalled(nativePlugin);

        found.Should().BeTrue();
        path.Should().Be("Native FL Studio Plugin");
    }

    [Fact]
    public void CheckPluginInstalled_NonExistentPlugin_ReturnsFalse()
    {
        var fakePlugin = new PluginDependency(
            DependencyId.ForPlugin("NonExistentVendor", "GhostPlugin_99999", "VST3"),
            new PluginIdentity("NonExistentVendor", "GhostPlugin_99999", PluginFormat.VST3));

        var (found, path, version) = DependencyResolverPipeline.CheckPluginInstalled(fakePlugin);

        found.Should().BeFalse();
        path.Should().BeNull();
        version.Should().BeNull();
    }
}
