using DawVcs.Domain.Common;
using DawVcs.Domain.Repositories;
using DawVcs.Infrastructure.Repositories;

using FluentAssertions;

using Xunit;

namespace DawVcs.Infrastructure.Tests.Repositories;

public sealed class FileSystemRepositoryContextBranchTests : IDisposable
{
    private readonly string _tempDir;
    private readonly FileSystemRepositoryContext _context;

    public FileSystemRepositoryContextBranchTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "dawvc-branch-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _context = new FileSystemRepositoryContext(_tempDir);
        _context.SetCurrentBranch(BranchName.Main);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            try
            {
                Directory.Delete(_tempDir, true);
            }
            catch
            {
                // Best effort cleanup in test temp folder
            }
        }
    }

    [Fact]
    public async Task CreateBranch_NestedSubdirectories_SucceedsAndListsCorrectly()
    {
        // Arrange
        var commit1 = CommitId.Parse(new string('a', 64));
        var commit2 = CommitId.Parse(new string('b', 64));
        var branchName1 = new BranchName("feature/vocals");
        var branchName2 = new BranchName("user/voss/drums");

        // Act - Create nested branches
        _context.CreateBranch(branchName1, commit1);
        _context.CreateBranch(branchName2, commit2);

        // Assert - GetBranches lists both with correct forward slashes
        var branches = _context.GetBranches();
        branches.Should().Contain(b => b.Name.Value == "feature/vocals" && b.CommitId == commit1);
        branches.Should().Contain(b => b.Name.Value == "user/voss/drums" && b.CommitId == commit2);

        // Assert - Lookup by branch name
        _context.GetBranchCommit(branchName1).Should().Be(commit1);
        _context.GetBranchCommit(branchName2).Should().Be(commit2);

        // Act - Update branch commit
        var commitUpdated = CommitId.Parse(new string('c', 64));
        await _context.UpdateBranchCommitAsync(branchName1, commitUpdated);
        _context.GetBranchCommit(branchName1).Should().Be(commitUpdated);

        // Act - Delete nested branch
        var deleted = _context.DeleteBranch(branchName1);
        deleted.Should().BeTrue();

        _context.GetBranches().Should().NotContain(b => b.Name.Value == "feature/vocals");
        _context.GetBranchCommit(branchName1).Should().BeNull();

        // Second branch still remains intact
        _context.GetBranchCommit(branchName2).Should().Be(commit2);
    }

    [Fact]
    public void GetCurrentBranch_WhenHeadFileMissing_ThrowsInvalidOperationException()
    {
        var emptyDir = Path.Combine(Path.GetTempPath(), "dawvc-empty-head-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(emptyDir);
        try
        {
            var ctx = new FileSystemRepositoryContext(emptyDir);
            var act = () => ctx.GetCurrentBranch();

            act.Should().Throw<InvalidOperationException>()
                .WithMessage("*HEAD file is missing*");
        }
        finally
        {
            try { Directory.Delete(emptyDir, true); } catch { }
        }
    }

    [Fact]
    public void GetCurrentBranch_WhenHeadFileCorrupt_ThrowsInvalidOperationException()
    {
        // Arrange: Write corrupt HEAD file (not ref: refs/heads/...)
        var dotDawvc = Path.Combine(_tempDir, ".dawvc");
        Directory.CreateDirectory(dotDawvc);
        File.WriteAllText(Path.Combine(dotDawvc, "HEAD"), "GARBAGE_HEAD_CONTENT\n");

        // Act
        var act = () => _context.GetCurrentBranch();

        // Assert
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Invalid HEAD reference*");
    }

    [Fact]
    public void SetCurrentBranch_WritesHeadAtomically_AndGetCurrentBranchReadsIt()
    {
        // Act
        _context.SetCurrentBranch(new BranchName("feature/atomic"));

        // Assert
        _context.GetCurrentBranch().Value.Should().Be("feature/atomic");

        var headPath = Path.Combine(_tempDir, ".dawvc", "HEAD");
        File.ReadAllText(headPath).Trim().Should().Be("ref: refs/heads/feature/atomic");
    }
}
