using DawVcs.Application.Dependencies;
using DawVcs.Domain.Dependencies;

using FluentAssertions;

using Xunit;

namespace DawVcs.Application.Tests.Dependencies;

public sealed class PortabilityPolicyEngineTests
{
    private const string WorkspaceDir = @"C:\Users\Producer\Projects\Track01";

    [Fact]
    public void DetermineAssetPolicy_RelativePath_ReturnsBundle()
    {
        var policy = PortabilityPolicyEngine.DetermineAssetPolicy("Samples/Kick.wav", WorkspaceDir);

        policy.Mode.Should().Be(PortabilityMode.Bundle);
        policy.IsRedistributable.Should().BeTrue();
    }

    [Fact]
    public void DetermineAssetPolicy_SubdirectoryOfWorkspace_ReturnsBundle()
    {
        var fullPath = Path.Combine(WorkspaceDir, "Audio", "Take1.wav");
        var policy = PortabilityPolicyEngine.DetermineAssetPolicy(fullPath, WorkspaceDir);

        policy.Mode.Should().Be(PortabilityMode.Bundle);
        policy.IsRedistributable.Should().BeTrue();
    }

    [Fact]
    public void DetermineAssetPolicy_SystemPath_ReturnsReferenceOnly()
    {
        var systemPath = @"C:\Program Files\Common Files\VST3\PluginData.wav";
        var policy = PortabilityPolicyEngine.DetermineAssetPolicy(systemPath, WorkspaceDir);

        policy.Mode.Should().Be(PortabilityMode.ReferenceOnly);
        policy.IsRedistributable.Should().BeFalse();
    }

    [Fact]
    public void DetermineAssetPolicy_CommercialLibraryKeyword_ReturnsUserChoice()
    {
        var kontaktPath = @"D:\SamplePacks\Kontakt\FactoryLibrary\AcousticPiano.wav";
        var policy = PortabilityPolicyEngine.DetermineAssetPolicy(kontaktPath, WorkspaceDir);

        policy.Mode.Should().Be(PortabilityMode.UserChoice);
        policy.IsRedistributable.Should().BeFalse();
    }

    [Fact]
    public void DeterminePluginPolicy_AlwaysReturnsReferenceOnly()
    {
        var identity = new PluginIdentity("Xfer Records", "Serum", PluginFormat.VST3);
        var policy = PortabilityPolicyEngine.DeterminePluginPolicy(identity);

        policy.Mode.Should().Be(PortabilityMode.ReferenceOnly);
        policy.IsRedistributable.Should().BeFalse();
    }
}
