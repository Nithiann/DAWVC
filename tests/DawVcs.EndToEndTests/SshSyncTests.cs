using System.Diagnostics;
using System.Text;
using System.Text.Json;

using DawVcs.Application.Checkouts;
using DawVcs.Application.Remotes;
using DawVcs.Domain.Artifacts;
using DawVcs.Domain.Common;
using DawVcs.Domain.Configuration;
using DawVcs.Domain.Entities;
using DawVcs.Domain.Hashing;
using DawVcs.Domain.Remotes;
using DawVcs.Domain.Repositories;
using DawVcs.Domain.Storage;
using DawVcs.Infrastructure.Configuration;
using DawVcs.Infrastructure.Remotes;
using DawVcs.Infrastructure.Remotes.Ssh;
using DawVcs.Infrastructure.Repositories;
using DawVcs.Infrastructure.Storage;

using FluentAssertions;

using Xunit;

namespace DawVcs.EndToEndTests;

public sealed class SshSyncTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "dawvc-ssh-" + Guid.NewGuid().ToString("N"));
    private readonly RepositoryConfig _config = new(RepositoryId.New(), "SSH song", new ArtifactPath("Song.flp"));
    private string Local => Path.Combine(_root, "local");
    private string Remote => Path.Combine(_root, "remote");
    private string Policy => Path.Combine(_root, "serve.json");
    private const string Url = "dawvc://test@host/projects/song";
    private static FileSystemRepositoryContext Context(string path) => new FileSystemRepositoryContext(path);
    private LocalProcessFactory Factory => new LocalProcessFactory(Policy);

    public SshSyncTests()
    {
        Directory.CreateDirectory(Local);
        using var context = new FileSystemRepositoryContext(Local);
        context.SaveConfig(_config);
        context.SetCurrentBranch(BranchName.Main);
        context.SaveRemote(new RemoteConfig(RemoteName.Origin, Url));
        Directory.CreateDirectory(Path.Combine(Remote, "objects"));
        Directory.CreateDirectory(Path.Combine(Remote, "refs", "heads"));
        File.WriteAllText(Path.Combine(Remote, "dawvc.yaml"), YamlRepositoryConfigManager.ToYaml(_config));
        WritePolicy(true);
    }

    [Fact]
    public async Task PushCloneFetchAndPull_UseOneProtocolAndRestoreIdenticalBytes()
    {
        var first = await CommitAsync("first");
        var push = new PushUseCase(Context, Factory);
        (await push.ExecuteAsync(new PushRequest(Local, DryRun: true))).ObjectsTransferred.Should().Be(3);
        File.Exists(Path.Combine(Remote, "ref-write.lock")).Should().BeFalse();
        (await push.ExecuteAsync(new PushRequest(Local))).NewCommit.Should().Be(first);
        (await push.ExecuteAsync(new PushRequest(Local))).ObjectsTransferred.Should().Be(0);
        var clonePath = Path.Combine(_root, "clone");
        var fetch = new FetchUseCase(Context, Factory, p => new QuarantineObjectStore(p));
        var checkout = new CheckoutUseCase(Context);
        var clone = new CloneUseCase(Context, Factory, fetch, checkout);
        (await clone.ExecuteAsync(new CloneRequest(Url, clonePath))).HeadCommit.Should().Be(first);
        File.ReadAllText(Path.Combine(clonePath, "Song.flp")).Should().Be("first");
        Directory.Exists(Path.Combine(Remote, ".dawvc")).Should().BeFalse();
        var second = await CommitAsync("second", first);
        await push.ExecuteAsync(new PushRequest(Local));
        (await fetch.ExecuteAsync(new FetchRequest(clonePath))).ObjectsFetched.Should().Be(3);
        File.ReadAllText(Path.Combine(clonePath, "Song.flp")).Should().Be("first");
        var pull = new PullUseCase(Context, fetch, checkout);
        (await pull.ExecuteAsync(new PullRequest(clonePath))).NewCommit.Should().Be(second);
        File.ReadAllText(Path.Combine(clonePath, "Song.flp")).Should().Be("second");
    }

    [Fact]
    public async Task ReadOnlyPolicy_AllowsReadButDeniesPushWithoutMutation()
    {
        await CommitAsync("first");
        WritePolicy(false);
        using var transport = Factory.CreateTransport(Url);
        (await transport.HandshakeAsync()).RepositoryId.Should().Be(_config.RepositoryId);
        await Assert.ThrowsAsync<PushRejectedException>(() => new PushUseCase(Context, Factory).ExecuteAsync(new PushRequest(Local)));
        File.Exists(Path.Combine(Remote, "ref-write.lock")).Should().BeFalse();
    }

    [Fact]
    public async Task UnknownAliasAndAttemptedFilesystemPath_AreDenied()
    {
        using var transport = Factory.CreateTransport("dawvc://host/secret");
        var error = await Assert.ThrowsAsync<PushRejectedException>(() => transport.HandshakeAsync());
        error.Message.Should().NotContain(_root);
        Action traversal = () => Factory.CreateTransport("dawvc://host/../secret");
        traversal.Should().Throw<ArgumentException>();
    }

    [Fact]
    public async Task DisconnectDuringPush_ReleasesReceiverLock()
    {
        await CommitAsync("first");
        using (var transport = Factory.CreateTransport(Url))
        {
            await transport.HandshakeAsync();
            using var session = await ((IRemotePushTransport)transport).BeginPushAsync(BranchName.Main, null);
        }
        // Dispose awaits process termination, so the OS must have released the lock.
        using var reopened = new FileStream(Path.Combine(Remote, "ref-write.lock"), FileMode.Open, FileAccess.ReadWrite, FileShare.None);
    }

    [Fact]
    public async Task Protocol_RejectsWrongVersionWithoutHostDetails()
    {
        using var input = new MemoryStream();
        using var output = new MemoryStream();
        await RemoteWire.WriteAsync(input, new WireRequest { Version = 99, Operation = "hello", Repository = "projects/song" }, default);
        input.Position = 0;
        (await RemoteProtocolServer.RunAsync(input, output, ServeConfiguration.Load(Policy))).Should().Be(8);
        output.Position = 0;
        (await RemoteWire.ReadAsync<WireResponse>(output, default))!.Error.Should().Be("incompatible");
        Encoding.UTF8.GetString(output.ToArray()).Should().NotContain(_root);
    }

    [Fact]
    public async Task Protocol_RejectsOversizedUploadAndCleansSession()
    {
        using var input = new MemoryStream();
        using var output = new MemoryStream();
        foreach (var request in new[] {
            new WireRequest { Operation = "hello", Repository = "projects/song" },
            new WireRequest { Operation = "begin", Branch = "main" },
            new WireRequest { Operation = "put", Hash = new string('a', 64), Type = 1, Length = RemoteWire.MaxObjectBytes + 1 } })
            await RemoteWire.WriteAsync(input, request, default);
        input.Position = 0;
        (await RemoteProtocolServer.RunAsync(input, output, ServeConfiguration.Load(Policy))).Should().Be(8);
        Directory.GetFileSystemEntries(Path.Combine(Remote, "incoming")).Should().BeEmpty();
        output.Position = 0;
        await RemoteWire.ReadAsync<WireResponse>(output, default);
        await RemoteWire.ReadAsync<WireResponse>(output, default);
        (await RemoteWire.ReadAsync<WireResponse>(output, default))!.Error.Should().Be("integrity");
    }

    [Fact]
    public async Task Protocol_EofDuringPushCleansSessionAndReleasesLock()
    {
        using var input = new MemoryStream();
        using var output = new MemoryStream();
        await RemoteWire.WriteAsync(input, new WireRequest { Operation = "hello", Repository = "projects/song" }, default);
        await RemoteWire.WriteAsync(input, new WireRequest { Operation = "begin", Branch = "main" }, default);
        input.Position = 0;
        (await RemoteProtocolServer.RunAsync(input, output, ServeConfiguration.Load(Policy))).Should().Be(0);
        Directory.GetFileSystemEntries(Path.Combine(Remote, "incoming")).Should().BeEmpty();
        using var reopened = new FileStream(Path.Combine(Remote, "ref-write.lock"), FileMode.Open, FileAccess.ReadWrite, FileShare.None);
    }

    [Fact]
    public async Task CorruptWireUpload_IsRejectedAndNeverPublishes()
    {
        using var transport = Factory.CreateTransport(Url);
        await transport.HandshakeAsync();
        using (var session = await ((IRemotePushTransport)transport).BeginPushAsync(BranchName.Main, null))
        {
            using var payload = new MemoryStream([1, 2, 9]);
            var header = new ObjectEnvelopeHeader(ObjectType.Blob, 3, Blake3ContentHasher.Hash([1, 2, 3]));
            await Assert.ThrowsAnyAsync<InvalidEnvelopeException>(() => session.ReceiveObjectAsync(header, payload));
        }
        File.Exists(Path.Combine(Remote, "refs", "heads", "main")).Should().BeFalse();
    }

    [Fact]
    public async Task AbandonedDownload_FaultsConnectionRatherThanReusingBodyAsAFrame()
    {
        var tip = await CommitAsync("first");
        await new PushUseCase(Context, Factory).ExecuteAsync(new PushRequest(Local));
        using var transport = Factory.CreateTransport(Url);
        await using (var body = await transport.OpenObjectPayloadStreamAsync(tip.Value))
        {
            body.Should().NotBeNull();
            body!.ReadByte().Should().BeGreaterThanOrEqualTo(0);
        }
        await Assert.ThrowsAsync<RemoteException>(() => transport.ListRefsAsync());
    }

    [Fact]
    public async Task HostDeadline_CancelsEvenAnInputStreamThatIgnoresCancellation()
    {
        using var input = new NonCooperativeInput();
        using var output = new MemoryStream();
        using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        (await RemoteProtocolServer.RunAsync(input, output, ServeConfiguration.Load(Policy), cancel.Token).WaitAsync(TimeSpan.FromSeconds(5))).Should().Be(8);
        output.Position = 0;
        (await RemoteWire.ReadAsync<WireResponse>(output, default))!.Error.Should().Be("timeout");
    }

    [Fact]
    public async Task HostRequestLimit_TerminatesTheSession()
    {
        using var input = new MemoryStream();
        using var output = new MemoryStream();
        await RemoteWire.WriteAsync(input, new WireRequest { Operation = "hello", Repository = "projects/song" }, default);
        input.Position = 0;
        (await RemoteProtocolServer.RunAsync(input, output, ServeConfiguration.Load(Policy) with { MaxRequests = 1 })).Should().Be(8);
    }

    private sealed class NonCooperativeInput : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => 0; set => throw new NotSupportedException(); }
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) => new(new TaskCompletionSource<int>().Task);
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    [Theory]
    [InlineData("dawvc://alice:secret@host/projects/song")]
    [InlineData("dawvc://alice%3asecret@host/projects/song")]
    [InlineData("dawvc://host/../song")]
    public void RemoteAdd_RejectsSecretsAndInvalidAliasesWithoutPersisting(string address)
    {
        var remotes = new RemoteUseCase(Context, new DefaultRemoteTransportFactory());
        Action add = () => remotes.AddRemote(new RemoteAddRequest(Local, "invalid", address));
        add.Should().Throw<ArgumentException>();
        using var context = new FileSystemRepositoryContext(Local);
        context.GetRemote("invalid").Should().BeNull();
    }

    [Fact]
    public async Task TruncatedWireUpload_CleansQuarantineAndReleasesLock()
    {
        using var input = new MemoryStream();
        using var output = new MemoryStream();
        foreach (var request in new[] {
            new WireRequest { Operation = "hello", Repository = "projects/song" },
            new WireRequest { Operation = "begin", Branch = "main" },
            new WireRequest { Operation = "put", Hash = new string('a', 64), Type = 1, Length = 3 } })
            await RemoteWire.WriteAsync(input, request, default);
        input.WriteByte(1);
        input.Position = 0;
        (await RemoteProtocolServer.RunAsync(input, output, ServeConfiguration.Load(Policy))).Should().Be(8);
        Directory.GetFileSystemEntries(Path.Combine(Remote, "incoming")).Should().BeEmpty();
        using var reopened = new FileStream(Path.Combine(Remote, "ref-write.lock"), FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        File.Exists(Path.Combine(Remote, "refs", "heads", "main")).Should().BeFalse();
    }

    private void WritePolicy(bool allowPush) => File.WriteAllText(Policy, JsonSerializer.Serialize(new ServeConfiguration
    {
        Repositories = new Dictionary<string, HostedRepository> { ["projects/song"] = new() { Path = Remote, AllowPush = allowPush } }
    }));

    private async Task<CommitId> CommitAsync(string text, CommitId? parent = null)
    {
        using var context = new FileSystemRepositoryContext(Local);
        using var bytes = new MemoryStream(Encoding.UTF8.GetBytes(text));
        var hash = await context.ObjectStore.WriteBlobAsync(bytes);
        var snapshot = new ProjectSnapshot(ProjectArtifact.CreateSingleFile("Synthetic", new ArtifactPath("Song.flp"), hash, bytes.Length), DateTimeOffset.UtcNow);
        await context.ObjectStore.WriteObjectAsync(ObjectType.Snapshot, snapshot.ToCanonicalBytes());
        var commit = new Commit(parent.HasValue ? [parent.Value] : [], snapshot.Id, "test", DateTimeOffset.UtcNow, text);
        await context.ObjectStore.WriteObjectAsync(ObjectType.Commit, commit.ToCanonicalBytes());
        await context.UpdateBranchCommitAsync(BranchName.Main, commit.Id);
        return commit.Id;
    }

    private sealed class LocalProcessFactory(string policy) : IRemoteTransportFactory
    {
        public IRemoteTransport CreateTransport(string remoteUrl) => new SshRemoteTransport(remoteUrl, () =>
        {
            var host = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet";
            var start = new ProcessStartInfo(host) { UseShellExecute = false, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
            start.ArgumentList.Add(typeof(DawVcs.Cli.Program).Assembly.Location);
            start.ArgumentList.Add("serve"); start.ArgumentList.Add("--config"); start.ArgumentList.Add(policy);
            return start;
        });
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);
}
