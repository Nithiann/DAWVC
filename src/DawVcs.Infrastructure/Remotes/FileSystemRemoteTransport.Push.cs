using DawVcs.Application.Checkouts;
using DawVcs.Application.Operations;
using DawVcs.Application.Recovery;
using DawVcs.Application.Remotes;
using DawVcs.Domain.Common;
using DawVcs.Domain.Hashing;
using DawVcs.Domain.Remotes;
using DawVcs.Domain.Repositories;
using DawVcs.Domain.Storage;
using DawVcs.Infrastructure.FileSystem;

namespace DawVcs.Infrastructure.Remotes;

public sealed partial class FileSystemRemoteTransport
{
    public Task ValidatePushAsync(BranchName branch, CommitId? expectedTip, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        PathSecurityGuard.ValidatePath(branch.Value);
        GuardWritablePath(_dotDawvcPath);
        GuardWritablePath(Path.Combine(_dotDawvcPath, "ref-write.lock"));
        GuardWritablePath(_objectsPath);
        GuardWritablePath(Path.Combine(_dotDawvcPath, "incoming"));
        GuardWritablePath(GetPushRefPath(branch));
        if (!Directory.Exists(_objectsPath) || !Directory.Exists(_refsHeadsPath))
            throw new PushRejectedException("Destination is not an initialized repository with objects and refs/heads directories.");

        if (!string.Equals(_configDir, _dotDawvcPath, StringComparison.Ordinal))
        {
            var headPath = Path.Combine(_dotDawvcPath, "HEAD");
            GuardWritablePath(headPath);
            if (!File.Exists(headPath)) throw new PushRejectedException("Destination HEAD is missing; refusing push.");
            var head = File.ReadAllText(headPath).Trim();
            const string prefix = "ref: refs/heads/";
            if (!head.StartsWith(prefix, StringComparison.Ordinal) ||
                !BranchName.TryCreate(head[prefix.Length..], out var active, out _))
                throw new PushRejectedException("Destination HEAD is invalid or detached; refusing push.");
            if (string.Equals(active.Value, branch.Value, StringComparison.OrdinalIgnoreCase))
                throw new PushRejectedException("Cannot push to the checked-out destination branch. Use an inactive branch or a bare repository.");
        }

        var refPath = GetPushRefPath(branch);
        CommitId? actual = null;
        if (File.Exists(refPath))
        {
            if (!CommitId.TryParse(File.ReadAllText(refPath).Trim(), out var parsed))
                throw new PushRejectedException("Destination branch reference is malformed; refusing push.");
            actual = parsed;
        }
        if (actual != expectedTip)
            throw new PushRejectedException("Destination branch changed since planning. Retry after fetching remote changes.");
        return Task.CompletedTask;
    }

    public async Task<IRemotePushSession> BeginPushAsync(BranchName branch, CommitId? expectedTip, CancellationToken cancellationToken = default)
    {
        await ValidatePushAsync(branch, expectedTip, cancellationToken).ConfigureAwait(false);
        RepositoryOperation operation;
        try { operation = RepositoryOperation.Enter(_configDir, remoteMetadata: _dotDawvcPath, cancellationToken: cancellationToken); }
        catch (RepositoryBusyException ex) { throw new RemoteException("Destination has another active operation. Retry later.", ex); }
        FileStream refLock;
        try { refLock = RepositoryRefLock.Acquire(_dotDawvcPath); }
        catch (IOException ex) { operation.Dispose(); throw new RemoteException("Destination has another active ref writer. Retry later.", ex); }
        catch { operation.Dispose(); throw; }
        try
        {
            WorkspaceRecoveryGuard.ThrowIfPending(_configDir);
            await ValidatePushAsync(branch, expectedTip, cancellationToken).ConfigureAwait(false);
            return new PushSession(this, branch, expectedTip, refLock, operation);
        }
        catch (WorkspaceRecoveryRequiredException ex) { refLock.Dispose(); operation.Dispose(); throw new RemoteException(ex.Message, ex); }
        catch { refLock.Dispose(); operation.Dispose(); throw; }
    }

