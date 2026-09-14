using DawVcs.Application.Common;

using FluentAssertions;

using Xunit;

namespace DawVcs.Application.Tests.Common;

public sealed class PathRedactorTests
{
    [Fact]
    public void Redact_WindowsUserProfile_ReplacesUsernameWithUserPlaceholder()
    {
        var path = @"C:\Users\SecretArtist\Documents\DAWVC\Projects\Track.flp";
        var redacted = PathRedactor.Redact(path);

        redacted.Should().Be(@"C:\Users\<user>\Documents\DAWVC\Projects\Track.flp");
        redacted.Should().NotContain("SecretArtist");
    }

    [Fact]
    public void Redact_UnixUserProfile_ReplacesUsernameWithUserPlaceholder()
    {
        var path = "/home/producer/studio/beats/Snare.wav";
        var redacted = PathRedactor.Redact(path);

        redacted.Should().Be("/home/<user>/studio/beats/Snare.wav");
        redacted.Should().NotContain("producer");
    }

    [Fact]
    public void Redact_UrlWithCredentials_RemovesCredentials()
    {
        var url = "https://admin:SuperSecretPassword123@my-repo.internal/dawvc.git";
        var redacted = PathRedactor.Redact(url);

        redacted.Should().Be("https://<redacted>@my-repo.internal/dawvc.git");
        redacted.Should().NotContain("SuperSecretPassword123");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Redact_EmptyOrNull_ReturnsEmptyString(string? input)
    {
        var redacted = PathRedactor.Redact(input);
        redacted.Should().BeEmpty();
    }
}
