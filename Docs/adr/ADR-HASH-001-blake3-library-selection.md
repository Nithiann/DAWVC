# ADR-HASH-001: BLAKE3 Library Selectie & Streaming API

- **Status:** Geaccepteerd
- **Datum:** 2026-09-13
- **Auteurs:** DAWVC Contributors
- **Gerelateerde Requirements:** FR-OBJ-001, FR-OBJ-003, NFR-INT-003, NFR-PERF-001
- **Werkpakket:** WP-01 (Spike B) / WP-02

---

## Context & Probleemdefinitie

DAWVC gebruikt content-addressing om blobs, projectbestanden en dependencies cryptografisch te identificeren. Conform de MVP-requirements (`FR-OBJ-001`) is BLAKE3 gekozen als de standaard hashing-algoritme. De hashing-laag moet voldoen aan de volgende criteria:
1. Volledig deterministische 32-byte (256-bit) hashes;
2. Streaming I/O ondersteuning zonder volledige audio- en projectbestanden in het RAM-geheugen te bufferen (`FR-OBJ-003`);
3. Hoge verwerkingssnelheid op grote multi-gigabyte audio-assets via SIMD (AVX2/AVX-512/NEON);
4. Geschiktheid voor zowel Windows x64 als Linux x64 CI-omgevingen onder .NET 10.

## Overwogen Opties

1. **`Blake3` (door Alexandre Mutel / xoofx):**
   - Officiële C/Rust SIMD bindings verpakt in een veilige C# P/Invoke wrapper.
   - Ondersteunt `Span<byte>`, `ReadOnlySpan<byte>` en streaming state via `Blake3.Hasher`.
   - Zeer hoge adoptie (>5,4M downloads), actieve maintenance en cross-platform native binaries meegeleverd in het NuGet-pakket.
2. **`Data.HashFunction.Blake3`:**
   - Managed port van BLAKE3.
   - Aanzienlijk lagere throughput op grote bestanden doordat geavanceerde SIMD-instructies van de C/Rust core ontbreken.
   - Lage downloadactiviteit (~14k).
3. **Eigen P/Invoke binding naar custom gecompileerde `blake3.dll`:**
   - Geeft maximale controle maar introduceert onderhoudslast voor cross-platform compilatie en native toolchains.

## Besluit

We kiezen voor **`Blake3` v3.0.2** (xoofx) als de primaire hashing-engine voor DAWVC.
In de domeinlaag wordt dit ontsloten via een generieke `IContentHasher` interface en een strongly typed, immutable `ContentHash` struct.

## Gevolgen

### Positieve gevolgen
- **Prestaties:** Door de hardware-geaccelereerde C/Rust core haalt BLAKE3 multi-gigabyte/sec throughput op SSD's.
- **Streaming:** `Blake3ContentHasher` verwerkt data in 64 KB streaming buffers, waardoor het geheugengebruik constant en ruim onder de 512 MB MVP-grens blijft.
- **Platformonafhankelijkheid:** Werkt direct in zowel Windows 11 x64 als Linux x64 (CI).

### Negatieve gevolgen of risico's
- Native dependency (`blake3.dll` / `libblake3.so`) wordt via het NuGet-pakket ingeladen. Dit is geverifieerd en werkt out-of-the-box in de .NET 10 runtime.

## Verificatie & Bewijslast

- De implementatie is getest tegen de officiële BLAKE3 testvectoren (waaronder de lege string hash: `af1349b9f5f9a1a6a0404dea36dcc9499bcb25c9adc112b7cc9a93cae41f3262`).
- De streaming tests in `Blake3HasherTests` bewijzen dat data gevoed in variërende brokjes (1 tot 256 KB) exact dezelfde hash oplevert als een in-memory buffer.
