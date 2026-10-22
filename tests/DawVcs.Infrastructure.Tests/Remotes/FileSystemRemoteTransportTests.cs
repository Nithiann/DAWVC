using DawVcs.Domain.Artifacts;
using DawVcs.Domain.Common;
using DawVcs.Domain.Configuration;
using DawVcs.Domain.Hashing;
using DawVcs.Domain.Remotes;
using DawVcs.Domain.Storage;
using DawVcs.Infrastructure.Configuration;
using DawVcs.Infrastructure.Remotes;
using DawVcs.Infrastructure.Storage;

using FluentAssertions;

using Xunit;

namespace DawVcs.Infrastructure.Tests.Remotes;

public sealed class FileSystemRemoteTransportTests : IDisposable
{
    private readonly string _tempDir;

    public FileSystemRemoteTransportTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "dawvc_remote_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            try { Directory.Delete(_tempDir, recursive: true); } catch { }
        }
    }

    [Fact]
    public async Task HandshakeAsync_NonExistentDirectory_ThrowsRemoteException()
    {
        var missingDir = Path.Combine(_tempDir, "does_not_exist");
        using var transport = new FileSystemRemoteTransport(missingDir);

        var act = () => transport.HandshakeAsync();
        await act.Should().ThrowAsync<RemoteException>()
            .WithMessage("*does not exist*");
    }

    [Fact]
    public async Task HandshakeAsync_MissingDawvcYaml_ThrowsRemoteException()
    {
        var emptyRepo = Path.Combine(_tempDir, "empty_repo");
        Directory.CreateDirectory(emptyRepo);
        using var transport = new FileSystemRemoteTransport(emptyRepo);

        var act = () => transport.HandshakeAsync();
        await act.Should().ThrowAsync<RemoteException>()
            .WithMessage("*missing configuration file 'dawvc.yaml'*");
    }

    [Fact]
    public async Task HandshakeAsync_ValidNonBareRepo_ReturnsMatchingHandshake()
    {
        var repoDir = Path.Combine(_tempDir, "valid_repo");
        Directory.CreateDirectory(Path.Combine(repoDir, ".dawvc"));

        var repoId = RepositoryId.New();
        var config = new RepositoryConfig(repoId, "RemoteSong", new ArtifactPath("Song.flp"), BranchName.Main);
        var yaml = YamlRepositoryConfigManager.ToYaml(config);
        await File.WriteAllTextAsync(Path.Combine(repoDir, "dawvc.yaml"), yaml);

        using var transport = new FileSystemRemoteTransport(repoDir);
        var handshake = await transport.HandshakeAsync();

        handshake.Should().NotBeNull();
        handshake.RepositoryId.Should().Be(repoId);
        handshake.SchemaVersion.Should().Be(1);
    }

    [Fact]
    public async Task HandshakeAsync_ValidBareRepo_ReturnsMatchingHandshake()
    {
        var bareDir = Path.Combine(_tempDir, "bare_repo.dawvc");
        Directory.CreateDirectory(Path.Combine(bareDir, "objects"));

        var repoId = RepositoryId.New();
        var config = new RepositoryConfig(repoId, "BareSong", new ArtifactPath("Song.flp"), BranchName.Main);
        var yaml = YamlRepositoryConfigManager.ToYaml(config);
        await File.WriteAllTextAsync(Path.Combine(bareDir, "dawvc.yaml"), yaml);

        using var transport = new FileSystemRemoteTransport(bareDir);
        var handshake = await transport.HandshakeAsync();

        handshake.Should().NotBeNull();
        handshake.RepositoryId.Should().Be(repoId);
        handshake.SchemaVersion.Should().Be(1);
    }

    [Fact]
    public async Task ListRefsAsync_EnumeratesBranchHeads()
    {
        var repoDir = Path.Combine(_tempDir, "refs_repo");
        var headsDir = Path.Combine(repoDir, ".dawvc", "refs", "heads");
        Directory.CreateDirectory(headsDir);

        var mainCommit = CommitId.Parse(new string('a', 64));
        var featCommit = CommitId.Parse(new string('b', 64));

        await File.WriteAllTextAsync(Path.Combine(headsDir, "main"), mainCommit.ToString() + "\n");

        var featSubdir = Path.Combine(headsDir, "feature");
        Directory.CreateDirectory(featSubdir);
        await File.WriteAllTextAsync(Path.Combine(featSubdir, "cool-idea"), featCommit.ToString() + "\n");

        using var transport = new FileSystemRemoteTransport(repoDir);
        var refs = await transport.ListRefsAsync();

        refs.Should().HaveCount(2);
        refs.Should().Contain(r => r.Name == "main" && r.HeadCommit == mainCommit);
        refs.Should().Contain(r => r.Name == "feature/cool-idea" && r.HeadCommit == featCommit);
    }

    [Fact]
    public async Task OpenObjectPayloadStreamAsync_And_ObjectExistsAsync_ReadStoredObject()
    {
        var repoDir = Path.Combine(_tempDir, "obj_repo");
        var store = new LooseObjectStore(repoDir);

        var payload = new byte[] { 1, 2, 3, 4, 5 };
        using var ms = new MemoryStream(payload);
        var blobHash = await store.WriteBlobAsync(ms);

        using var transport = new FileSystemRemoteTransport(repoDir);
        var exists = await transport.ObjectExistsAsync(blobHash);
        exists.Should().BeTrue();

        await using var stream = await transport.OpenObjectPayloadStreamAsync(blobHash);
        stream.Should().NotBeNull();

        using var readMs = new MemoryStream();
        await stream!.CopyToAsync(readMs);
        readMs.ToArray().Length.Should().BeGreaterThan(payload.Length); // Raw envelope contains header + payload
    }
}
