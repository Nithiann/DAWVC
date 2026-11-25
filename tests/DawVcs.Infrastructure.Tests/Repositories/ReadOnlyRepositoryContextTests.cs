using DawVcs.Domain.Hashing;
using DawVcs.Infrastructure.Repositories;

using FluentAssertions;

using Microsoft.Data.Sqlite;

using Xunit;

namespace DawVcs.Infrastructure.Tests.Repositories;

public sealed class ReadOnlyRepositoryContextTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "dawvc-read-context-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void ConstructionReadsAndDisposal_DoNotCreateMissingRepositoryDirectories()
    {
        using (var context = new FileSystemRepositoryContext(_root))
        {
            context.GetRemotes().Should().BeEmpty();
            context.ObjectStore.Exists(new ContentHash(new byte[32])).Should().BeFalse();
            context.ObjectStore.EnumerateStoredObjects().Should().BeEmpty();
            Action readConfig = () => context.LoadConfig();
            readConfig.Should().Throw<FileNotFoundException>();
        }
        Directory.Exists(_root).Should().BeFalse();
    }

    [Fact]
    public void DisposedContext_CannotCreateALazyIndexAfterItsLifetime()
    {
        var context = new FileSystemRepositoryContext(_root);
        context.Dispose();
        context.Dispose();
        Action access = () => _ = context.StagingIndex;
        access.Should().Throw<ObjectDisposedException>();
        Directory.Exists(_root).Should().BeFalse();
    }

    [Fact]
    public async Task DisposingAnUnusedIndexContext_DoesNotClearAnotherOwnersPool()
    {
        using var owner = new SqliteStagingIndex(_root);
        await owner.GetStagedEntriesAsync();
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = Path.Combine(_root, ".dawvc", "index.db"),
            Mode = SqliteOpenMode.ReadWriteCreate
        }.ToString());
        await connection.OpenAsync();
        var native = connection.Handle!;
        await connection.DisposeAsync();
        native.IsClosed.Should().BeFalse();
        using (var reader = new FileSystemRepositoryContext(_root)) reader.GetRemotes().Should().BeEmpty();
        native.IsClosed.Should().BeFalse("the read context never owned or used an index");
        owner.Dispose();
        native.IsClosed.Should().BeTrue();
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}
