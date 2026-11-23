using System.Buffers.Binary;
using System.Text;

using DawVcs.Domain.Remotes;
using DawVcs.Infrastructure.Remotes;
using DawVcs.Infrastructure.Remotes.Ssh;

using FluentAssertions;

using Xunit;

namespace DawVcs.Infrastructure.Tests.Remotes;

public sealed class SshTransportTests
{
    [Theory]
    [InlineData("dawvc://host/song", "host", null, 22, "song")]
    [InlineData("dawvc://alice@nas:2222/projects/song", "nas", "alice", 2222, "projects/song")]
    [InlineData("dawvc://[::1]/song", "::1", null, 22, "song")]
    public void Address_ParsesLogicalAliases(string value, string host, string? user, int port, string repository)
    {
        var parsed = DawVcsAddress.Parse(value);
        parsed.Host.Trim('[', ']').Should().Be(host);
        parsed.User.Should().Be(user);
        parsed.Port.Should().Be(port);
        parsed.Repository.Should().Be(repository);
    }

    [Theory]
    [InlineData("dawvc:://host/song")]
    [InlineData("dawvc://alice:secret@host/song")]
    [InlineData("dawvc://host/../song")]
    [InlineData("dawvc://host/a/./song")]
    [InlineData("dawvc://host/%2e%2e/song")]
    [InlineData("dawvc://host/a%2fsong")]
    [InlineData("dawvc://host/song?path=/etc")]
    [InlineData("dawvc://host/song#fragment")]
    [InlineData("dawvc://host/")]
    [InlineData("dawvc://host/a//song")]
    [InlineData("dawvc://host/song;")]
    [InlineData("dawvc://-lroot@host/song")]
    [InlineData("dawvc://host:0/song")]
    public void Address_RejectsCredentialsTraversalAndShellInput(string value)
    {
        Action parse = () => DawVcsAddress.Parse(value);
        parse.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void OpenSsh_EnforcesHostKeysPublicKeysAndConstantCommand()
    {
        var info = DawVcsAddress.Parse("dawvc://alice@nas:2222/projects/song").CreateStartInfo();
        info.FileName.Should().Be("ssh");
        info.UseShellExecute.Should().BeFalse();
        info.ArgumentList.Should().Contain("StrictHostKeyChecking=yes").And.Contain("BatchMode=yes")
            .And.Contain("PasswordAuthentication=no").And.Contain("ForwardAgent=no").And.Contain("ClearAllForwardings=yes")
            .And.Contain("PermitLocalCommand=no");
        info.ArgumentList[^1].Should().Be("dawvc serve");
        info.ArgumentList.Should().NotContain(a => a.Contains("projects/song", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("ssh://host/song")]
    [InlineData("https://host/song")]
    [InlineData("ftp://host/song")]
    public void Factory_RejectsUnimplementedSchemesInsteadOfTreatingThemAsPaths(string value)
    {
        Action create = () => new DefaultRemoteTransportFactory().CreateTransport(value);
        create.Should().Throw<NotSupportedException>();
    }

    [Fact]
    public void Factory_SelectsSshWithoutStartingAProcess()
    {
        using var transport = new DefaultRemoteTransportFactory().CreateTransport("dawvc://host/song");
        transport.Should().BeOfType<SshRemoteTransport>();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(RemoteWire.MaxFrameBytes + 1)]
    public async Task Wire_RejectsUnboundedFrameLengthsBeforeAllocation(int length)
    {
        var prefix = new byte[4];
        BinaryPrimitives.WriteInt32BigEndian(prefix, length);
        using var input = new MemoryStream(prefix);
        await Assert.ThrowsAsync<RemoteException>(() => RemoteWire.ReadAsync<WireRequest>(input, default));
    }

    [Fact]
    public async Task Wire_RejectsUnknownFieldsAndTruncation()
    {
        var json = Encoding.UTF8.GetBytes("{\"Version\":1,\"Operation\":\"hello\",\"Executable\":\"shell\"}");
        using var input = new MemoryStream();
        var prefix = new byte[4];
        BinaryPrimitives.WriteInt32BigEndian(prefix, json.Length);
        input.Write(prefix); input.Write(json); input.Position = 0;
        await Assert.ThrowsAsync<RemoteException>(() => RemoteWire.ReadAsync<WireRequest>(input, default));
        input.SetLength(6); input.Position = 0;
        await Assert.ThrowsAsync<EndOfStreamException>(() => RemoteWire.ReadAsync<WireRequest>(input, default));
    }

    [Fact]
    public async Task Body_NeverConsumesTheNextFrameAndRejectsTruncation()
    {
        using var input = new MemoryStream([1, 2, 3, 99]);
        using (var body = new RemoteBodyStream(input, 3))
        {
            using var output = new MemoryStream();
            await body.CopyToAsync(output);
            output.ToArray().Should().Equal(1, 2, 3);
        }
        input.ReadByte().Should().Be(99);
        using var truncated = new RemoteBodyStream(Stream.Null, 1);
        await Assert.ThrowsAsync<EndOfStreamException>(() => truncated.CopyToAsync(Stream.Null));
    }
}
