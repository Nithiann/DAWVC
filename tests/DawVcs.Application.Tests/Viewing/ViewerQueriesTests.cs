using DawVcs.Application.Commits;
using DawVcs.Application.Viewing;
using DawVcs.Domain.Artifacts;
using DawVcs.Domain.Common;
using DawVcs.Domain.Dependencies;
using DawVcs.Domain.Entities;
using DawVcs.Domain.Hashing;
using DawVcs.Domain.Repositories;

using FluentAssertions;

using NSubstitute;

using Xunit;

namespace DawVcs.Application.Tests.Viewing;

public sealed class ViewerQueriesTests
{
    [Fact]
    public async Task HistoryCursor_IsBoundedAndMatchesExistingLogWithoutSkippingCommits()
    {
        var context = Substitute.For<IRepositoryContext>();
        var snapshot = new SnapshotId(new ContentHash(new byte[32]));
        var first = new Commit([], snapshot, "A", DateTimeOffset.UnixEpoch, "first");
        var second = new Commit([first.Id], snapshot, "A", DateTimeOffset.UnixEpoch.AddSeconds(1), "second");
        var third = new Commit([second.Id], snapshot, "A", DateTimeOffset.UnixEpoch.AddSeconds(2), "third");
        context.GetCurrentBranch().Returns(BranchName.Main);
        context.GetBranchCommit(BranchName.Main).Returns(third.Id);
        foreach (var commit in new[] { first, second, third }) context.LoadCommitAsync(commit.Id, Arg.Any<CancellationToken>()).Returns(commit);
        var queries = new ViewerQueries(_ => context);
        var page = await queries.HistoryAsync("repo", pageSize: 2);
        page.Entries.Select(e => e.Id).Should().Equal(third.Id, second.Id);
        page.Next.Should().Be(first.Id);
        var next = await queries.HistoryAsync("repo", page.Next, 2);
        next.Entries.Select(e => e.Id).Should().Equal(first.Id);
        next.Next.Should().BeNull();
        var log = await new LogUseCase(_ => context).ExecuteAsync(new LogRequest("repo", 3));
        page.Entries.Concat(next.Entries).Should().BeEquivalentTo(log.Entries, options => options.WithStrictOrdering());
        _ = context.DidNotReceive().StagingIndex;
    }

    [Fact]
    public async Task SnapshotFilesAndDependencies_ArePagedWithoutLocators()
    {
        var context = Substitute.For<IRepositoryContext>();
        var hash = new ContentHash(new byte[32]);
        var entries = Enumerable.Range(0, 5).Select(i => ArtifactEntry.Create(new ArtifactPath($"Asset{i}.wav"), hash, 4)).ToArray();
        var dependencies = new DependencyGraph([new AssetDependency(new DependencyId("asset"), @"C:\Users\secret\Kick.wav", DependencyRequirement.Required,
            DependencySource.NativeProjectParser, PortabilityPolicy.ReferenceOnlyDefault, originalPath: @"C:\Users\secret\Kick.wav", isMissing: true)]);
        var snapshot = new ProjectSnapshot(new ProjectArtifact("FL Studio", new DirectoryArtifact(entries)), DateTimeOffset.UnixEpoch, dependencies: dependencies);
        var commit = new Commit([], snapshot.Id, "A", DateTimeOffset.UnixEpoch, "M");
        context.LoadCommitAsync(commit.Id, Arg.Any<CancellationToken>()).Returns(commit);
        context.LoadSnapshotAsync(snapshot.Id, Arg.Any<CancellationToken>()).Returns(snapshot);
        var queries = new ViewerQueries(_ => context);
        var files = await queries.CommitAsync("repo", commit.Id, 0, 2);
        files.TotalFiles.Should().Be(5);
        files.Files.Should().HaveCount(2);
        files.NextOffset.Should().Be(2);
        (await queries.CommitAsync("repo", commit.Id, 4, 2)).NextOffset.Should().BeNull();
        var page = await queries.DependenciesAsync("repo", commit.Id);
        page.Entries.Should().ContainSingle().Which.Name.Should().Be("Kick.wav");
        page.Entries[0].Status.Should().Be("Missing");
        System.Text.Json.JsonSerializer.Serialize(page).Should().NotContain("secret");
    }

    [Fact]
    public async Task InvalidBoundsAndPreCancellation_DoNotCreateAContext()
    {
        bool called = false;
        var queries = new ViewerQueries(_ => { called = true; throw new InvalidOperationException(); });
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => queries.HistoryAsync("repo", pageSize: 101));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => queries.HistoryAsync("repo", cancellationToken: cancellation.Token));
        called.Should().BeFalse();
    }
}
