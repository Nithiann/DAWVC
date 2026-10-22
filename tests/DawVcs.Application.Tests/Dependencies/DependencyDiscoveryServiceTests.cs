using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

using DawVcs.Adapters.Abstractions;
using DawVcs.Application.Dependencies;
using DawVcs.Domain.Dependencies;

using FluentAssertions;

using NSubstitute;

using Xunit;

namespace DawVcs.Application.Tests.Dependencies;

public sealed class DependencyDiscoveryServiceTests : IDisposable
{
    private readonly string _tempRoot;

    public DependencyDiscoveryServiceTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "dawvc_discovery_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempRoot))
            {
                Directory.Delete(_tempRoot, recursive: true);
            }
        }
        catch
        {
            // Best effort
        }
    }

    [Fact]
    public async Task DiscoverAsync_WhenSampleResolvedViaAdapterSearchPath_MarksNotMissingAndComputesHash()
    {
        var externalDir = Path.Combine(_tempRoot, "ExternalSounds");
        Directory.CreateDirectory(externalDir);

        var externalSample = Path.Combine(externalDir, "KSHMR_Kick.wav");
        await File.WriteAllBytesAsync(externalSample, [0x01, 0x02, 0x03, 0x04]);

        var mockAdapter = Substitute.For<IDawAdapter>();
        mockAdapter.ResolveSamplePath("KSHMR_Kick.wav").Returns(externalSample);

        var detectionResult = new ProjectDetectionResult(
            ProjectDetectionStatus.Valid,
            "FL Studio",
            "2024",
            1.0,
            metadata: new Dictionary<string, string>
            {
                ["SamplePaths"] = "KSHMR_Kick.wav"
            });

        var graph = await DependencyDiscoveryService.DiscoverAsync(
            _tempRoot,
            detectionResult,
            mockAdapter);

        var asset = graph.Dependencies.OfType<AssetDependency>().Should().ContainSingle().Subject;
        asset.Name.Should().Be("KSHMR_Kick.wav");
        asset.IsMissing.Should().BeFalse();
        asset.Hash.Should().NotBeNull();
        asset.OriginalPath.Should().Be(externalSample);
    }

    [Fact]
    public async Task DiscoverAsync_WhenSampleNotFoundAnywhere_MarksIsMissingTrue()
    {
        var mockAdapter = Substitute.For<IDawAdapter>();
        mockAdapter.ResolveSamplePath("Missing.wav").Returns((string?)null);

        var detectionResult = new ProjectDetectionResult(
            ProjectDetectionStatus.Valid,
            "FL Studio",
            "2024",
            1.0,
            metadata: new Dictionary<string, string>
            {
                ["SamplePaths"] = "Missing.wav"
            });

        var graph = await DependencyDiscoveryService.DiscoverAsync(
            _tempRoot,
            detectionResult,
            mockAdapter);

        var asset = graph.Dependencies.OfType<AssetDependency>().Should().ContainSingle().Subject;
        asset.Name.Should().Be("Missing.wav");
        asset.IsMissing.Should().BeTrue();
        asset.Hash.Should().BeNull();
    }
}
