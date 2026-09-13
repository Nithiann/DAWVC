using DawVcs.Domain.Common;
using DawVcs.Domain.Hashing;

using FluentAssertions;

using Xunit;

namespace DawVcs.Domain.Tests.Common;

public class ValueObjectTests
{
    [Fact]
    [Requirement("INV-002")]
    public void RepositoryId_NewAndParse_RoundTripsSuccessfully()
    {
        var id = RepositoryId.New();
        var str = id.ToString();

        var parsed = RepositoryId.Parse(str);
        parsed.Should().Be(id);
        RepositoryId.TryParse(str, out var tryParsed).Should().BeTrue();
        tryParsed.Should().Be(id);
    }

    [Fact]
    [Requirement("INV-002")]
    public void RepositoryId_EmptyGuid_ThrowsArgumentException()
    {
        var act = () => new RepositoryId(Guid.Empty);
        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [Requirement("FR-BRA-003")]
    [InlineData("main")]
    [InlineData("feature/vocals")]
    [InlineData("mix-v2")]
    [InlineData("producer_edit.1")]
    public void BranchName_ValidInputs_ConstructsSuccessfully(string validName)
    {
        var branch = new BranchName(validName);
        branch.Value.Should().Be(validName);
        branch.ToString().Should().Be(validName);
    }

    [Theory]
    [Requirement("FR-BRA-003")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("/leading/slash")]
    [InlineData("trailing/slash/")]
    [InlineData("double//slash")]
    [InlineData("traversal/../escape")]
    [InlineData("invalid char")]
    [InlineData("invalid~char")]
    [InlineData("invalid^char")]
    [InlineData("invalid:char")]
    [InlineData("branch.lock")]
    [InlineData("branch.")]
    public void BranchName_InvalidInputs_ThrowsArgumentException(string invalidName)
    {
        var act = () => new BranchName(invalidName);
        act.Should().Throw<ArgumentException>();

        BranchName.TryCreate(invalidName, out _, out var error).Should().BeFalse();
        error.Should().NotBeNullOrEmpty();
    }

    [Fact]
    [Requirement("INV-002")]
    public void StronglyTypedIds_Commit_Snapshot_Blob_WrapContentHashCorrectly()
    {
        var hash = ContentHash.Parse("af1349b9f5f9a1a6a0404dea36dcc9499bcb25c9adc112b7cc9a93cae41f3262");

        var commitId = new CommitId(hash);
        var snapshotId = new SnapshotId(hash);
        var blobId = new BlobId(hash);

        commitId.Value.Should().Be(hash);
        snapshotId.Value.Should().Be(hash);
        blobId.Value.Should().Be(hash);

        commitId.ToString().Should().Be(hash.ToString());
    }
}
