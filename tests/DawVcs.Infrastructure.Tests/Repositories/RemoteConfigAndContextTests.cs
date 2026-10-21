using DawVcs.Domain.Artifacts;
using DawVcs.Domain.Common;
using DawVcs.Domain.Configuration;
using DawVcs.Domain.Remotes;
using DawVcs.Infrastructure.Configuration;
using DawVcs.Infrastructure.Repositories;

using FluentAssertions;

using Xunit;

namespace DawVcs.Infrastructure.Tests.Repositories;

public sealed class RemoteConfigAndContextTests : IDisposable
{
    private readonly string _tempDir;
    private readonly FileSystemRepositoryContext _context;

    public RemoteConfigAndContextTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "dawvc_context_remotes_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        Directory.CreateDirectory(Path.Combine(_tempDir, ".dawvc"));

        var config = new RepositoryConfig(RepositoryId.New(), "TestSong", new ArtifactPath("Song.flp"), BranchName.Main);
        var yaml = YamlRepositoryConfigManager.ToYaml(config);
        File.WriteAllText(Path.Combine(_tempDir, "dawvc.yaml"), yaml);

        _context = new FileSystemRepositoryContext(_tempDir);
    }

    public void Dispose()
    {
        _context.Dispose();
        if (Directory.Exists(_tempDir))
        {
            try { Directory.Delete(_tempDir, recursive: true); } catch { }
        }
    }

    [Fact]
    public void Remotes_SaveAndGetAndRemove_PersistsInRemotesJson()
    {
        _context.GetRemotes().Should().BeEmpty();

        var origin = new RemoteConfig(RemoteName.Origin, @"\\NAS\Music\SharedSong.dawvc");
        _context.SaveRemote(origin);

        var remotes = _context.GetRemotes();
        remotes.Should().HaveCount(1);
        remotes[0].Name.Value.Should().Be("origin");
        remotes[0].Url.Should().Be(@"\\NAS\Music\SharedSong.dawvc");

        var retrieved = _context.GetRemote("origin");
        retrieved.Should().NotBeNull();
        retrieved!.Url.Should().Be(@"\\NAS\Music\SharedSong.dawvc");

        var removed = _context.RemoveRemote("origin");
        removed.Should().BeTrue();
        _context.GetRemotes().Should().BeEmpty();
    }

    [Fact]
    public void Upstream_SetAndGet_PersistsMapping()
    {
        var main = BranchName.Main;
        var (remote, remoteBranch) = _context.GetUpstream(main);
        remote.Should().BeNull();
        remoteBranch.Should().BeNull();

        _context.SetUpstream(main, "origin", main);
        var (afterRemote, afterRemoteBranch) = _context.GetUpstream(main);
        afterRemote.Should().Be("origin");
        afterRemoteBranch.Should().Be(main);
    }

    [Fact]
    public async Task RemoteTrackingRefs_UpdateAndGetAndResolveReference_Succeeds()
    {
        var main = BranchName.Main;
        var commitId = CommitId.Parse(new string('a', 64));

        _context.GetRemoteTrackingCommit("origin", main).Should().BeNull();

        await _context.UpdateRemoteTrackingCommitAsync("origin", main, commitId);

        var trackingCommit = _context.GetRemoteTrackingCommit("origin", main);
        trackingCommit.Should().Be(commitId);

        // ResolveReference("origin/main") should return trackingCommit
        var resolved = _context.ResolveReference("origin/main");
        resolved.Should().Be(commitId);
    }
}
