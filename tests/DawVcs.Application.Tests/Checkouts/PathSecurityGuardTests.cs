using DawVcs.Application.Checkouts;
using DawVcs.Application.Exceptions;

using FluentAssertions;

using Xunit;

namespace DawVcs.Application.Tests.Checkouts;

public class PathSecurityGuardTests
{
    [Theory]
    [InlineData("../evil.flp")]
    [InlineData("audio/../../secret.wav")]
    [InlineData("audio/../secret.wav")]
    [InlineData("./same.wav")]
    public void ValidatePath_ThrowsOnTraversal(string invalidPath)
    {
        var act = () => PathSecurityGuard.ValidatePath(invalidPath);
        act.Should().Throw<PathSecurityException>()
            .WithMessage("*traversal*");
    }

    [Theory]
    [InlineData("/root/file.wav")]
    [InlineData("\\root\\file.wav")]
    public void ValidatePath_ThrowsOnLeadingSeparators(string invalidPath)
    {
        var act = () => PathSecurityGuard.ValidatePath(invalidPath);
        act.Should().Throw<PathSecurityException>()
            .WithMessage("*relative*");
    }

    [Theory]
    [InlineData("C:\\Windows\\file.wav")]
    [InlineData("D:/Project/file.wav")]
    public void ValidatePath_ThrowsOnDriveLetters(string invalidPath)
    {
        var act = () => PathSecurityGuard.ValidatePath(invalidPath);
        act.Should().Throw<PathSecurityException>()
            .WithMessage("*drive*");
    }

    [Theory]
    [InlineData("Track<1>.flp")]
    [InlineData("Audio|Vocal.wav")]
    [InlineData("Sample?1.wav")]
    public void ValidatePath_ThrowsOnInvalidChars(string invalidPath)
    {
        var act = () => PathSecurityGuard.ValidatePath(invalidPath);
        act.Should().Throw<PathSecurityException>()
            .WithMessage("*invalid*");
    }

    [Fact]
    public void ValidateAll_ThrowsOnCaseCollision()
    {
        var paths = new[]
        {
            "Samples/Kick.wav",
            "samples/kick.wav"
        };

        var act = () => PathSecurityGuard.ValidateAll(paths);
        act.Should().Throw<PathSecurityException>()
            .WithMessage("*Case collision*");
    }

    [Fact]
    public void ValidateAll_PassesOnValidUniquePaths()
    {
        var paths = new[]
        {
            "Project.flp",
            "Audio/Vocal.wav",
            "Samples/Kick.wav",
            "Samples/Snare.wav"
        };

        var act = () => PathSecurityGuard.ValidateAll(paths);
        act.Should().NotThrow();
    }
}
