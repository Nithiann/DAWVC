using DawVcs.Domain.Artifacts;
using DawVcs.Domain.Dependencies;
using DawVcs.Domain.Hashing;

using FluentAssertions;

using Xunit;

namespace DawVcs.Domain.Tests.Dependencies;

public sealed class DependencyGraphTests
{
    [Fact]
    public void CalculateBundleByteSize_SumsOnlyNonMissingBundledAssets()
    {
        var sample1 = new AssetDependency(
            DependencyId.ForAsset("kick.wav"),
            "kick.wav",
            DependencyRequirement.Required,
            DependencySource.NativeProjectParser,
            PortabilityPolicy.BundleDefault,
            relativePath: new ArtifactPath("kick.wav"),
            fileSize: 1000,
            isMissing: false);

        var sample2 = new AssetDependency(
            DependencyId.ForAsset("snare.wav"),
            "snare.wav",
            DependencyRequirement.Required,
            DependencySource.NativeProjectParser,
            PortabilityPolicy.BundleDefault,
            relativePath: new ArtifactPath("snare.wav"),
            fileSize: 2500,
            isMissing: false);

        var missingSample = new AssetDependency(
            DependencyId.ForAsset("vocal.wav"),
            "vocal.wav",
            DependencyRequirement.Required,
            DependencySource.NativeProjectParser,
            PortabilityPolicy.BundleDefault,
            relativePath: null,
            fileSize: null,
            isMissing: true);

        var plugin = new PluginDependency(
            DependencyId.ForPlugin("Xfer Records", "Serum", "VST3"),
            new PluginIdentity("Xfer Records", "Serum", PluginFormat.VST3));

        var graph = new DependencyGraph([sample1, sample2, missingSample, plugin]);

        graph.CalculateBundleByteSize().Should().Be(3500);
        graph.GetBundledDependencies().Should().HaveCount(3);
        graph.GetReferenceOnlyDependencies().Should().HaveCount(1);
        graph.GetMissingRequiredBundleDependencies().Should().ContainSingle().Which.Name.Should().Be("vocal.wav");
    }
}
