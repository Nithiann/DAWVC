using DawVcs.Application.Commits;
using DawVcs.Domain.Common;
using DawVcs.Domain.Entities;
using DawVcs.Domain.Hashing;
using DawVcs.Domain.Repositories;

using FluentAssertions;

using NSubstitute;

using Xunit;

namespace DawVcs.Application.Tests.Commits;

public sealed class LogUseCaseTests
{
    private readonly IRepositoryContext _context;

    public LogUseCaseTests()
    {
        _context = Substitute.For<IRepositoryContext>();
        _context.GetCurrentBranch().Returns(BranchName.Main);
    }

    [Fact]
    public async Task Log_WhenNoCommits_ReturnsEmptyList()
    {
        _context.GetBranchCommit(BranchName.Main).Returns((CommitId?)null);

        var useCase = new LogUseCase(_ => _context);
        var result = await useCase.ExecuteAsync(new LogRequest("dummyDir"));

        result.Entries.Should().BeEmpty();
    }

    [Fact]
    public async Task Log_WithHistory_TraversesParentsAndAppliesLimit()
    {
        var dummySnapshotId = new SnapshotId(new ContentHash(new byte[32]));

        var commit1 = new Commit(
            Array.Empty<CommitId>(),
            dummySnapshotId,
            "Producer",
            DateTimeOffset.UtcNow.AddMinutes(-20),
            "Initial commit");

        var commit2 = new Commit(
            new[] { commit1.Id },
            dummySnapshotId,
            "Producer",
            DateTimeOffset.UtcNow.AddMinutes(-10),
            "Second commit");

        var commit3 = new Commit(
            new[] { commit2.Id },
            dummySnapshotId,
            "Producer",
            DateTimeOffset.UtcNow,
            "Third commit");

        _context.GetBranchCommit(BranchName.Main).Returns(commit3.Id);
        _context.LoadCommitAsync(commit3.Id, Arg.Any<CancellationToken>()).Returns(commit3);
        _context.LoadCommitAsync(commit2.Id, Arg.Any<CancellationToken>()).Returns(commit2);
        _context.LoadCommitAsync(commit1.Id, Arg.Any<CancellationToken>()).Returns(commit1);

        var useCase = new LogUseCase(_ => _context);

        // Unlimited log
        var allLog = await useCase.ExecuteAsync(new LogRequest("dummyDir"));
        allLog.Entries.Should().HaveCount(3);
        allLog.Entries[0].Id.Should().Be(commit3.Id);
        allLog.Entries[1].Id.Should().Be(commit2.Id);
        allLog.Entries[2].Id.Should().Be(commit1.Id);

        // Limited log
        var limitedLog = await useCase.ExecuteAsync(new LogRequest("dummyDir", Limit: 2));
        limitedLog.Entries.Should().HaveCount(2);
        limitedLog.Entries[0].Id.Should().Be(commit3.Id);
        limitedLog.Entries[1].Id.Should().Be(commit2.Id);
    }
}
