using System.Buffers.Binary;
using System.Text.Json;
using System.Text.Json.Serialization;

using DawVcs.Domain.Remotes;

namespace DawVcs.Infrastructure.Remotes.Ssh;

internal sealed record WireRequest
{
    [JsonRequired]
    public int Version { get; init; } = 1;
    public string Operation { get; init; } = "";
    public string? Repository { get; init; }
    public string? Branch { get; init; }
    public string? ExpectedTip { get; init; }
    public string? Tip { get; init; }
    public string? Hash { get; init; }
    public int Type { get; init; }
    public long Length { get; init; }
}
internal sealed record WireRef(string Name, string Commit);
internal sealed record WireResponse
{
    [JsonRequired]
    public int Version { get; init; } = 1;
    public string? Error { get; init; }
    public string? Configuration { get; init; }
    public WireRef[]? Refs { get; init; }
    public bool Exists { get; init; }
    public bool Ready { get; init; }
    public bool Received { get; init; }
    public long Length { get; init; }
}

internal static class RemoteWire
{
    public const int MaxFrameBytes = 1024 * 1024;
    public const long MaxObjectBytes = 8L * 1024 * 1024 * 1024;
    private static readonly JsonSerializerOptions Options = new() { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow, MaxDepth = 16 };

    public static async Task WriteAsync<T>(Stream stream, T frame, CancellationToken cancellationToken)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(frame, Options);
        if (bytes.Length > MaxFrameBytes) throw new RemoteException("Remote control frame exceeds the protocol limit.");
        var prefix = new byte[4];
        BinaryPrimitives.WriteInt32BigEndian(prefix, bytes.Length);
        await stream.WriteAsync(prefix, cancellationToken).AsTask().WaitAsync(cancellationToken).ConfigureAwait(false);
        await stream.WriteAsync(bytes, cancellationToken).AsTask().WaitAsync(cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    public static async Task<T?> ReadAsync<T>(Stream stream, CancellationToken cancellationToken) where T : class
    {
        var prefix = new byte[4];
        var first = await stream.ReadAsync(prefix.AsMemory(0, 1), cancellationToken).AsTask().WaitAsync(cancellationToken).ConfigureAwait(false);
        if (first == 0) return null;
        await stream.ReadExactlyAsync(prefix.AsMemory(1), cancellationToken).AsTask().WaitAsync(cancellationToken).ConfigureAwait(false);
        var length = BinaryPrimitives.ReadInt32BigEndian(prefix);
        if (length <= 0 || length > MaxFrameBytes) throw new RemoteException("Invalid remote control frame length.");
        var payload = new byte[length];
        await stream.ReadExactlyAsync(payload, cancellationToken).AsTask().WaitAsync(cancellationToken).ConfigureAwait(false);
        try { return JsonSerializer.Deserialize<T>(payload, Options) ?? throw new RemoteException("Empty remote control frame."); }
        catch (JsonException) { throw new RemoteException("Invalid remote control frame."); }
    }
}

/// <summary>Non-owning bounded stream; binary bodies cannot consume the next control frame.</summary>
internal sealed class RemoteBodyStream(Stream input, long length, Action<bool>? onDispose = null, CancellationToken lifetime = default) : Stream
{
    private readonly long _length = length;
    private long _remaining = length;
    private bool _disposed;
    public long Remaining => _remaining;
    public override bool CanRead => !_disposed;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => _length;
    public override long Position { get => _length - _remaining; set => throw new NotSupportedException(); }
    public override int Read(byte[] buffer, int offset, int count) => ReadAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();
    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (buffer.Length == 0 || _remaining == 0) return 0;
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(lifetime, cancellationToken);
        var count = await input.ReadAsync(buffer[..(int)Math.Min(buffer.Length, _remaining)], linked.Token).AsTask().WaitAsync(linked.Token).ConfigureAwait(false);
        if (count == 0) throw new EndOfStreamException("Truncated remote object body.");
        _remaining -= count;
        return count;
    }
    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) => ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
    protected override void Dispose(bool disposing)
    {
        if (!_disposed) { _disposed = true; onDispose?.Invoke(_remaining == 0); }
        base.Dispose(disposing);
    }
    public override void Flush() { }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
