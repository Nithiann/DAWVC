using System.Text;

using DawVcs.Adapters.Abstractions;
using DawVcs.Adapters.FLStudio.Tests.Fixtures;

using FluentAssertions;

using Xunit;

namespace DawVcs.Adapters.FLStudio.Tests;

/// <summary>
/// Valideert alle 13 scenario's uit de specificatieve fixturematrix (IMP-0511).
/// </summary>
public sealed class FLStudioFixtureMatrixTests
{
    private readonly FLStudioAdapter _adapter = new();

    [Fact]
    [Trait("Requirement", "FR-FLP-001")]
    public async Task Scenario01_EmptyProject_DetectsValidHeaderAndVersion()
    {
        var bytes = FlpFixtureGenerator.CreateValidFlp(version: "25.2.5.5319", channelCount: 5, ppq: 96);
        using var stream = new MemoryStream(bytes);

        var result = await _adapter.DetectAsync(stream);

        result.Status.Should().Be(ProjectDetectionStatus.Valid);
        result.DetectedVersion.Should().Be("25.2.5.5319");
        result.Metadata["ChannelCount"].Should().Be("5");
        result.Metadata["Ppq"].Should().Be("96");
        result.Metadata["SampleCount"].Should().Be("0");
        result.Metadata["PluginCount"].Should().Be("0");
    }

    [Fact]
    [Trait("Requirement", "FR-FLP-004")]
    public async Task Scenario02_SingleSample_ExtractsReferencedSample()
    {
        var bytes = FlpFixtureGenerator.CreateValidFlp(samplePaths: ["Audio/Kick.wav"]);
        using var stream = new MemoryStream(bytes);

        var result = await _adapter.DetectAsync(stream);

        result.Status.Should().Be(ProjectDetectionStatus.Valid);
        result.Metadata["SampleCount"].Should().Be("1");
        result.Metadata["SamplePaths"].Should().Contain("Audio/Kick.wav");
    }

    [Fact]
    [Trait("Requirement", "FR-FLP-004")]
    public async Task Scenario03_ExternalSample_ExtractsAbsolutePath()
    {
        var bytes = FlpFixtureGenerator.CreateValidFlp(samplePaths: [@"D:\SamplePacks\KSHMR\Vocal.wav"]);
        using var stream = new MemoryStream(bytes);

        var result = await _adapter.DetectAsync(stream);

        result.Status.Should().Be(ProjectDetectionStatus.Valid);
        result.Metadata["SamplePaths"].Should().Contain(@"D:\SamplePacks\KSHMR\Vocal.wav");
    }

    [Fact]
    [Trait("Requirement", "FR-FLP-004")]
    public async Task Scenario04_MissingSample_InspectsCleanlyWithoutThrowing()
    {
        var bytes = FlpFixtureGenerator.CreateValidFlp(samplePaths: ["NonExistent/Missing.wav"]);
        using var stream = new MemoryStream(bytes);

        var result = await _adapter.DetectAsync(stream);

        result.Status.Should().Be(ProjectDetectionStatus.Valid);
        result.Metadata["SamplePaths"].Should().Contain("NonExistent/Missing.wav");
    }

    [Fact]
    [Trait("Requirement", "FR-FLP-004")]
    public async Task Scenario05_Recording_ExtractsAudioRecordingPath()
    {
        var bytes = FlpFixtureGenerator.CreateValidFlp(samplePaths: ["Recorded/Take_01_Guitar.wav"]);
        using var stream = new MemoryStream(bytes);

        var result = await _adapter.DetectAsync(stream);

        result.Status.Should().Be(ProjectDetectionStatus.Valid);
        result.Metadata["SamplePaths"].Should().Contain("Recorded/Take_01_Guitar.wav");
    }

    [Fact]
    [Trait("Requirement", "FR-FLP-005")]
    public async Task Scenario06_NativePlugin_ExtractsPluginName()
    {
        var bytes = FlpFixtureGenerator.CreateValidFlp(pluginNames: ["Fruity Parametric EQ 2"]);
        using var stream = new MemoryStream(bytes);

        var result = await _adapter.DetectAsync(stream);

        result.Status.Should().Be(ProjectDetectionStatus.Valid);
        result.Metadata["PluginCount"].Should().Be("1");
        result.Metadata["PluginNames"].Should().Contain("Fruity Parametric EQ 2");
    }

