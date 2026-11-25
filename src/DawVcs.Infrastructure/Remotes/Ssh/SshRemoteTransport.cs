using System.ComponentModel;
using System.Diagnostics;

using DawVcs.Domain.Common;
using DawVcs.Domain.Hashing;
using DawVcs.Domain.Remotes;
using DawVcs.Domain.Storage;
using DawVcs.Infrastructure.Configuration;

namespace DawVcs.Infrastructure.Remotes.Ssh;

/// <summary>One transient SSH process, serialized requests, streamed bounded binary bodies.</summary>
public sealed class SshRemoteTransport : IRemoteTransport, IRemotePushTransport
{
    private readonly DawVcsAddress _address;
    private readonly Func<ProcessStartInfo> _startInfo;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private Process? _process;
    private Stream? _input;
    private Stream? _output;
    private RemoteHandshake? _handshake;
    private bool _faulted;
    private bool _disposed;
    public string RemoteUrl { get; }

    public SshRemoteTransport(string remoteUrl) : this(remoteUrl, null) { }
    internal SshRemoteTransport(string remoteUrl, Func<ProcessStartInfo>? startInfo)
    {
        RemoteUrl = remoteUrl;
        _address = DawVcsAddress.Parse(remoteUrl);
        _startInfo = startInfo ?? _address.CreateStartInfo;
    }

    private async Task ConnectAsync(CancellationToken ct)
    {
        if (_handshake is not null) return;
        _process = new Process { StartInfo = _startInfo() };
        try
        {
            if (!_process.Start()) throw new RemoteException("Could not start OpenSSH.");
        }
        catch (Win32Exception) { throw new RemoteException("OpenSSH client could not be started. Install ssh and make it available in PATH."); }
        _input = _process.StandardInput.BaseStream;
        _output = _process.StandardOutput.BaseStream;
        _ = DrainErrorsAsync(_process.StandardError);
        await RemoteWire.WriteAsync(_input, new WireRequest { Operation = "hello", Repository = _address.Repository }, ct).ConfigureAwait(false);
        var reply = await ReadReplyAsync(ct).ConfigureAwait(false);
        if (reply.Configuration is null || reply.Configuration.Length > 256 * 1024)
            throw new IncompatibleRemoteRepositoryException("Missing or oversized SSH repository configuration.");
        var config = YamlRepositoryConfigManager.Parse(reply.Configuration);
        _handshake = new RemoteHandshake(config.SchemaVersion, config.RepositoryId) { Configuration = config };
    }

    private static async Task DrainErrorsAsync(StreamReader stderr)
    {
        try
        {
            var buffer = new char[2048];
            while (await stderr.ReadAsync(buffer).ConfigureAwait(false) != 0) { }
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException) { }
    }

    private async Task<WireResponse> ReadReplyAsync(CancellationToken ct)
    {
        var response = await RemoteWire.ReadAsync<WireResponse>(_output!, ct).ConfigureAwait(false)
            ?? throw new RemoteException("SSH connection ended. Check the trusted host key, SSH key access and host DAWVC configuration.");
        if (response.Version != 1) throw new IncompatibleRemoteRepositoryException("Unsupported DAWVC wire protocol version.");
        switch (response.Error)
        {
            case null: return response;
            case "incompatible": throw new IncompatibleRemoteRepositoryException("Incompatible DAWVC remote protocol or repository.");
            case "non-fast-forward": throw new PushNonFastForwardException("SSH remote", "destination");
            case "integrity": throw new InvalidEnvelopeException("Remote rejected an unsafe, incomplete or corrupt object.");
            case "validation": throw new ArgumentException("Remote rejected invalid protocol arguments.");
            case "denied": throw new PushRejectedException("Remote denied repository access, write permission, quota or branch update.");
            default: throw new RemoteException("Remote session failed or exceeded its limits. Inspect host-side configuration locally.");
        }
    }

    private async Task<WireResponse> ExchangeAsync(WireRequest request, CancellationToken ct)
    {
        await RemoteWire.WriteAsync(_input!, request, ct).ConfigureAwait(false);
        return await ReadReplyAsync(ct).ConfigureAwait(false);
    }

