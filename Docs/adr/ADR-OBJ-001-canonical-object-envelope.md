# ADR-OBJ-001: Canonical Object Envelope Format (v1)

- **Status:** Accepted
- **Date:** 2026-09-13
- **Authors:** DAWVC Contributors
- **Related Requirements:** FR-OBJ-006, FR-OBJ-007, FR-OBJ-008, NFR-INT-003, NFR-INT-005
- **Work Package:** WP-01 (Spike C) / WP-02

---

## Context & Problem Statement

DAWVC persists all objects (blobs, artifact trees, commit snapshots, and manifests) content-addressed in local object storage (`.dawvc/objects/`).
In accordance with requirement `FR-OBJ-006`, objects must contain a binary envelope so that storage corruption, partial writes, unrecognized compression modes, or future format versions are detected reliably and early before data is exposed to the repository or workspace.

## Considered Options

1. **Bare Content (Git-style raw zlib streams with ASCII header `blob <size>\0`):**
   - Requires parsing text strings within binary streams.
   - Provides no fixed-offset metadata or explicit payload hash in the header itself.
2. **TLV / Protocol Buffers Envelope:**
   - Highly flexible, but introduces serialization overhead and parsing complexity to high-throughput audio blob streams.
3. **Fixed 56-byte Binary Header (Canonical Envelope v1):**
   - Deterministic fixed-size header with little-endian fields and 8-byte alignment.
   - Fixed offsets for magic bytes, envelope version, object type, compression mode, payload length, and 32-byte BLAKE3 payload hash.

## Decision Outcome

We choose Option 3: **Fixed 56-byte Canonical Envelope Format (v1)**.

### Header Byte Layout (56 bytes, Little-Endian)

| Offset | Field | Type | Description / Expected Value |
|---|---|---|---|
| `0..3` | `Magic` | 4 bytes ASCII | Always `DWVC` (`0x44 0x57 0x56 0x43`) |
| `4..5` | `EnvelopeVersion` | `uint16` | Schema version, exactly `1` in MVP v0.1 |
| `6..7` | `ObjectType` | `uint16` | `1` = Blob, `2` = Tree, `3` = Snapshot, `4` = Commit |
| `8` | `CompressionMode` | `uint8` | `0` = None (uncompressed). Other values rejected |
| `9` | `Flags` | `uint8` | Reserved for future flags (`0x00`) |
| `10..15` | `Reserved` | 6 bytes | Zero padding (`0x00`) for 8-byte alignment |
| `16..23` | `PayloadLength` | `uint64` | Length of payload in bytes |
| `24..55` | `PayloadHash` | 32 bytes | BLAKE3 cryptographic hash over raw payload bytes |
| `56..` | `Payload` | N bytes | Raw uncompressed payload stream |

## Consequences

### Positive Consequences
- **Corruption Detection:** Readers verify `Magic`, `Version`, `PayloadLength`, and `PayloadHash` in a streaming fashion. Any bit flip or aborted download/write is immediately reported as a typed exception.
- **Memory Efficiency:** The fixed 56-byte header can be read and written using `stackalloc` and `Span<byte>` with zero heap allocations.
- **Forward Compatibility:** The `EnvelopeVersion` field ensures that newer schema versions are safely rejected with `UnsupportedEnvelopeVersionException`.

### Negative Consequences or Risks
- 56 bytes of overhead per stored object (negligible for audio and project files).

## Verification & Validation Evidence

The test suite in `tests/DawVcs.Infrastructure.Tests/Storage/ObjectEnvelopeCorruptionTests.cs` validates:
- Round-trip of valid payloads;
- Truncated headers (< 56 bytes);
- Invalid magic bytes;
- Unsupported envelope versions (`v2`);
- Unsupported compression modes (`CompressionMode != None`);
- Premature stream truncation (length mismatch);
- Bit corruption in the payload (hash mismatch).

