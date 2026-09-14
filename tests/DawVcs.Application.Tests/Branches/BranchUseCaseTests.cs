using DawVcs.Application.Branches;
using DawVcs.Domain.Common;
using DawVcs.Domain.Hashing;
using DawVcs.Domain.Repositories;

using FluentAssertions;

using NSubstitute;

using Xunit;

namespace DawVcs.Application.Tests.Branches;

public sealed class BranchUseCaseTests
{
    private readonly IRepositoryContext _context;
    private readonly BranchUseCase _useCase;

    public BranchUseCaseTests()
    {
        _context = Substitute.For<IRepositoryContext>();
        _useCase = new BranchUseCase(_ => _context);
    }

    [Fact]
    public async Task ListAsync_ReturnsBranchesFromContext()
    {
        var main = BranchName.Main;
        var feature = new BranchName("feature");
        var commitId = new CommitId(Blake3ContentHasher.Hash(new byte[] { 1, 2, 3 }));

        var expected = new List<BranchInfo>
        {
            new(main, commitId, true),
            new(feature, commitId, false)
        };

        _context.GetBranches().Returns(expected);

        var result = await _useCase.ListAsync("dummyDir");

        result.Should().BeEquivalentTo(expected);
    }

    [Fact]
    public async Task CreateAsync_WithValidName_CreatesBranchAtCurrentHead()
    {
        var currentBranch = BranchName.Main;
        var commitId = new CommitId(Blake3ContentHasher.Hash(new byte[] { 1 }));

        _context.GetCurrentBranch().Returns(currentBranch);
        _context.GetBranchCommit(currentBranch).Returns(commitId);

        var result = await _useCase.CreateAsync(new BranchCreateRequest("dummyDir", "experiment"));

        result.Name.Value.Should().Be("experiment");
        result.CommitId.Should().Be(commitId);
        _context.Received(1).CreateBranch(Arg.Is<BranchName>(b => b.Value == "experiment"), commitId);
    }

    [Fact]
    public async Task CreateAsync_WithInvalidName_ThrowsArgumentException()
    {
        var act = () => _useCase.CreateAsync(new BranchCreateRequest("dummyDir", "invalid branch name with spaces"));

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task CreateAsync_WhenCurrentBranchHasNoCommits_ThrowsInvalidOperationException()
    {
        var currentBranch = BranchName.Main;
        _context.GetCurrentBranch().Returns(currentBranch);
        _context.GetBranchCommit(currentBranch).Returns((CommitId?)null);

        var act = () => _useCase.CreateAsync(new BranchCreateRequest("dummyDir", "feature"));

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*no commits*");
    }

    [Fact]
    public async Task DeleteAsync_WhenDeletingActiveBranch_ThrowsInvalidOperationException()
    {
        var currentBranch = BranchName.Main;
        _context.GetCurrentBranch().Returns(currentBranch);

        var act = () => _useCase.DeleteAsync(new BranchDeleteRequest("dummyDir", "main"));

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Cannot delete the currently active branch*");
    }

    [Fact]
    public async Task DeleteAsync_WhenValid_CallsDeleteOnContext()
    {
        _context.GetCurrentBranch().Returns(BranchName.Main);
        _context.DeleteBranch(Arg.Any<BranchName>()).Returns(true);

        var result = await _useCase.DeleteAsync(new BranchDeleteRequest("dummyDir", "old-feature"));

        result.Should().BeTrue();
        _context.Received(1).DeleteBranch(Arg.Is<BranchName>(b => b.Value == "old-feature"));
    }
}
