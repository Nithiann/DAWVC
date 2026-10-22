using DawVcs.Application.Checkouts;
using DawVcs.Application.Exceptions;
using DawVcs.Domain.Hashing;
using DawVcs.Domain.Remotes;
using DawVcs.Domain.Serialization;
using DawVcs.Domain.Storage;

namespace DawVcs.Application.Remotes;

/// <summary>
/// Verifies incoming objects streamed from untrusted remotes before promotion (FR-FET-003..005, 008).
/// Enforces envelope integrity, payload hash verification, size limits, and path traversal guards.
/// </summary>
public static class IncomingObjectVerifier
{
    public const long MaxMetadataPayloadBytes = 2 * 1024 * 1024; // 2 MB per FR-FET-008
    private const int BufferSize = 64 * 1024;

    /// <summary>
    /// Reads and verifies an incoming enveloped object stream from a remote, writing it simultaneously
    /// into the quarantine destination stream, and performing all integrity and security checks.
    /// </summary>
    public static async Task<VerifiedIncomingObject> VerifyAndWriteAsync(
        Stream remoteSourceStream,
        Stream quarantineDestinationStream,
        ContentHash expectedHash,
        string quarantineFilePath,
        ObjectType? expectedType = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(remoteSourceStream);
        ArgumentNullException.ThrowIfNull(quarantineDestinationStream);

        // 1. Read envelope header (56 bytes)
        var headerBuffer = new byte[ObjectEnvelopeHeader.HeaderSize];
        var headerBytesRead = 0;
        while (headerBytesRead < ObjectEnvelopeHeader.HeaderSize)
        {
            var read = await remoteSourceStream.ReadAsync(
                headerBuffer.AsMemory(headerBytesRead, ObjectEnvelopeHeader.HeaderSize - headerBytesRead),
                cancellationToken).ConfigureAwait(false);

            if (read == 0)
            {
                throw new InvalidEnvelopeException($"Truncated envelope header: expected {ObjectEnvelopeHeader.HeaderSize} bytes but got {headerBytesRead}.");
            }

            headerBytesRead += read;
        }

        // Deserialize and validate header
        var header = ObjectEnvelopeHeader.ReadFrom(headerBuffer);

        if (expectedType.HasValue && header.ObjectType != expectedType.Value)
        {
            throw new InvalidEnvelopeException($"Object type mismatch: expected {expectedType.Value} but got {header.ObjectType}.");
        }

        if (header.PayloadHash != expectedHash)
        {
            throw new PayloadHashMismatchException($"Envelope header declared hash '{header.PayloadHash}' does not match expected object hash '{expectedHash}'.");
        }

        // Check metadata size limits (FR-FET-008: Commits and Snapshots max 2MB)
        if (header.ObjectType is ObjectType.Commit or ObjectType.Snapshot or ObjectType.Tree)
        {
            if (header.PayloadLength > (ulong)MaxMetadataPayloadBytes)
            {
                throw new InvalidEnvelopeException($"Metadata object {header.ObjectType} size {header.PayloadLength} exceeds maximum allowed limit of {MaxMetadataPayloadBytes} bytes.");
            }
        }

        // Write header to quarantine stream
        await quarantineDestinationStream.WriteAsync(headerBuffer.AsMemory(0, ObjectEnvelopeHeader.HeaderSize), cancellationToken).ConfigureAwait(false);

        // 2. Stream payload, compute BLAKE3 hash, and collect metadata bytes if needed for semantic validation
        using var hasher = new Blake3ContentHasher();
        MemoryStream? metadataPayloadBuffer = null;
        if (header.ObjectType == ObjectType.Snapshot)
        {
            metadataPayloadBuffer = new MemoryStream((int)header.PayloadLength);
        }

        try
        {
            var buffer = new byte[BufferSize];
            ulong totalPayloadRead = 0;

            while (totalPayloadRead < header.PayloadLength)
            {
                var remaining = header.PayloadLength - totalPayloadRead;
                var toRead = (int)Math.Min((ulong)buffer.Length, remaining);

                var bytesRead = await remoteSourceStream.ReadAsync(buffer.AsMemory(0, toRead), cancellationToken).ConfigureAwait(false);
                if (bytesRead == 0)
                {
                    throw new PayloadLengthMismatchException($"Truncated payload: expected {header.PayloadLength} bytes but stream ended at {totalPayloadRead} bytes.");
                }

                hasher.Update(buffer.AsSpan(0, bytesRead));
                await quarantineDestinationStream.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken).ConfigureAwait(false);

                metadataPayloadBuffer?.Write(buffer, 0, bytesRead);
                totalPayloadRead += (ulong)bytesRead;
            }

            // Verify no trailing extra bytes in stream if source has known length
            var computedHash = hasher.FinalizeHash();
            if (computedHash != header.PayloadHash)
            {
                throw new PayloadHashMismatchException($"Computed payload hash '{computedHash}' does not match header payload hash '{header.PayloadHash}'.");
            }

            if (computedHash != expectedHash)
            {
                throw new PayloadHashMismatchException($"Computed payload hash '{computedHash}' does not match expected hash '{expectedHash}'.");
            }

            // 3. Semantic validation for snapshots (Path Traversal Guard - FR-FET-005)
            if (header.ObjectType == ObjectType.Snapshot && metadataPayloadBuffer is not null)
            {
                ValidateSnapshotSecurity(metadataPayloadBuffer.ToArray());
            }

            await quarantineDestinationStream.FlushAsync(cancellationToken).ConfigureAwait(false);

            return new VerifiedIncomingObject(expectedHash, header.ObjectType, (long)header.PayloadLength, quarantineFilePath);
        }
        finally
        {
            metadataPayloadBuffer?.Dispose();
        }
    }

    private static void ValidateSnapshotSecurity(byte[] snapshotPayload)
    {
        var dto = ObjectPayloadSerializer.ReadSnapshotDto(snapshotPayload)
            ?? throw new InvalidEnvelopeException("Failed to parse snapshot payload JSON.");

        if (dto.Entries is not null && dto.Entries.Length > 0)
        {
            var paths = dto.Entries.Select(e => e.Path).ToList();
            // Validates against '..', absolute paths, invalid characters, case collisions, and duplicate paths
            PathSecurityGuard.ValidateAll(paths);
        }

        if (dto.Dependencies is not null)
        {
            foreach (var dep in dto.Dependencies)
            {
                if (!string.IsNullOrWhiteSpace(dep.RelativePath))
                {
                    PathSecurityGuard.ValidatePath(dep.RelativePath);
                }
            }
        }
    }
}
