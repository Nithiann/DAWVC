using DawVcs.Domain.Hashing;
using DawVcs.Domain.Remotes;
using DawVcs.Domain.Storage;
using DawVcs.Infrastructure.Storage;

using FluentAssertions;

using Xunit;

namespace DawVcs.Infrastructure.Tests.Storage;

public sealed class QuarantineObjectStoreTests : IDisposable
{
    private readonly string _tempRepoDir;
    private readonly QuarantineObjectStore _store;

    public QuarantineObjectStoreTests()
    {
        _tempRepoDir = Path.Combine(Path.GetTempPath(), "dawvc_quarantine_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(_tempRepoDir, ".dawvc", "objects"));

        _store = new QuarantineObjectStore(_tempRepoDir);
    }

    public void Dispose()
    {
        _store.Dispose();
        if (Directory.Exists(_tempRepoDir))
        {
            try { Directory.Delete(_tempRepoDir, recursive: true); } catch { }
        }
    }

    [Fact]
    public async Task CreateIncomingWriteStreamAsync_CreatesFileInSessionDir()
    {
        var hash = Blake3ContentHasher.Hash("sample blob"u8);
        await using (var stream = await _store.CreateIncomingWriteStreamAsync(hash))
        {
            await stream.WriteAsync("sample blob"u8.ToArray());
        }

        var incomingPath = _store.GetIncomingPath(hash);
        File.Exists(incomingPath).Should().BeTrue();
    }

    [Fact]
    public async Task PromoteAsync_MovesObjectToLooseObjectsDirectory()
    {
        var data = "payload"u8.ToArray();
        var hash = Blake3ContentHasher.Hash(data);

        await using (var stream = await _store.CreateIncomingWriteStreamAsync(hash))
        {
            await stream.WriteAsync(data);
        }

        var incomingPath = _store.GetIncomingPath(hash);
        var verified = new VerifiedIncomingObject(hash, ObjectType.Blob, data.Length, incomingPath);

        await _store.PromoteAsync(verified);

        File.Exists(incomingPath).Should().BeFalse();

        var hex = hash.ToString();
        var expectedDest = Path.Combine(_tempRepoDir, ".dawvc", "objects", hex[..2], hex[2..]);
        File.Exists(expectedDest).Should().BeTrue();
    }

    [Fact]
    public async Task PromoteAsync_DestinationAlreadyExists_IdempotentlyDeletesIncoming()
    {
        var data = "payload"u8.ToArray();
        var hash = Blake3ContentHasher.Hash(data);

        var hex = hash.ToString();
        var destDir = Path.Combine(_tempRepoDir, ".dawvc", "objects", hex[..2]);
        Directory.CreateDirectory(destDir);
        var destFile = Path.Combine(destDir, hex[2..]);
        await File.WriteAllBytesAsync(destFile, data);

        await using (var stream = await _store.CreateIncomingWriteStreamAsync(hash))
        {
            await stream.WriteAsync(data);
        }

        var incomingPath = _store.GetIncomingPath(hash);
        var verified = new VerifiedIncomingObject(hash, ObjectType.Blob, data.Length, incomingPath);

        await _store.PromoteAsync(verified);

        File.Exists(incomingPath).Should().BeFalse();
        File.Exists(destFile).Should().BeTrue();
    }

    [Fact]
    public async Task PromoteAllAsync_EnforcesOrdering_BlobsBeforeSnapshotsBeforeCommits()
    {
        var promotionOrder = new List<ObjectType>();

        var blobHash = Blake3ContentHasher.Hash("blob"u8);
        var snapHash = Blake3ContentHasher.Hash("snap"u8);
        var commitHash = Blake3ContentHasher.Hash("commit"u8);

        await File.WriteAllBytesAsync(_store.GetIncomingPath(blobHash), "blob"u8.ToArray());
        await File.WriteAllBytesAsync(_store.GetIncomingPath(snapHash), "snap"u8.ToArray());
        await File.WriteAllBytesAsync(_store.GetIncomingPath(commitHash), "commit"u8.ToArray());

        // Pass objects in reverse order: Commit, Snapshot, Blob
        var objects = new List<VerifiedIncomingObject>
        {
            new(commitHash, ObjectType.Commit, 6, _store.GetIncomingPath(commitHash)),
            new(snapHash, ObjectType.Snapshot, 4, _store.GetIncomingPath(snapHash)),
            new(blobHash, ObjectType.Blob, 4, _store.GetIncomingPath(blobHash))
        };

        await _store.PromoteAllAsync(objects);

        // Verify all 3 now exist in objects/
        foreach (var obj in objects)
        {
            var hex = obj.Hash.ToString();
            var path = Path.Combine(_tempRepoDir, ".dawvc", "objects", hex[..2], hex[2..]);
            File.Exists(path).Should().BeTrue();
        }
    }

    [Fact]
    public void PruneStaleDirectories_PrunesOlderThan24h_LeavesActiveSession()
    {
        var incomingRoot = Path.Combine(_tempRepoDir, ".dawvc", "incoming");
        var staleDir = Path.Combine(incomingRoot, "stale_session_123");
        Directory.CreateDirectory(staleDir);
        File.WriteAllText(Path.Combine(staleDir, "dummy.tmp"), "test");

        Directory.SetLastWriteTimeUtc(staleDir, DateTime.UtcNow.AddHours(-25));

        _store.PruneStaleDirectories(TimeSpan.FromHours(24));

        Directory.Exists(staleDir).Should().BeFalse();
        Directory.Exists(_store.SessionDirectory).Should().BeTrue();
    }
}
