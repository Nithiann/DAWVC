using System.Text;

using DawVcs.Domain.Hashing;
using DawVcs.Domain.Serialization;

using FluentAssertions;

using Xunit;

namespace DawVcs.Domain.Tests.Serialization;

public sealed class CanonicalJsonFuzzTests
{
    [Fact]
    public void SerializeCanonical_WithUnicodeAndEmojis_ProducesByteIdenticalOutputAcrossRuns()
    {
        var complexObj = new
        {
            title = "FL Studio Track 🎵🎹🔥",
            artist = "Björk & Sigur Rós / モンスター",
            description = "Special \t tabs, \r carriage, \n newlines and \u0000 characters."
        };

        var bytes1 = CanonicalJsonSerializer.SerializeCanonical(complexObj);
        var bytes2 = CanonicalJsonSerializer.SerializeCanonical(complexObj);

        bytes1.Should().Equal(bytes2);
        var hash1 = Blake3ContentHasher.Hash(bytes1);
        var hash2 = Blake3ContentHasher.Hash(bytes2);
        hash1.Should().Be(hash2);
    }

    [Fact]
    public void SerializeCanonical_SortsKeysLexicographically()
    {
        var objA = new { z = 1, a = 2, m = 3 };
        var objB = new { a = 2, m = 3, z = 1 };

        var bytesA = CanonicalJsonSerializer.SerializeCanonical(objA);
        var bytesB = CanonicalJsonSerializer.SerializeCanonical(objB);

        bytesA.Should().Equal(bytesB);
        var json = Encoding.UTF8.GetString(bytesA);
        json.IndexOf("\"a\":", StringComparison.Ordinal).Should().BeLessThan(json.IndexOf("\"m\":", StringComparison.Ordinal));
        json.IndexOf("\"m\":", StringComparison.Ordinal).Should().BeLessThan(json.IndexOf("\"z\":", StringComparison.Ordinal));
    }

    [Fact]
    public void SerializeCanonical_WithDeeplyNestedStructures_RoundtripsDeterministically()
    {
        var nested = new
        {
            level1 = new
            {
                level2 = new
                {
                    level3 = new
                    {
                        items = new object[] { "one", 2, true, new { inner = "value" } }
                    }
                }
            }
        };

        var bytes = CanonicalJsonSerializer.SerializeCanonical(nested);
        bytes.Should().NotBeEmpty();

        var hashA = Blake3ContentHasher.Hash(bytes);
        var hashB = Blake3ContentHasher.Hash(CanonicalJsonSerializer.SerializeCanonical(nested));
        hashA.Should().Be(hashB);
    }
}