    private string GetPushRefPath(BranchName branch) => Path.Combine(_refsHeadsPath, branch.Value.Replace('/', Path.DirectorySeparatorChar));

    // Fail closed on links/junctions in existing metadata path components, including the root.
    internal static void GuardWritablePath(string path)
    {
        for (var current = Path.GetFullPath(path); current is not null; current = Path.GetDirectoryName(current))
        {
            try
            {
                if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                    throw new PushRejectedException("Repository transport does not follow symbolic links or reparse points in repository metadata.");
            }
            catch (FileNotFoundException) { }
            catch (DirectoryNotFoundException) { }
        }
    }

    private sealed class PushSession : IRemotePushSession
    {
        private readonly FileSystemRemoteTransport _owner;
        private readonly BranchName _branch;
        private readonly CommitId? _expectedTip;
        private readonly FileStream _refLock;
        private readonly RepositoryOperation _operation;
        private readonly string _sessionPath;
        private readonly List<VerifiedIncomingObject> _received = [];
        private bool _disposed;
        private bool _published;

        public PushSession(FileSystemRemoteTransport owner, BranchName branch, CommitId? expectedTip, FileStream refLock, RepositoryOperation operation)
        {
            _owner = owner;
            _branch = branch;
            _expectedTip = expectedTip;
            _refLock = refLock;
            _operation = operation;
            _sessionPath = Path.Combine(owner._dotDawvcPath, "incoming", "push-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_sessionPath);
        }

        public async Task<bool> ReceiveObjectAsync(ObjectEnvelopeHeader header, Stream payload, CancellationToken cancellationToken = default)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_published) throw new InvalidOperationException("Push has already been published.");
            cancellationToken.ThrowIfCancellationRequested();
            var hex = header.PayloadHash.ToString();
            var target = Path.Combine(_owner._objectsPath, hex[..2], hex[2..]);
            GuardWritablePath(target);
            if (File.Exists(target))
            {
                await VerifyFileAsync(target, header, cancellationToken).ConfigureAwait(false);
                return false;
            }
            var incoming = Path.Combine(_sessionPath, hex);
            await using (var output = new FileStream(incoming, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, useAsync: true))
            {
                var buffer = new byte[ObjectEnvelopeHeader.HeaderSize];
                header.WriteTo(buffer);
                await output.WriteAsync(buffer, cancellationToken).ConfigureAwait(false);
                var chunk = new byte[65536];
                ulong copied = 0;
                while (copied < header.PayloadLength)
                {
                    var length = (int)Math.Min((ulong)chunk.Length, header.PayloadLength - copied);
                    var read = await payload.ReadAsync(chunk.AsMemory(0, length), cancellationToken).ConfigureAwait(false);
                    if (read == 0) throw new InvalidEnvelopeException("Truncated push payload.");
                    await output.WriteAsync(chunk.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                    copied += (ulong)read;
                }
                if (await payload.ReadAsync(chunk.AsMemory(0, 1), cancellationToken).ConfigureAwait(false) != 0)
                    throw new InvalidEnvelopeException("Push payload contains trailing bytes.");
                await output.FlushAsync(cancellationToken).ConfigureAwait(false);
                output.Flush(flushToDisk: true);
            }
            await VerifyFileAsync(incoming, header, cancellationToken).ConfigureAwait(false);
            _received.Add(new VerifiedIncomingObject(header.PayloadHash, header.ObjectType, checked((long)header.PayloadLength), incoming));
            return true;
        }

