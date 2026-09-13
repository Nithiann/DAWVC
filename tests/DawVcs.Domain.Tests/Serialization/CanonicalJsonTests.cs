using System.Text;

using DawVcs.Domain.Hashing;
using DawVcs.Domain.Serialization;
using DawVcs.Domain.Tests.Common;

using FluentAssertions;

using Xunit;

namespace DawVcs.Domain.Tests.Serialization;

public class CanonicalJsonTests
{
    private sealed record TestModel(string Zeta, string Alpha, int Beta, InnerModel? Nested = null);
    private sealed record InnerModel(string Y, string X);

    [Fact]
    [Requirement("FR-OBJ-008")]
    public void SerializeCanonical_SortsObjectPropertiesAlphabetically()
    {
        // Arrange
        var model = new TestModel(
            Zeta: "last",
            Alpha: "first",
            Beta: 42,
            Nested: new InnerModel(Y: "valY", X: "valX"));

        // Act
        var bytes = CanonicalJsonSerializer.SerializeCanonical(model);
        var json = Encoding.UTF8.GetString(bytes);

        // Assert: Properties must appear in sorted order: alpha, beta, nested (x, y), zeta
        json.Should().Be("{\"alpha\":\"first\",\"beta\":42,\"nested\":{\"x\":\"valX\",\"y\":\"valY\"},\"zeta\":\"last\"}");
    }

    [Fact]
    [Requirement("FR-OBJ-008")]
    public void SerializeCanonical_ProducesDeterministicContentHash()
    {
        // Arrange
        var model1 = new TestModel("Z", "A", 1);
        var model2 = new TestModel("Z", "A", 1);

        // Act
        var bytes1 = CanonicalJsonSerializer.SerializeCanonical(model1);
        var bytes2 = CanonicalJsonSerializer.SerializeCanonical(model2);

        var hash1 = Blake3ContentHasher.Hash(bytes1);
        var hash2 = Blake3ContentHasher.Hash(bytes2);

        // Assert
        hash1.Should().Be(hash2);
    }

    [Fact]
    [Requirement("FR-OBJ-008")]
    public void Deserialize_CanonicalBytes_RestoresObject()
    {
        // Arrange
        var original = new TestModel("Z", "A", 99, new InnerModel("y", "x"));
        var bytes = CanonicalJsonSerializer.SerializeCanonical(original);

        // Act
        var deserialized = CanonicalJsonSerializer.Deserialize<TestModel>(bytes);

        // Assert
        deserialized.Should().BeEquivalentTo(original);
    }
}