    private async Task<T> CallAsync<T>(Func<CancellationToken, Task<T>> action, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(5));
        using var registration = timeout.Token.Register(Abort);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_faulted) throw new RemoteException("SSH transport is closed; start a new operation.");
            await ConnectAsync(timeout.Token).ConfigureAwait(false);
            return await action(timeout.Token).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or OperationCanceledException or ObjectDisposedException)
        {
            Abort();
            cancellationToken.ThrowIfCancellationRequested();
            throw new RemoteException("SSH transfer failed, disconnected or timed out. Inspect remote refs before retrying an interrupted push.");
        }
        catch { Abort(); throw; }
        finally { _gate.Release(); }
    }

    public Task<RemoteHandshake> HandshakeAsync(CancellationToken cancellationToken = default)
        => CallAsync(_ => Task.FromResult(_handshake!), cancellationToken);

    public Task<IReadOnlyList<RemoteRef>> ListRefsAsync(CancellationToken cancellationToken = default)
        => CallAsync<IReadOnlyList<RemoteRef>>(async ct =>
        {
            var reply = await ExchangeAsync(new WireRequest { Operation = "refs" }, ct).ConfigureAwait(false);
            if (reply.Refs is null || reply.Refs.Length > 4096) throw new RemoteException("Invalid advertised references.");
            return reply.Refs.Select(r => new RemoteRef(BranchName.Parse(r.Name).Value, CommitId.Parse(r.Commit))).ToArray();
        }, cancellationToken);

    public Task<bool> ObjectExistsAsync(ContentHash hash, CancellationToken cancellationToken = default)
        => CallAsync(async ct => (await ExchangeAsync(new WireRequest { Operation = "exists", Hash = hash.ToString() }, ct).ConfigureAwait(false)).Exists, cancellationToken);

    public async Task<Stream?> OpenObjectPayloadStreamAsync(ContentHash hash, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(5));
        var registration = timeout.Token.Register(Abort);
        var handedOff = false;
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_faulted) throw new RemoteException("SSH transport is closed.");
            await ConnectAsync(timeout.Token).ConfigureAwait(false);
            var reply = await ExchangeAsync(new WireRequest { Operation = "get", Hash = hash.ToString() }, timeout.Token).ConfigureAwait(false);
            if (!reply.Exists) return null;
            if (reply.Length < ObjectEnvelopeHeader.HeaderSize || reply.Length - ObjectEnvelopeHeader.HeaderSize > RemoteWire.MaxObjectBytes)
                throw new InvalidEnvelopeException("Invalid remote object body length.");
            var stream = new RemoteBodyStream(_output!, reply.Length, complete =>
            {
                if (!complete) Abort();
                registration.Dispose();
                timeout.Dispose();
                _gate.Release();
            }, timeout.Token);
            handedOff = true;
            return stream;
        }
        catch (Exception ex) when (ex is IOException or OperationCanceledException or ObjectDisposedException)
        {
            Abort();
            cancellationToken.ThrowIfCancellationRequested();
            throw new RemoteException("SSH object read failed or timed out.");
        }
        catch { Abort(); throw; }
        finally
        {
            if (!handedOff) { registration.Dispose(); timeout.Dispose(); _gate.Release(); }
        }
    }

    public Task ValidatePushAsync(BranchName branch, CommitId? expectedTip, CancellationToken cancellationToken = default)
        => CallAsync(async ct => await ExchangeAsync(new WireRequest { Operation = "validate", Branch = branch.Value, ExpectedTip = expectedTip?.ToString() }, ct).ConfigureAwait(false), cancellationToken);

    public async Task<IRemotePushSession> BeginPushAsync(BranchName branch, CommitId? expectedTip, CancellationToken cancellationToken = default)
    {
        await CallAsync(async ct => await ExchangeAsync(new WireRequest { Operation = "begin", Branch = branch.Value, ExpectedTip = expectedTip?.ToString() }, ct).ConfigureAwait(false), cancellationToken).ConfigureAwait(false);
        return new SshPushSession(this);
    }

    private sealed class SshPushSession(SshRemoteTransport owner) : IRemotePushSession
    {
        private bool _disposed;
        public Task<bool> ReceiveObjectAsync(ObjectEnvelopeHeader header, Stream payload, CancellationToken cancellationToken = default)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (header.PayloadLength > (ulong)RemoteWire.MaxObjectBytes) throw new InvalidEnvelopeException("Object exceeds SSH protocol limits.");
            return owner.CallAsync(async ct =>
            {
                var reply = await owner.ExchangeAsync(new WireRequest { Operation = "put", Hash = header.PayloadHash.ToString(), Type = (int)header.ObjectType, Length = (long)header.PayloadLength }, ct).ConfigureAwait(false);
                if (!reply.Ready) return false;
                using var body = new RemoteBodyStream(payload, (long)header.PayloadLength);
                await body.CopyToAsync(owner._input!, ct).ConfigureAwait(false);
                if (await payload.ReadAsync(new byte[1], ct).ConfigureAwait(false) != 0) throw new InvalidEnvelopeException("Upload has trailing bytes.");
                await owner._input!.FlushAsync(ct).ConfigureAwait(false);
                return (await owner.ReadReplyAsync(ct).ConfigureAwait(false)).Received;
            }, cancellationToken);
        }
        public Task PublishAsync(CommitId newTip, CancellationToken cancellationToken = default)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return owner.CallAsync(async ct => await owner.ExchangeAsync(new WireRequest { Operation = "publish", Tip = newTip.ToString() }, ct).ConfigureAwait(false), cancellationToken);
        }
        public void Dispose() { if (!_disposed) { _disposed = true; owner.Abort(); } }
    }

    private void Abort()
    {
        _faulted = true;
        try { if (_process is { HasExited: false }) _process.Kill(entireProcessTree: true); }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception) { }
    }
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Abort();
        if (_process is not null)
        {
            try { _process.WaitForExit(2000); } catch (InvalidOperationException) { }
            _process.Dispose();
        }
    }
}