        private static async Task VerifyFileAsync(string path, ObjectEnvelopeHeader expected, CancellationToken cancellationToken)
        {
            await using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, useAsync: true);
            var verified = await IncomingObjectVerifier.VerifyAndWriteAsync(input, Stream.Null, expected.PayloadHash,
                path, expected.ObjectType, cancellationToken).ConfigureAwait(false);
            if ((ulong)verified.PayloadLength != expected.PayloadLength || input.Position != input.Length)
                throw new InvalidEnvelopeException("Object length does not match its expected envelope.");
        }

        public async Task PublishAsync(CommitId newTip, CancellationToken cancellationToken = default)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_published) throw new InvalidOperationException("Push has already been published.");
            await _owner.ValidatePushAsync(_branch, _expectedTip, cancellationToken).ConfigureAwait(false);
            var closure = await PushClosure.ReadAsync(newTip, ReadReceivedAsync, cancellationToken).ConfigureAwait(false);
            if (_expectedTip.HasValue && !closure.Commits.Contains(_expectedTip.Value.Value))
                throw new PushNonFastForwardException("destination", _branch.Value);
            foreach (var obj in _received.OrderBy(o => (int)o.ObjectType))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var hex = obj.Hash.ToString();
                var target = Path.Combine(_owner._objectsPath, hex[..2], hex[2..]);
                GuardWritablePath(target);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                // Never replace an immutable object, including one created by another actor.
                if (File.Exists(target))
                    await VerifyFileAsync(target, new ObjectEnvelopeHeader(obj.ObjectType, (ulong)obj.PayloadLength, obj.Hash), cancellationToken).ConfigureAwait(false);
                else
                    File.Move(obj.QuarantineFilePath, target);
            }
            await _owner.ValidatePushAsync(_branch, _expectedTip, cancellationToken).ConfigureAwait(false);
            var refPath = _owner.GetPushRefPath(_branch);
            GuardWritablePath(refPath);
            cancellationToken.ThrowIfCancellationRequested();
            // No cancellable work after the publication boundary: a successful rename is success.
            AtomicFileWriter.WriteAtomic(refPath, newTip + "\n");
            _published = true;
        }

        private async Task<PushObject> ReadReceivedAsync(ContentHash hash, ObjectType type, CancellationToken cancellationToken)
        {
            var hex = hash.ToString();
            var incoming = Path.Combine(_sessionPath, hex);
            var path = File.Exists(incoming) ? incoming : Path.Combine(_owner._objectsPath, hex[..2], hex[2..]);
            GuardWritablePath(path);
            if (!File.Exists(path)) throw new InvalidEnvelopeException("Push closure contains a missing object.");
            await using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, useAsync: true);
            var buffer = new byte[ObjectEnvelopeHeader.HeaderSize];
            await input.ReadExactlyAsync(buffer, cancellationToken).ConfigureAwait(false);
            var header = ObjectEnvelopeHeader.ReadFrom(buffer);
            if (header.ObjectType != type || header.PayloadHash != hash ||
                (type != ObjectType.Blob && header.PayloadLength > (ulong)IncomingObjectVerifier.MaxMetadataPayloadBytes))
                throw new InvalidEnvelopeException("Invalid received push object.");
            input.Position = 0;
            await VerifyFileAsync(path, header, cancellationToken).ConfigureAwait(false);
            byte[]? metadata = null;
            if (type != ObjectType.Blob)
            {
                input.Position = ObjectEnvelopeHeader.HeaderSize;
                metadata = new byte[checked((int)header.PayloadLength)];
                await input.ReadExactlyAsync(metadata, cancellationToken).ConfigureAwait(false);
            }
            return new PushObject(header, metadata);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            try
            {
                if (Directory.Exists(_sessionPath)) Directory.Delete(_sessionPath, recursive: true);
            }
            catch (IOException) { /* Best-effort cleanup must not turn published success into failure. */ }
            catch (UnauthorizedAccessException) { /* Preserve remaining quarantine for manual cleanup. */ }
            finally { try { _refLock.Dispose(); } finally { _operation.Dispose(); } }
        }
    }
}
