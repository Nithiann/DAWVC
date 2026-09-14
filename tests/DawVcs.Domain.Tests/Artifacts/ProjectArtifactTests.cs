using DawVcs.Domain.Artifacts;
using DawVcs.Domain.Hashing;
using DawVcs.Domain.Tests.Common;

using FluentAssertions;

using Xunit;

namespace DawVcs.Domain.Tests.Artifacts;

public class ProjectArtifactTests
{
    [Fact]
    [Requirement("INV-001")]
    public void SingleFileArtifact_ProducesDeterministicAggregateHash()
    {
        // Arrange
        var hash = ContentHash.Parse("af1349b9f5f9a1a6a0404dea36dcc9499bcb25c9adc112b7cc9a93cae41f3262");
        var path = new ArtifactPath("Project.flp");

        // Act
        var artifact1 = ProjectArtifact.CreateSingleFile("FL Studio", path, hash, 1024);
        var artifact2 = ProjectArtifact.CreateSingleFile("FL Studio", path, hash, 1024);

        // Assert
        artifact1.AggregateHash.Should().Be(artifact2.AggregateHash);
        artifact1.Root.Kind.Should().Be(ArtifactKind.SingleFile);
        artifact1.Root.GetEntries().Should().ContainSingle();
    }

    [Fact]
    [Requirement("INV-001")]
    public void DirectoryArtifact_IndependentOfEntryOrder_ProducesSameAggregateHash()
    {
        // Arrange
        var hashA = ContentHash.Parse("1111111111111111111111111111111111111111111111111111111111111111");
        var hashB = ContentHash.Parse("2222222222222222222222222222222222222222222222222222222222222222");

        var entry1 = ArtifactEntry.Create(new ArtifactPath("A_Project.als"), hashA, 500, ArtifactRole.PrimaryProjectFile);
        var entry2 = ArtifactEntry.Create(new ArtifactPath("B_Samples/Kick.wav"), hashB, 1000, ArtifactRole.ProjectAsset);

        // Order 1: entry1, entry2
        var root1 = new DirectoryArtifact([entry1, entry2]);
        var artifact1 = new ProjectArtifact("Ableton Live", root1);

        // Order 2: entry2, entry1 (reversed insertion order)
        var root2 = new DirectoryArtifact([entry2, entry1]);
        var artifact2 = new ProjectArtifact("Ableton Live", root2);

        // Assert: Canonical sorting guarantees exact aggregate hash match
        artifact1.AggregateHash.Should().Be(artifact2.AggregateHash);
    }

    [Fact]
    [Requirement("INV-001")]
    public void DirectoryArtifact_ModifiedContent_ChangesAggregateHash()
    {
        // Arrange
        var hashA = ContentHash.Parse("1111111111111111111111111111111111111111111111111111111111111111");
        var hashB = ContentHash.Parse("2222222222222222222222222222222222222222222222222222222222222222");
        var hashBModified = ContentHash.Parse("3333333333333333333333333333333333333333333333333333333333333333");

        var entry1 = ArtifactEntry.Create(new ArtifactPath("A.txt"), hashA, 100);
        var entry2 = ArtifactEntry.Create(new ArtifactPath("B.txt"), hashB, 200);
        var entry2Mod = ArtifactEntry.Create(new ArtifactPath("B.txt"), hashBModified, 200);

        var rootOriginal = new DirectoryArtifact([entry1, entry2]);
        var rootModified = new DirectoryArtifact([entry1, entry2Mod]);

        // Assert
        rootOriginal.ComputeAggregateHash().Should().NotBe(rootModified.ComputeAggregateHash());
    }
}
