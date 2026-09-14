using System.Text;

using DawVcs.Domain.Artifacts;
using DawVcs.Domain.Tests.Common;

using FluentAssertions;

using Xunit;

namespace DawVcs.Domain.Tests.Artifacts;

public class ArtifactPathTests
{
    [Theory]
    [Requirement("INV-007")]
    [InlineData("audio/kick.wav", "audio/kick.wav")]
    [InlineData("audio\\kick.wav", "audio/kick.wav")]
    [InlineData("samples/drums/snare.wav", "samples/drums/snare.wav")]
    [InlineData("samples\\drums\\snare.wav", "samples/drums/snare.wav")]
    [InlineData("project.flp", "project.flp")]
    [InlineData("./audio/kick.wav", "audio/kick.wav")]
    public void Constructor_ValidRelativePaths_NormalizesWithForwardSlashes(string raw, string expected)
    {
        var path = new ArtifactPath(raw);
        path.Value.Should().Be(expected);
        path.ToString().Should().Be(expected);
    }

    [Fact]
    [Requirement("INV-007")]
    public void Constructor_UnicodeInput_NormalizesToFormC()
    {
        // Decomposed 'e' + combining acute accent (Form D)
        string decomposed = "caf\u0065\u0301/sample.wav";
        // Precomposed 'é' (Form C)
        string precomposed = "caf\u00E9/sample.wav";

        var path = new ArtifactPath(decomposed);
        path.Value.Should().Be(precomposed);
        path.Value.IsNormalized(NormalizationForm.FormC).Should().BeTrue();
    }

    [Theory]
    [Requirement("FR-CHK-012")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("/absolute/path")]
    [InlineData("\\absolute\\path")]
    [InlineData("C:/projects/audio.wav")]
    [InlineData("D:\\projects\\audio.wav")]
    [InlineData("../parent.wav")]
    [InlineData("audio/../../escape.wav")]
    [InlineData("audio//kick.wav")]
    public void Constructor_InvalidOrTraversalPaths_ThrowsArgumentException(string invalidPath)
    {
        var act = () => new ArtifactPath(invalidPath);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    [Requirement("INV-007")]
    public void Properties_FileName_Extension_DirectoryName_ExtractCorrectly()
    {
        var path = new ArtifactPath("Audio/Samples/Kick.WAV");

        path.FileName.Should().Be("Kick.WAV");
        path.Extension.Should().Be(".WAV");
        path.DirectoryName.Should().Be("Audio/Samples");
    }

    [Fact]
    [Requirement("INV-007")]
    public void Combine_AppendsRelativeChildDeterministically()
    {
        var baseDir = new ArtifactPath("Samples/Drums");
        var combined = baseDir.Combine("HiHat.wav");

        combined.Value.Should().Be("Samples/Drums/HiHat.wav");
    }
}
