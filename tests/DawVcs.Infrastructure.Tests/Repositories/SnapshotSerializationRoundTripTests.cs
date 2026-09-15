using DawVcs.Domain.Artifacts;
using DawVcs.Domain.Common;
using DawVcs.Domain.Dependencies;
using DawVcs.Domain.Entities;
using DawVcs.Domain.Hashing;
using DawVcs.Domain.Metadata;
using DawVcs.Domain.Storage;
using DawVcs.Infrastructure.Repositories;

using FluentAssertions;

using Xunit;

namespace DawVcs.Infrastructure.Tests.Repositories;

public sealed class SnapshotSerializationRoundTripTests : IDisposable
{
    private readonly string _tempDir;

    public SnapshotSerializationRoundTripTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "dawvc_snapshot_roundtrip_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, true);
        }
    }

    [Fact]
    public async Task Snapshot_FullDependencyGraph_RoundTripsThroughObjectStoreSemanticallyIdentical()
    {
        // 1. Initialize context
        using var context = new FileSystemRepositoryContext(_tempDir);

        // 2. Build comprehensive, multi-kind dependencies
        var sampleBytes = new byte[] { 0x52, 0x49, 0x46, 0x46, 0x01, 0x02, 0x03, 0x04 };
        var sampleHash = Blake3ContentHasher.Hash(sampleBytes);

        var assetDep = new AssetDependency(
            DependencyId.ForAsset(sampleHash),
            "Kick_Heavy.wav",
            DependencyRequirement.Required,
            DependencySource.NativeProjectParser,
            PortabilityPolicy.BundleDefault,
            relativePath: new ArtifactPath("Audio/Kick_Heavy.wav"),
            originalPath: @"D:\Studio\Samples\Kick_Heavy.wav",
            hash: sampleHash,
            fileSize: 44100,
            isMissing: false,
            provenance: new MetadataObservation<string>("FLdt chunk event 198", "FlStudioAdapter"));

        var pluginDep = new PluginDependency(
            DependencyId.ForPlugin("FabFilter", "FabFilter Pro-Q 3", "VST3"),
            new PluginIdentity("FabFilter", "FabFilter Pro-Q 3", PluginFormat.VST3),
            role: PluginRole.Effect,
            versionRequirement: "3.24.0",
            requirement: DependencyRequirement.Required,
            source: DependencySource.NativeProjectParser,
            portability: PortabilityPolicy.ReferenceOnlyDefault,
            provenance: new MetadataObservation<string>("Slot 1 on Master", "FlStudioAdapter"));

        var envDep = new EnvironmentDependency(
            new DependencyId("env:flstudio:version"),
            "FlStudioVersion",
            "2026.1",
            requirement: DependencyRequirement.Optional,
            source: DependencySource.Inference,
            provenance: new MetadataObservation<string>("Project header version 25.2.5", "FlStudioAdapter"));

        var depGraph = new DependencyGraph([assetDep, pluginDep, envDep]);

        // 3. Construct artifact & snapshot
        var primaryHash = Blake3ContentHasher.Hash(new byte[] { 0x46, 0x4C, 0x68, 0x64 });
        var rootEntry = ArtifactEntry.Create(new ArtifactPath("Track.flp"), primaryHash, 4, ArtifactRole.PrimaryProjectFile);
        var projectArtifact = new ProjectArtifact("FL Studio", new SingleFileArtifact(rootEntry));

        var snapshot = new ProjectSnapshot(
            projectArtifact,
            DateTimeOffset.UtcNow,
            new Dictionary<string, string> { ["Tempo"] = "130.0", ["Ppq"] = "96" },
            isComplete: true,
            incompleteReason: null,
            dependencies: depGraph);

        // 4. Save to object store via canonical bytes
        var canonicalBytes = snapshot.ToCanonicalBytes();
        var writtenHash = await context.ObjectStore.WriteObjectAsync(ObjectType.Snapshot, canonicalBytes);
        writtenHash.Should().Be(snapshot.Id.Value, "SnapshotId is the BLAKE3 hash of its canonical bytes");

        // 5. Read back through context.LoadSnapshotAsync
        var restored = await context.LoadSnapshotAsync(snapshot.Id);

        // 6. Assert exact semantic equivalence
        restored.Should().NotBeNull();
        restored!.Id.Should().Be(snapshot.Id);
        restored.Project.DawName.Should().Be("FL Studio");
        restored.Metadata["Tempo"].Should().Be("130.0");
        restored.IsComplete.Should().BeTrue();

        var restoredDeps = restored.Dependencies.Dependencies.ToList();
        restoredDeps.Should().HaveCount(3);

        // Verify restored AssetDependency
        var restoredAsset = restoredDeps.OfType<AssetDependency>().Should().ContainSingle().Subject;
        restoredAsset.Id.Should().Be(assetDep.Id);
        restoredAsset.Name.Should().Be("Kick_Heavy.wav");
        restoredAsset.Hash.Should().Be(sampleHash);
        restoredAsset.FileSize.Should().Be(44100);
        restoredAsset.OriginalPath.Should().Be(@"D:\Studio\Samples\Kick_Heavy.wav");
        restoredAsset.RelativePath!.Value.Value.Should().Be("Audio/Kick_Heavy.wav");
        restoredAsset.IsMissing.Should().BeFalse();
        restoredAsset.Provenance?.Value.Should().Be("FLdt chunk event 198");
        restoredAsset.Portability.Mode.Should().Be(PortabilityMode.Bundle);
        restoredAsset.Requirement.Should().Be(DependencyRequirement.Required);
        restoredAsset.Source.Should().Be(DependencySource.NativeProjectParser);

        // Verify restored PluginDependency (no more "Unknown" vendor or format!)
        var restoredPlugin = restoredDeps.OfType<PluginDependency>().Should().ContainSingle().Subject;
        restoredPlugin.Id.Should().Be(pluginDep.Id);
        restoredPlugin.Plugin.Vendor.Should().Be("FabFilter");
        restoredPlugin.Plugin.Product.Should().Be("FabFilter Pro-Q 3");
        restoredPlugin.Plugin.Format.Should().Be(PluginFormat.VST3);
        restoredPlugin.Role.Should().Be(PluginRole.Effect);
        restoredPlugin.VersionRequirement.Should().Be("3.24.0");
        restoredPlugin.Portability.Mode.Should().Be(PortabilityMode.ReferenceOnly);
        restoredPlugin.Requirement.Should().Be(DependencyRequirement.Required);

        // Verify restored EnvironmentDependency
        var restoredEnv = restoredDeps.OfType<EnvironmentDependency>().Should().ContainSingle().Subject;
        restoredEnv.Key.Should().Be("FlStudioVersion");
        restoredEnv.ExpectedValue.Should().Be("2026.1");
        restoredEnv.Requirement.Should().Be(DependencyRequirement.Optional);
    }
}
