using DawVcs.Application.Exceptions;
using DawVcs.Domain.Common;
using DawVcs.Domain.Hashing;
using DawVcs.Domain.Remotes;
using DawVcs.Domain.Storage;
using DawVcs.Infrastructure.Configuration;

namespace DawVcs.Infrastructure.Remotes.Ssh;

/// <summary>A transient, bounded stdio receiver. Authentication belongs to the SSH daemon.</summary>
public static class RemoteProtocolServer
{
    public static async Task<int> RunAsync(Stream input, Stream output, ServeConfiguration policy, CancellationToken cancellationToken = default)
    {
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        lifetime.CancelAfter(TimeSpan.FromSeconds(policy.SessionTimeoutSeconds));
        var ct = lifetime.Token;
        IRemotePushSession? push = null;
        FileSystemRemoteTransport? transport = null;
        long transferred = 0;
        try
        {
            var hello = await RemoteWire.ReadAsync<WireRequest>(input, ct).ConfigureAwait(false);
            if (hello is null) return 0;
            if (hello.Version != 1 || hello.Operation != "hello") throw new IncompatibleRemoteRepositoryException("Protocol version mismatch.");
            var alias = hello.Repository ?? throw new RemoteException("Repository alias required.");
            _ = DawVcsAddress.Parse("dawvc://policy/" + alias);
            if (!policy.Repositories.TryGetValue(alias, out var hosted)) throw new PushRejectedException("Repository access denied.");
            FileSystemRemoteTransport.GuardWritablePath(hosted.Path);
            FileSystemRemoteTransport.GuardWritablePath(Path.Combine(hosted.Path, "dawvc.yaml"));
            transport = new FileSystemRemoteTransport(hosted.Path);
            var handshake = await transport.HandshakeAsync(ct).ConfigureAwait(false);
            await RemoteWire.WriteAsync(output, new WireResponse { Configuration = YamlRepositoryConfigManager.ToYaml(handshake.Configuration!) }, ct).ConfigureAwait(false);
            for (var count = 1; count < policy.MaxRequests; count++)
            {
                var request = await RemoteWire.ReadAsync<WireRequest>(input, ct).ConfigureAwait(false);
                if (request is null) return 0;
                if (request.Version != 1 || request.Repository is not null) throw new IncompatibleRemoteRepositoryException("Protocol state/version mismatch.");
                var response = new WireResponse();
                switch (request.Operation)
                {
                    case "refs":
                        var refs = await transport.ListRefsAsync(ct).ConfigureAwait(false);
                        if (refs.Count > 4096) throw new RemoteException("Too many advertised references.");
                        response = response with { Refs = refs.Select(r => new WireRef(r.Name, r.HeadCommit.ToString())).ToArray() };
                        break;
                    case "exists":
                        response = response with { Exists = await transport.ObjectExistsAsync(ParseHash(request.Hash), ct).ConfigureAwait(false) };
                        break;
                    case "get":
                        await using (var body = await transport.OpenObjectPayloadStreamAsync(ParseHash(request.Hash), ct).ConfigureAwait(false))
                        {
                            if (body is null) break;
                            var length = body.Length;
                            if (length < ObjectEnvelopeHeader.HeaderSize || length - ObjectEnvelopeHeader.HeaderSize > policy.MaxObjectBytes)
                                throw new InvalidEnvelopeException("Object exceeds host limits.");
                            transferred = Charge(transferred, length, policy.MaxTransferBytes);
                            await RemoteWire.WriteAsync(output, response with { Exists = true, Length = length }, ct).ConfigureAwait(false);
                            await body.CopyToAsync(output, ct).WaitAsync(ct).ConfigureAwait(false);
                            await output.FlushAsync(ct).WaitAsync(ct).ConfigureAwait(false);
                            continue;
                        }
                    case "validate":
                        RequireWrite(hosted);
                        await transport.ValidatePushAsync(ParseBranch(request.Branch), ParseTip(request.ExpectedTip), ct).ConfigureAwait(false);
                        break;
                    case "begin":
                        RequireWrite(hosted);
                        if (push is not null) throw new PushRejectedException("Push already active.");
                        push = await transport.BeginPushAsync(ParseBranch(request.Branch), ParseTip(request.ExpectedTip), ct).ConfigureAwait(false);
                        break;
                    case "put":
                        RequireWrite(hosted);
                        if (push is null) throw new PushRejectedException("No active push.");
                        var hash = ParseHash(request.Hash);
                        if (request.Type is < 1 or > 4 || request.Length < 0 || request.Length > policy.MaxObjectBytes ||
                            (request.Type != (int)ObjectType.Blob && request.Length > 2 * 1024 * 1024))
                            throw new InvalidEnvelopeException("Upload type or size exceeds limits.");
                        var header = new ObjectEnvelopeHeader((ObjectType)request.Type, (ulong)request.Length, hash);
                        if (await transport.ObjectExistsAsync(hash, ct).ConfigureAwait(false))
                        {
                            await push.ReceiveObjectAsync(header, Stream.Null, ct).ConfigureAwait(false);
                            break;
                        }
                        transferred = Charge(transferred, request.Length, policy.MaxTransferBytes);
                        await RemoteWire.WriteAsync(output, response with { Ready = true }, ct).ConfigureAwait(false);
                        using (var body = new RemoteBodyStream(input, request.Length))
                        {
                            var received = await push.ReceiveObjectAsync(header, body, ct).ConfigureAwait(false);
                            await body.CopyToAsync(Stream.Null, ct).ConfigureAwait(false);
                            response = response with { Received = received };
                        }
                        break;
                    case "publish":
                        RequireWrite(hosted);
                        if (push is null) throw new PushRejectedException("No active push.");
                        await push.PublishAsync(ParseTip(request.Tip) ?? throw new ArgumentException("Missing tip."), ct).ConfigureAwait(false);
                        push.Dispose();
                        push = null;
                        break;
                    default: throw new RemoteException("Unsupported remote operation.");
                }
                await RemoteWire.WriteAsync(output, response, ct).ConfigureAwait(false);
            }
            throw new RemoteException("Session request limit exceeded.");
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            var code = ex switch
            {
                IncompatibleRemoteRepositoryException => "incompatible",
                PushNonFastForwardException => "non-fast-forward",
                PushRejectedException => "denied",
                InvalidEnvelopeException or PathSecurityException => "integrity",
                ArgumentException => "validation",
                OperationCanceledException => "timeout",
                _ => "remote"
            };
            // Never echo raw host paths, configuration, stderr or exception messages.
            try
            {
                using var errorTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                await RemoteWire.WriteAsync(output, new WireResponse { Error = code }, errorTimeout.Token).ConfigureAwait(false);
            }
            catch (Exception error) when (error is IOException or OperationCanceledException or ObjectDisposedException) { }
            return 8;
        }
        finally { push?.Dispose(); transport?.Dispose(); }
    }

    private static void RequireWrite(HostedRepository repository)
    {
        if (!repository.AllowPush) throw new PushRejectedException("Write access denied.");
    }
    private static ContentHash ParseHash(string? hash) => ContentHash.TryParse(hash, out var value) ? value : throw new ArgumentException("Invalid hash.");
    private static CommitId? ParseTip(string? tip) => tip is null ? null : CommitId.TryParse(tip, out var value) ? value : throw new ArgumentException("Invalid tip.");
    private static BranchName ParseBranch(string? branch) => branch is not null && branch.Length <= 256 ? BranchName.Parse(branch) : throw new ArgumentException("Invalid branch.");
    private static long Charge(long used, long length, long limit) => length <= limit - used ? used + length : throw new PushRejectedException("Session transfer quota exceeded.");
}
