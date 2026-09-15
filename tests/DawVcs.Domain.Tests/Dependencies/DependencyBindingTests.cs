using DawVcs.Domain.Dependencies;
using DawVcs.Domain.Hashing;

using FluentAssertions;

using Xunit;

namespace DawVcs.Domain.Tests.Dependencies;

public class DependencyBindingTests
{
    [Fact]
    public void Constructor_SetsPropertiesCorrectly()
    {
        var hash = Blake3ContentHasher.Hash(new byte[] { 1, 2, 3 });
        var depId = DependencyId.ForAsset(hash);
        var now = DateTimeOffset.UtcNow;

        var binding = new DependencyBinding(
            depId,
            "C:\\Samples\\Kick.wav",
            BindingMethod.RelativePath,
            BindingStatus.Verified,
            hash,
            now,
            "Local test binding");

        binding.DependencyId.Should().Be(depId);
        binding.Locator.Should().Be("C:\\Samples\\Kick.wav");
        binding.Method.Should().Be(BindingMethod.RelativePath);
        binding.Status.Should().Be(BindingStatus.Verified);
        binding.VerifiedHash.Should().Be(hash);
        binding.BoundAt.Should().Be(now);
        binding.Notes.Should().Be("Local test binding");
    }

    [Fact]
    public void WithStatus_UpdatesStatusAndTimestamp()
    {
        var depId = DependencyId.ForUnresolvedAsset("Samples/Snare.wav");
        var originalTime = DateTimeOffset.UtcNow.AddHours(-1);
        var binding = new DependencyBinding(
            depId,
            "C:\\Samples\\Snare.wav",
            BindingMethod.OriginalPath,
            BindingStatus.Unresolved,
            null,
            originalTime);

        var newHash = Blake3ContentHasher.Hash(new byte[] { 4, 5, 6 });
        var updated = binding.WithStatus(BindingStatus.Verified, newHash);

        updated.Status.Should().Be(BindingStatus.Verified);
        updated.VerifiedHash.Should().Be(newHash);
        updated.BoundAt.Should().BeAfter(originalTime);
        updated.Locator.Should().Be(binding.Locator);
    }

    [Fact]
    public void ValueEquality_WorksAsExpected()
    {
        var hash = Blake3ContentHasher.Hash(new byte[] { 7, 8, 9 });
        var depId = DependencyId.ForAsset(hash);
        var now = DateTimeOffset.UtcNow;

        var b1 = new DependencyBinding(depId, "D:\\Hat.wav", BindingMethod.ContentHashDiscovery, BindingStatus.Verified, hash, now);
        var b2 = new DependencyBinding(depId, "D:\\Hat.wav", BindingMethod.ContentHashDiscovery, BindingStatus.Verified, hash, now);

        b1.Should().Be(b2);
    }
}
