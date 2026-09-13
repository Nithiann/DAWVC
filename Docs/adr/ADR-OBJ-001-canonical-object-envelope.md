# ADR-OBJ-001: Canonical Object Envelope Format (v1)

- **Status:** Geaccepteerd
- **Datum:** 2026-09-13
- **Auteurs:** DAWVC Contributors
- **Gerelateerde Requirements:** FR-OBJ-006, FR-OBJ-007, FR-OBJ-008, NFR-INT-003, NFR-INT-005
- **Werkpakket:** WP-01 (Spike C) / WP-02

---

## Context & Probleemdefinitie

DAWVC slaat alle objecten (blobs, artifact trees, commit snapshots en manifests) content-addressed op in de lokale objectopslag (`.dawvc/objects/`).
Conform requirement `FR-OBJ-006` moeten objecten een binary envelope bevatten zodat opslagcorruptie, onvolledige writes, onbekende compressiemodi of toekomstige formaatversies gecontroleerd en vroegtijdig worden gedetecteerd voordat gegevens aan de repository of workspace worden toevertrouwd.

## Overwogen Opties

1. **Bare Content (Git-achtig losse zlib streams met ascii header `blob <size>\0`):**
   - Vereist parsing van tekststrings in binaire streams.
   - Biedt geen directe fixed-offset metadata of expliciete payload-hash in de header zelf.
2. **TLV / Protocol Buffers envelope:**
   - Flexibel, maar voegt serialisatieoverhead en parsing-complexiteit toe aan high-throughput audio blob streams.
3. **Fixed 56-byte Binary Header (Canonical Envelope v1):**
   - Determinische fixed-size header met little-endian velden en 8-byte uitlijning.
   - Vaste offsets voor magic bytes, envelopeversie, objecttype, compressiemodus, payload-lengte en 32-byte BLAKE3 payload hash.

## Besluit

We kiezen voor optie 3: **Fixed 56-byte Canonical Envelope Format (v1)**.

### Header Byte-Layout (56 bytes, Little-Endian)

| Offset | Veld | Type | Beschrijving / Verwachte Waarde |
|---|---|---|---|
| `0..3` | `Magic` | 4 bytes ASCII | Altijd `DWVC` (`0x44 0x57 0x56 0x43`) |
| `4..5` | `EnvelopeVersion` | `uint16` | Schemaversie, exact `1` in MVP v0.1 |
| `6..7` | `ObjectType` | `uint16` | `1` = Blob, `2` = Tree, `3` = Snapshot, `4` = Commit |
| `8` | `CompressionMode` | `uint8` | `0` = None (ongecomprimeerd). Andere waarden worden geweigerd |
| `9` | `Flags` | `uint8` | Gereserveerd voor toekomstige vlaggen (`0x00`) |
| `10..15` | `Reserved` | 6 bytes | Opvulling met nullen (`0x00`) voor 8-byte alignment |
| `16..23` | `PayloadLength` | `uint64` | Lengte van de payload in bytes |
| `24..55` | `PayloadHash` | 32 bytes | BLAKE3 cryptografische hash over de ruwe payload bytes |
| `56..` | `Payload` | N bytes | Ruwe ongecomprimeerde payload stream |

## Gevolgen

### Positieve gevolgen
- **Detectie van corruptie:** Readers verifiëren `Magic`, `Version`, `PayloadLength` én `PayloadHash` streaming. Elke bitfout of afgebroken download/write wordt direct als een typed exception gesignaleerd.
- **Geheugenefficiëntie:** De fixed 56-byte header kan via `stackalloc` en `Span<byte>` worden gelezen en geschreven zonder geheugenallocaties.
- **Voorwaartse compatibiliteit:** Het `EnvelopeVersion`-veld garandeert dat nieuwere schemaversies gecontroleerd geweigerd worden met `UnsupportedEnvelopeVersionException`.

### Negatieve gevolgen of risico's
- 56 bytes overhead per opgeslagen object (verwaarloosbaar op audio- en projectbestanden).

## Verificatie & Bewijslast

De testsuite in `tests/DawVcs.Infrastructure.Tests/Storage/ObjectEnvelopeCorruptionTests.cs` valideert:
- Round-trip van geldige payloads;
- Truncated headers (< 56 bytes);
- Ongeldige magic bytes;
- Onbekende envelope-versies (`v2`);
- Onbekende compressiemodi (`CompressionMode != None`);
- Voortijdige stream-afbreking (lengte mismatch);
- Bit-corruptie in de payload (hash mismatch).
