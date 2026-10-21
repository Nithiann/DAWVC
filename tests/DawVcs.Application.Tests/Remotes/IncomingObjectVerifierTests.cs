using System.Text;

using DawVcs.Application.Exceptions;
using DawVcs.Application.Remotes;
using DawVcs.Domain.Hashing;
using DawVcs.Domain.Serialization;
using DawVcs.Domain.Storage;

using FluentAssertions;

using Xunit;

namespace DawVcs.Application.Tests.Remotes;

public sealed class IncomingObjectVerifierTests
{
    [Fact]
    public async Task VerifyAndWriteAsync_ValidBlob_SucceedsAndReturnsVerifiedObject()
    {
        var payload = "Hello audio world!"u8.ToArray();
        var hash = Blake3ContentHasher.Hash(payload);
        var envelope = CreateEnvelope(ObjectType.Blob, hash, payload);

        using var sourceStream = new MemoryStream(envelope);
        using var destinationStream = new MemoryStream();

        var verified = await IncomingObjectVerifier.VerifyAndWriteAsync(
            sourceStream,
            destinationStream,
            hash,
            "/quarantine/blob.tmp",
            ObjectType.Blob);

        verified.Hash.Should().Be(hash);
        verified.ObjectType.Should().Be(ObjectType.Blob);
        verified.PayloadLength.Should().Be(payload.Length);
        destinationStream.ToArray().Should().Equal(envelope);
    }

    [Fact]
    public async Task VerifyAndWriteAsync_InvalidMagicBytes_ThrowsInvalidMagicBytesException()
    {
        var payload = "data"u8.ToArray();
        var hash = Blake3ContentHasher.Hash(payload);
        var envelope = CreateEnvelope(ObjectType.Blob, hash, payload);
        envelope[0] = 0xFF; // corrupt magic

        using var sourceStream = new MemoryStream(envelope);
        using var destinationStream = new MemoryStream();

        var act = () => IncomingObjectVerifier.VerifyAndWriteAsync(
            sourceStream,
            destinationStream,
            hash,
            "/quarantine/blob.tmp");

        await act.Should().ThrowAsync<InvalidMagicBytesException>();
    }

    [Fact]
    public async Task VerifyAndWriteAsync_UnsupportedVersion_ThrowsUnsupportedEnvelopeVersionException()
    {
        var payload = "data"u8.ToArray();
        var hash = Blake3ContentHasher.Hash(payload);
        var envelope = CreateEnvelope(ObjectType.Blob, hash, payload);
        envelope[4] = 0x99; // corrupt version

        using var sourceStream = new MemoryStream(envelope);
        using var destinationStream = new MemoryStream();

        var act = () => IncomingObjectVerifier.VerifyAndWriteAsync(
            sourceStream,
            destinationStream,
            hash,
            "/quarantine/blob.tmp");

        await act.Should().ThrowAsync<UnsupportedEnvelopeVersionException>();
    }

    [Fact]
    public async Task VerifyAndWriteAsync_TypeMismatch_ThrowsInvalidEnvelopeException()
    {
        var payload = "data"u8.ToArray();
        var hash = Blake3ContentHasher.Hash(payload);
        var envelope = CreateEnvelope(ObjectType.Blob, hash, payload);

        using var sourceStream = new MemoryStream(envelope);
        using var destinationStream = new MemoryStream();

        var act = () => IncomingObjectVerifier.VerifyAndWriteAsync(
            sourceStream,
            destinationStream,
            hash,
            "/quarantine/blob.tmp",
            expectedType: ObjectType.Commit);

        await act.Should().ThrowAsync<InvalidEnvelopeException>()
            .WithMessage("*type mismatch*");
    }

