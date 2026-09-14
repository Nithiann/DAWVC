# ADR-HASH-001: BLAKE3 Library Selection & Streaming API

- **Status:** Accepted
- **Date:** 2026-09-13
- **Authors:** DAWVC Contributors
- **Related Requirements:** FR-OBJ-001, FR-OBJ-003, NFR-INT-003, NFR-PERF-001
- **Work Package:** WP-01 (Spike B) / WP-02

---

## Context & Problem Statement

DAWVC utilizes content-addressing to cryptographically identify blobs, project files, and dependencies. In accordance with the MVP requirements (`FR-OBJ-001`), BLAKE3 was chosen as the standard hashing algorithm. The hashing subsystem must satisfy the following criteria:
1. Fully deterministic 32-byte (256-bit) hashes;
2. Streaming I/O support without buffering entire audio and project files in RAM (`FR-OBJ-003`);
3. High throughput on multi-gigabyte audio assets leveraging SIMD hardware acceleration (AVX2/AVX-512/NEON);
4. Seamless execution across both Windows 11 x64 and Linux x64 CI environments under .NET 10.

## Considered Options

1. **`Blake3` (by Alexandre Mutel / xoofx):**
   - Official C/Rust SIMD bindings wrapped in a safe C# P/Invoke layer.
   - Supports `Span<byte>`, `ReadOnlySpan<byte>`, and streaming state via `Blake3.Hasher`.
   - Very high adoption (>5.4M downloads), active maintenance, and cross-platform native binaries packaged inside the NuGet package.
2. **`Data.HashFunction.Blake3`:**
   - Managed C# port of BLAKE3.
   - Significantly lower throughput on large files due to absence of specialized SIMD instructions from the C/Rust core.
   - Low download volume (~14k).
3. **Custom P/Invoke Binding to In-house Compiled `blake3.dll`:**
   - Provides maximum low-level control but introduces maintenance overhead for cross-platform builds and native toolchains.

## Decision Outcome

We choose **`Blake3` v3.0.2** (xoofx) as the primary hashing engine for DAWVC.
In the domain layer, this is exposed via a generic `IContentHasher` port and a strongly typed, immutable `ContentHash` struct.

## Consequences

### Positive Consequences
- **Performance:** Hardware-accelerated C/Rust core enables BLAKE3 to achieve multi-gigabyte/sec throughput on modern SSDs.
- **Streaming:** `Blake3ContentHasher` processes data in 64 KB streaming buffers, maintaining constant memory utilization well below the 512 MB MVP ceiling.
- **Cross-Platform:** Runs out-of-the-box on Windows 11 x64 and Linux x64 (GitHub Actions CI).

### Negative Consequences or Risks
- Native dependencies (`blake3.dll` / `libblake3.so`) are loaded via the NuGet package. This has been validated and runs out-of-the-box in the .NET 10 runtime.

## Verification & Validation Evidence

- Tested against official BLAKE3 test vectors (including empty string hash: `af1349b9f5f9a1a6a0404dea36dcc9499bcb25c9adc112b7cc9a93cae41f3262`).
- Streaming tests in `Blake3HasherTests` verify that data chunked in varying block sizes (1 to 256 KB) produces the exact same hash as an in-memory buffer.

