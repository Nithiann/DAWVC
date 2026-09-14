using DawVcs.Domain.Artifacts;
using DawVcs.Domain.Common;
using DawVcs.Domain.Hashing;
using DawVcs.Infrastructure.Repositories;

using FluentAssertions;

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
}