    [Fact]
    [Trait("Requirement", "FR-FLP-005")]
    public async Task Scenario07_Vst3Plugin_ExtractsVst3Identifier()
    {
        var bytes = FlpFixtureGenerator.CreateValidFlp(pluginNames: ["Serum_x64.vst3"]);
        using var stream = new MemoryStream(bytes);

        var result = await _adapter.DetectAsync(stream);

        result.Status.Should().Be(ProjectDetectionStatus.Valid);
        result.Metadata["PluginNames"].Should().Contain("Serum_x64.vst3");
    }

    [Fact]
    [Trait("Requirement", "FR-FLP-004")]
    [Trait("Requirement", "FR-FLP-005")]
    public async Task Scenario08_MultipleAssets_ExtractsAllReferences()
    {
        var samples = new[] { "Kick.wav", "Snare.wav", "HiHat.wav" };
        var plugins = new[] { "Serum", "FabFilter Pro-Q 3", "Sylenth1" };

        var bytes = FlpFixtureGenerator.CreateValidFlp(samplePaths: samples, pluginNames: plugins, tempoBpm: 128.0, title: "Club Track");
        using var stream = new MemoryStream(bytes);

        var result = await _adapter.DetectAsync(stream);

        result.Status.Should().Be(ProjectDetectionStatus.Valid);
        result.Metadata["SampleCount"].Should().Be("3");
        result.Metadata["PluginCount"].Should().Be("3");
        result.Metadata["TempoBpm"].Should().Be("128");
        result.Metadata["ProjectTitle"].Should().Be("Club Track");
    }

    [Fact]
    [Trait("Requirement", "FR-FLP-004")]
    public async Task Scenario09_RenamedSample_ReadsCurrentPathDeterministically()
    {
        var bytes = FlpFixtureGenerator.CreateValidFlp(samplePaths: ["Samples/Bass_v2.wav"]);
        using var stream = new MemoryStream(bytes);

        var result = await _adapter.DetectAsync(stream);

        result.Status.Should().Be(ProjectDetectionStatus.Valid);
        result.Metadata["SamplePaths"].Should().Be("Samples/Bass_v2.wav");
    }

    [Fact]
    [Trait("Requirement", "FR-FLP-006")]
    public async Task Scenario10_TruncatedFlp_ReturnsInvalidStatusWithoutCrashing()
    {
        var bytes = FlpFixtureGenerator.CreateTruncatedFlp(length: 10); // Less than 14 bytes
        using var stream = new MemoryStream(bytes);

        var result = await _adapter.DetectAsync(stream);

        result.Status.Should().Be(ProjectDetectionStatus.Invalid);
        result.Confidence.Should().Be(0.0);
    }

    [Fact]
    [Trait("Requirement", "FR-SCAN-002")]
    public async Task Scenario11_WrongExtension_MarksExtensionEvidenceFalse()
    {
        var bytes = FlpFixtureGenerator.CreateValidFlp();
        using var stream = new MemoryStream(bytes);
        using var context = new ArtifactReadContext(stream, fileName: "project.txt", leaveOpen: true);

        var result = await _adapter.DetectAsync(context);

        result.Status.Should().Be(ProjectDetectionStatus.Valid); // Stream itself is valid FLP
        result.Evidence.Should().Contain(e => e.Category == "FileExtension" && !e.Matched);
    }

    [Fact]
    [Trait("Requirement", "FR-FLP-005")]
    public async Task Scenario12_ValidExtensionInvalidSignature_ReturnsInvalidStatus()
    {
        var bytes = FlpFixtureGenerator.CreateInvalidSignatureFlp();
        using var stream = new MemoryStream(bytes);

        var result = await _adapter.DetectAsync(stream);

        result.Status.Should().Be(ProjectDetectionStatus.Invalid);
        result.Findings.Should().Contain(f => f.Contains("Invalid FLP magic signature"));
    }

    [Fact]
    [Trait("Requirement", "FR-FLP-004")]
    [Trait("Requirement", "FR-FLP-008")]
    public async Task Scenario13_UnknownNewerVersion_DegradesGracefullyToUnsupportedForOpaqueFallback()
    {
        var bytes = FlpFixtureGenerator.CreateValidFlp(version: "99.0.0.9999");
        using var stream = new MemoryStream(bytes);

        var result = await _adapter.DetectAsync(stream);

        result.Status.Should().Be(ProjectDetectionStatus.Unsupported);
        result.RequiresOpaqueFallback.Should().BeTrue();
        result.Findings.Should().Contain(f => f.Contains("99.0.0.9999") && f.Contains("opaque"));
    }
}
