# ADR-ADP-001: FLP Parser Boundaries & Fallback Behavior

- **Status:** Geaccepteerd
- **Datum:** 2026-09-13
- **Auteurs:** DAWVC Contributors
- **Gerelateerde Requirements:** FR-FLP-001, FR-FLP-002, FR-FLP-003, FR-FLP-004, FR-FLP-005, FR-FLP-008, FR-FLP-009, NFR-SEC-001, NFR-SEC-004
- **Werkpakket:** WP-01 (Spike A) / WP-05

---

## Context & Probleemdefinitie

DAWVC moet native FL Studio-projectbestanden (`.flp`) kunnen herkennen, inspecteren en valideren zonder risico op beschadiging van de bronbestanden en zonder onbeperkte geheugenallocaties bij corrupte of gigantische projecten (`FR-FLP-001` t/m `FR-FLP-012`).
Belangrijke randvoorwaarden:
1. **Strikt read-only:** De adapter mag de bronbestanden onder geen beding muteren (`FR-FLP-002`, `FR-FLP-009`).
2. **Begrensde executie (Bounded Parser):** De parser mag niet vastlopen, crashen of oneindig geheugen alloceren bij corrupte chunks of malafide input.
3. **Graceful Fallback:** Als een project een onbekende of toekomstige FL Studio-versie heeft, moet DAWVC het bestand niet weigeren of kapot parsen, maar veilig degraderen naar een *opaque project artifact* (`FR-FLP-008`).

## Overwogen Opties

1. **Volledige reverse-engineered AST-parser (in-memory DOM van alle events):**
   - Bouwt een compleet objectmodel op van alle patronen, noten, automations en plugins.
   - Hoog risico: FL Studio-versie-updates wijzigen interne event-structuren; binaire formaatwijzigingen leiden direct tot parser-crashes.
2. **Native FL Studio COM / Scripting Automation:**
   - Vereist dat FL Studio geïnstalleerd is en op de achtergrond gestart wordt.
   - Schendt `FR-FLP-010` (adapter mag FL Studio niet starten voor detectie) en werkt niet in headless CI of op machines zonder FL Studio licentie.
3. **Bounded Chunk Streamer met Heuristische Metadata-Extractie:**
   - Valideert de vaste `FLhd` header (14 bytes: magic, format, channel count, PPQ).
   - Scant uitsluitend de stream van de `FLdt` data-chunk via een bounded lezer (max. 2 MB scanlimiet voor headers/metadata).
   - Leest selectief betrouwbare variabele events (o.a. event 199 = versie string, event 201 = titel, event 203 = samples, event 214 = plugins) met LEB128 lengtevalidatie.
   - Degradeert bij onbekende versies gecontroleerd naar `ProjectDetectionStatus.Unsupported` (opaque snapshotting).

## Besluit

We kiezen voor optie 3: **Bounded Chunk Streamer met Opaque Fallback**.

### Binaire Formaatgrenzen

1. **Header Chunk (`FLhd` - 14 bytes):**
   - Offset `0..3`: `FLhd` (`0x46 0x4C 0x68 0x64`).
   - Offset `4..7`: Chunk payloadlengte (`uint32 = 6`).
   - Offset `8..9`: Format (`uint16`).
   - Offset `10..11`: Kanaalaantal (`uint16`).
   - Offset `12..13`: Time division / PPQ (`uint16`).
2. **Data Chunk (`FLdt`):**
   - Offset `0..3`: `FLdt` (`0x46 0x4C 0x64 0x74`).
   - Offset `4..7`: Totale datagrootte (`uint32`).
   - Event loop:
     - Events `0..63`: 1-byte data.
     - Events `64..127`: 2-byte data.
     - Events `128..191`: 4-byte data.
     - Events `192..255`: Variable-length quantity (LEB128) + payload bytes.
3. **Versiedetectie:**
   - Event `199` (`0xC7`) bevat de FL Studio-versie (bv. `25.2.5.5319` voor FL Studio 2026.x).
   - Versies `25.x`, `24.x`, `21.x` en `20.x` worden geaccepteerd met status `Valid`.
   - Onbekende of toekomstige versies krijgen de status `Unsupported` met opaciteit en worden als `SingleFileArtifact` byte-exact opgeslagen zonder dat commits worden geblokkeerd.

## Gevolgen

### Positieve gevolgen
- **Aantoonbare Read-Only Integriteit:** De bronstream wordt uitsluitend geopend met `FileAccess.Read` en `FileShare.Read`. Tests tonen via BLAKE3-hashing aan dat inspectie 0 bytes wijzigt.
- **Geen externe afhankelijkheden:** Geen FL Studio installatie, COM API of native DLL's vereist om een project te detecteren.
- **Veilig tegen corruptie:** Truncated streams, corrupte signatures en buitensporige eventlengtes worden gecontroleerd opgevangen als `ProjectDetectionStatus.Invalid` zonder onbehandelde exceptions.

### Negatieve gevolgen of risico's
- Derdepartij pluginparameters en diep geneste preset-blobs worden in v0.1 niet semantisch gedecodificeerd (valt onder best-effort discovery conform `DEC-MVP-005`).

## Verificatie & Bewijslast

Geverifieerd in `tests/DawVcs.Adapters.FLStudio.Tests/FLStudioAdapterFixtureTests.cs`:
- Reële FL Studio 2026.x fixture (`Nithiann & Mr. Unit - ID.flp`) met succes geïnspecteerd (versie `25.2.5.5319`, 37 kanalen, 96 PPQ);
- BLAKE3 voor/na-hashcontrole bewijst dat de fixture-bytes 100% ongewijzigd blijven;
- Negatieve tests: truncated FLP (<14 bytes), ongeldige magic bytes (`RIFF`), en toekomstige versie (`99.0.0` met correcte opaque fallback).
