using DawVcs.Desktop.Services;

using FluentAssertions;

using Xunit;

namespace DawVcs.Desktop.Tests;

public sealed class RecentRepositoriesTests
{
    [Fact]
    public async Task Storage_IsBoundedDeduplicatedAndSeparateFromRepository()
    {
        using var directories = new TestRepositories();
        var path = Path.Combine(directories.Root, "preferences", "recent.json");
        var store = new RecentRepositories(path);
        (await store.LoadAsync(TestContext.Current.CancellationToken)).Should().BeEmpty();
        var roots = Enumerable.Range(0, 30).Select(i => new RecentRepository(Path.Combine(directories.Root, $"repo-{i}"))).ToArray();
        await store.SaveAsync([roots[0], .. roots], TestContext.Current.CancellationToken);
        var loaded = await store.LoadAsync(TestContext.Current.CancellationToken);
        loaded.Should().HaveCount(20);
        loaded[0].Should().Be(roots[0]);
        Directory.Exists(Path.Combine(directories.Root, "repo-0")).Should().BeFalse();
        Directory.GetFiles(Path.GetDirectoryName(path)!).Should().ContainSingle();
    }

    [Fact]
    public async Task CancelledWrite_PreservesPreviousPreferences()
    {
        using var directories = new TestRepositories();
        var path = Path.Combine(directories.Root, "preferences", "recent.json");
        var store = new RecentRepositories(path);
        var root = new RecentRepository(Path.Combine(directories.Root, "original"));
        await store.SaveAsync([root], TestContext.Current.CancellationToken);
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        var act = () => store.SaveAsync([], cancelled.Token);
        await act.Should().ThrowAsync<OperationCanceledException>();
        (await store.LoadAsync(TestContext.Current.CancellationToken)).Should().ContainSingle().Which.Should().Be(root);
    }
    [Fact]
    public void DefaultPath_IsAbsoluteAndOutsideWorkingRepository()
    {
        Path.IsPathFullyQualified(RecentRepositories.DefaultPath).Should().BeTrue();
        RecentRepositories.DefaultPath.Should().NotStartWith(Directory.GetCurrentDirectory() + Path.DirectorySeparatorChar);
    }

}
