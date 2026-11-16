using DawVcs.Domain.Artifacts;
using DawVcs.Domain.Common;
using DawVcs.Domain.Hashing;
using DawVcs.Infrastructure.Repositories;

using FluentAssertions;

using Microsoft.Data.Sqlite;

using Xunit;

namespace DawVcs.Infrastructure.Tests.Repositories;

public sealed class SqliteStagingIndexTests : IDisposable
{
    private readonly string _testDir;

    public SqliteStagingIndexTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), "dawvc_staging_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDir))
        {
            try { Directory.Delete(_testDir, recursive: true); } catch { }
        }
    }

    [Fact]
    public async Task Dispose_ClosesIdleNativeHandlesAndAllowsImmediateRepositoryDeletion()
    {
        var root = Path.Combine(_testDir, "disposal");
        using var index = new SqliteStagingIndex(root);
        await index.GetStagedEntriesAsync();
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = Path.Combine(root, ".dawvc", "index.db"),
            Mode = SqliteOpenMode.ReadWriteCreate
        };
        var probe = new SqliteConnection(builder.ToString());
        await probe.OpenAsync();
        var native = probe.Handle!;
        await probe.DisposeAsync();
        native.IsClosed.Should().BeFalse("pooling retains the physical connection before index disposal");

        index.Dispose();
        index.Dispose(); // Context and host shutdown may both dispose owned resources.

        native.IsClosed.Should().BeTrue("session disposal must release pooled native file handles");
        Directory.Delete(root, recursive: true); // Must also pass on Windows, without retries/pool cleanup in the test.
        Directory.Exists(root).Should().BeFalse();
        var afterDispose = () => index.GetStagedEntriesAsync();
        await afterDispose.Should().ThrowAsync<ObjectDisposedException>();
    }

    [Fact]
    public async Task Dispose_DoesNotCloseActiveConnectionsOrAnotherRepositoryPool()
    {
        var root = Path.Combine(_testDir, "active");
        var otherRoot = Path.Combine(_testDir, "other");
        using var index = new SqliteStagingIndex(root);
        using var otherIndex = new SqliteStagingIndex(otherRoot);
        await index.GetStagedEntriesAsync();
        await otherIndex.GetStagedEntriesAsync();
        static SqliteConnection CreateConnection(string directory) => new(new SqliteConnectionStringBuilder
        {
            DataSource = Path.Combine(directory, ".dawvc", "index.db"),
            Mode = SqliteOpenMode.ReadWriteCreate
        }.ToString());
        await using var active = CreateConnection(root);
        await active.OpenAsync();
        var activeNative = active.Handle!;
        var other = CreateConnection(otherRoot);
        await other.OpenAsync();
        var otherNative = other.Handle!;
        await other.DisposeAsync();

        index.Dispose();

        activeNative.IsClosed.Should().BeFalse();
        otherNative.IsClosed.Should().BeFalse("disposal must not clear unrelated pools");
        await using var command = active.CreateCommand();
        command.CommandText = "SELECT 1";
        (await command.ExecuteScalarAsync()).Should().Be(1L);
        await active.DisposeAsync();
        activeNative.IsClosed.Should().BeTrue("an active handle must close instead of returning to the cleared pool");
    }

    [Fact]
    public async Task StagingEntries_CanBeStagedRetrievedAndCleared()
    {
        using var index = new SqliteStagingIndex(_testDir);

        var hash = new ContentHash(new byte[32]);
        var entry1 = ArtifactEntry.Create(new ArtifactPath("Samples/Kick.wav"), hash, 1024, ArtifactRole.ProjectAsset);
        var entry2 = ArtifactEntry.Create(new ArtifactPath("Samples/Snare.wav"), hash, 2048, ArtifactRole.ProjectAsset);

        await index.StageEntryAsync(entry1);
        await index.StageEntryAsync(entry2);

        var staged = await index.GetStagedEntriesAsync();
        staged.Should().HaveCount(2);
        staged.Select(s => s.Path.Value).Should().Contain("Samples/Kick.wav");
        staged.Select(s => s.Path.Value).Should().Contain("Samples/Snare.wav");

        await index.UnstageEntryAsync(entry1.Path);
        var afterUnstage = await index.GetStagedEntriesAsync();
        afterUnstage.Should().HaveCount(1);
        afterUnstage[0].Path.Value.Should().Be("Samples/Snare.wav");

        await index.ClearStagedEntriesAsync();
        var afterClear = await index.GetStagedEntriesAsync();
        afterClear.Should().BeEmpty();
    }

    [Fact]
    public async Task FilesystemCache_CachesHashAndMatchesOnSizeAndTimestamp()
    {
        using var index = new SqliteStagingIndex(_testDir);

        var path = new ArtifactPath("Audio/Vocals.wav");
        var hash = new ContentHash(new byte[32]);
        var timestamp = DateTimeOffset.UtcNow;
        long size = 50000;

        var initial = await index.TryGetCachedHashAsync(path, size, timestamp);
        initial.Should().BeNull();

        await index.SetCachedHashAsync(path, size, timestamp, hash);

        // Cache hit
        var hit = await index.TryGetCachedHashAsync(path, size, timestamp);
        hit.Should().Be(hash);

        // Cache miss on changed size
        var missSize = await index.TryGetCachedHashAsync(path, size + 1, timestamp);
        missSize.Should().BeNull();

        // Cache miss on changed timestamp
        var missTime = await index.TryGetCachedHashAsync(path, size, timestamp.AddSeconds(1));
        missTime.Should().BeNull();
    }

    [Fact]
    public async Task CorruptionRecovery_WhenDatabaseCorrupted_TransparentlyRecreatesSchema()
    {
        var dbPath = Path.Combine(_testDir, ".dawvc", "index.db");
        Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);

        // Corrupt file by writing invalid header
        await File.WriteAllTextAsync(dbPath, "NOT A VALID SQLITE DATABASE FILE - CORRUPTION TEST");

        using var index = new SqliteStagingIndex(_testDir);

        // Must recover and succeed without throwing
        var hash = new ContentHash(new byte[32]);
        var entry = ArtifactEntry.Create(new ArtifactPath("Recovered.wav"), hash, 100);
        await index.StageEntryAsync(entry);

        var staged = await index.GetStagedEntriesAsync();
        staged.Should().HaveCount(1);
        staged[0].Path.Value.Should().Be("Recovered.wav");
    }

    [Fact]
    public async Task PreCancelledToken_ThrowsOperationCanceledExceptionAcrossAllMethods()
    {
        using var index = new SqliteStagingIndex(_testDir);
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var token = cts.Token;

        var path = new ArtifactPath("Test.wav");
        var hash = new ContentHash(new byte[32]);
        var entry = ArtifactEntry.Create(path, hash, 100);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => index.StageEntryAsync(entry, token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => index.UnstageEntryAsync(path, token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => index.GetStagedEntriesAsync(token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => index.ClearStagedEntriesAsync(token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => index.TryGetCachedHashAsync(path, 100, DateTimeOffset.UtcNow, token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => index.SetCachedHashAsync(path, 100, DateTimeOffset.UtcNow, hash, token));
    }
}