    [Fact]
    public async Task VerifyAndWriteAsync_TruncatedPayload_ThrowsPayloadLengthMismatchException()
    {
        var payload = "0123456789"u8.ToArray();
        var hash = Blake3ContentHasher.Hash(payload);
        var envelope = CreateEnvelope(ObjectType.Blob, hash, payload);

        // Truncate by 3 bytes
        var truncated = envelope.AsSpan(0, envelope.Length - 3).ToArray();

        using var sourceStream = new MemoryStream(truncated);
        using var destinationStream = new MemoryStream();

        var act = () => IncomingObjectVerifier.VerifyAndWriteAsync(
            sourceStream,
            destinationStream,
            hash,
            "/quarantine/blob.tmp");

        await act.Should().ThrowAsync<PayloadLengthMismatchException>();
    }

    [Fact]
    public async Task VerifyAndWriteAsync_CorruptedPayloadBytes_ThrowsPayloadHashMismatchException()
    {
        var payload = "original pristine content"u8.ToArray();
        var hash = Blake3ContentHasher.Hash(payload);
        var envelope = CreateEnvelope(ObjectType.Blob, hash, payload);

        // Modify a payload byte
        envelope[^1] = (byte)(envelope[^1] ^ 0x01);

        using var sourceStream = new MemoryStream(envelope);
        using var destinationStream = new MemoryStream();

        var act = () => IncomingObjectVerifier.VerifyAndWriteAsync(
            sourceStream,
            destinationStream,
            hash,
            "/quarantine/blob.tmp");

        await act.Should().ThrowAsync<PayloadHashMismatchException>();
    }

    [Fact]
    public async Task VerifyAndWriteAsync_MetadataExceeds2MB_ThrowsInvalidEnvelopeException()
    {
        var payloadLength = (ulong)(2 * 1024 * 1024 + 1);
        var fakeHash = Blake3ContentHasher.Hash("fake"u8);
        var header = new ObjectEnvelopeHeader(ObjectType.Commit, payloadLength, fakeHash);

        var headerBytes = new byte[ObjectEnvelopeHeader.HeaderSize];
        header.WriteTo(headerBytes);

        using var sourceStream = new MemoryStream(headerBytes);
        using var destinationStream = new MemoryStream();

        var act = () => IncomingObjectVerifier.VerifyAndWriteAsync(
            sourceStream,
            destinationStream,
            fakeHash,
            "/quarantine/commit.tmp",
            expectedType: ObjectType.Commit);

        await act.Should().ThrowAsync<InvalidEnvelopeException>()
            .WithMessage("*exceeds maximum allowed limit*");
    }

    [Fact]
    public async Task VerifyAndWriteAsync_SnapshotWithPathTraversal_ThrowsPathSecurityException()
    {
        var maliciousSnapshot = new SnapshotPayloadDto
        {
            SchemaVersion = 1,
            DawName = "FL Studio",
            ContainerKind = 1,
            AggregateHash = "abc",
            CreatedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            Entries =
            [
                new EntryPayloadDto
                {
                    Path = "../../Windows/System32/evil.dll",
                    BlobId = "deadbeef",
                    Hash = new string('a', 64),
                    Size = 100,
                    Role = 1
                }
            ]
        };

        var snapshotJson = CanonicalJsonSerializer.SerializeCanonical(maliciousSnapshot);
        var hash = Blake3ContentHasher.Hash(snapshotJson);
        var envelope = CreateEnvelope(ObjectType.Snapshot, hash, snapshotJson);

        using var sourceStream = new MemoryStream(envelope);
        using var destinationStream = new MemoryStream();

        var act = () => IncomingObjectVerifier.VerifyAndWriteAsync(
            sourceStream,
            destinationStream,
            hash,
            "/quarantine/snapshot.tmp",
            expectedType: ObjectType.Snapshot);

        await act.Should().ThrowAsync<PathSecurityException>()
            .WithMessage("*traversal*");
    }

    private static byte[] CreateEnvelope(ObjectType type, ContentHash payloadHash, byte[] payload)
    {
        var header = new ObjectEnvelopeHeader(type, (ulong)payload.Length, payloadHash);
        var bytes = new byte[ObjectEnvelopeHeader.HeaderSize + payload.Length];
        header.WriteTo(bytes.AsSpan(0, ObjectEnvelopeHeader.HeaderSize));
        payload.CopyTo(bytes.AsSpan(ObjectEnvelopeHeader.HeaderSize));
        return bytes;
    }
}
