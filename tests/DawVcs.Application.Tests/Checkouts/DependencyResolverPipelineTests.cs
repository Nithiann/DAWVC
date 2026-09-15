using DawVcs.Application.Dependencies;
using DawVcs.Domain.Artifacts;
using DawVcs.Domain.Dependencies;
using DawVcs.Domain.Hashing;

using FluentAssertions;

using Xunit;

namespace DawVcs.Application.Tests.Checkouts;

public sealed class DependencyResolverPipelineTests : IDisposable
{
    private readonly string _tempDir;

    public DependencyResolverPipelineTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "dawvc-resolver-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            try
            {
                Directory.Delete(_tempDir, recursive: true);
            }
            catch
            {
            }
        }
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task ResolveAsync_MatchingRelativeAsset_ReturnsVerified()
    {
        var sampleBytes = new byte[] { 10, 20, 30, 40 };
        var sampleHash = Blake3ContentHasher.Hash(sampleBytes);
        var relPath = Path.Combine("Audio", "Vocal.wav");
        var fullPath = Path.Combine(_tempDir, relPath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        await File.WriteAllBytesAsync(fullPath, sampleBytes);

        var dep = new AssetDependency(
            DependencyId.ForAsset(sampleHash),
            "Vocal.wav",
            DependencyRequirement.Required,
            DependencySource.NativeProjectParser,
            PortabilityPolicy.BundleDefault,
            new ArtifactPath("Audio/Vocal.wav"),
            fullPath,
            sampleHash,
            sampleBytes.Length,
            false);

        var binding = await DependencyResolverPipeline.ResolveAsync(_tempDir, dep);

        binding.Status.Should().Be(BindingStatus.Verified);
        binding.Method.Should().Be(BindingMethod.RepositoryAsset);
        binding.VerifiedHash.Should().Be(sampleHash);
        binding.Locator.Should().Be(fullPath);
    }

    [Fact]
    public async Task ResolveAsync_ModifiedFileAtRelativePath_ReturnsMismatch()
    {
        // Bestand op schijf heeft andere inhoud dan de snapshot verwacht (FR-BND-005)
        var expectedBytes = new byte[] { 1, 2, 3, 4 };
        var expectedHash = Blake3ContentHasher.Hash(expectedBytes);

        var actualBytes = new byte[] { 99, 98, 97 };
        var relPath = Path.Combine("Audio", "Vocal.wav");
        var fullPath = Path.Combine(_tempDir, relPath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        await File.WriteAllBytesAsync(fullPath, actualBytes);

        var dep = new AssetDependency(
            DependencyId.ForAsset(expectedHash),
            "Vocal.wav",
            DependencyRequirement.Required,
            DependencySource.NativeProjectParser,
            PortabilityPolicy.BundleDefault,
            new ArtifactPath("Audio/Vocal.wav"),
            fullPath,
            expectedHash,
            expectedBytes.Length,
            false);

        var binding = await DependencyResolverPipeline.ResolveAsync(_tempDir, dep);

        binding.Status.Should().Be(BindingStatus.Mismatch);
        binding.Method.Should().Be(BindingMethod.RelativePath);
        binding.Notes.Should().Contain("hash does not match");
    }

    [Fact]
    public async Task ResolveAsync_SampleMovedToSubfolder_DiscoversViaContentHash()
    {
        // Bestand is verplaatst naar een andere submap maar heeft gelijke hash (FR-BND-007)
        var sampleBytes = new byte[] { 55, 66, 77, 88 };
        var sampleHash = Blake3ContentHasher.Hash(sampleBytes);

        var movedPath = Path.Combine(_tempDir, "MovedFolder", "Sub", "RenamedSample.wav");
        Directory.CreateDirectory(Path.GetDirectoryName(movedPath)!);
        await File.WriteAllBytesAsync(movedPath, sampleBytes);

        var dep = new AssetDependency(
            DependencyId.ForAsset(sampleHash),
            "OldPath.wav",
            DependencyRequirement.Required,
            DependencySource.NativeProjectParser,
            PortabilityPolicy.BundleDefault,
            new ArtifactPath("Original/OldPath.wav"),
            null,
            sampleHash,
            sampleBytes.Length,
            false);

        var binding = await DependencyResolverPipeline.ResolveAsync(_tempDir, dep);

        binding.Status.Should().Be(BindingStatus.Verified);
        binding.Method.Should().Be(BindingMethod.ContentHashDiscovery);
        binding.Locator.Should().Be(movedPath);
        binding.VerifiedHash.Should().Be(sampleHash);
    }

    [Fact]
    public async Task ResolveAsync_MissingAsset_ReturnsUnresolved()
    {
        var dummyHash = Blake3ContentHasher.Hash(new byte[] { 1 });
        var dep = new AssetDependency(
            DependencyId.ForAsset(dummyHash),
            "Sample.wav",
            DependencyRequirement.Required,
            DependencySource.NativeProjectParser,
            PortabilityPolicy.BundleDefault,
            new ArtifactPath("Missing/Sample.wav"),
            null,
            dummyHash,
            1,
            true);

        var binding = await DependencyResolverPipeline.ResolveAsync(_tempDir, dep);

        binding.Status.Should().Be(BindingStatus.Unresolved);
        binding.Method.Should().Be(BindingMethod.Unresolved);
    }
}
