# Security Threat Model Review & Dependency Audit (WP-09)

**Documentversie:** 1.0  
**Datum:** 14 september 2026  
**Status:** Definitief (WP-09 / IMP-0911, IMP-0912)  
**Requirements:** `NFR-SEC-001` t/m `NFR-SEC-009`, `NFR-INT-001` t/m `NFR-INT-008`, `NFR-PERF-001` t/m `NFR-PERF-008`.

---

## 1. Doel en Reikwijdte

Dit document beschrijft de formele security- en resilience-review van DAWVC voor de v0.1 MVP release. Het documenteert de audit van externe NuGet-afhankelijkheden (`IMP-0911`) en toetst de geïmplementeerde mitigaties aan het threat model (`IMP-0912`).

---

## 2. NuGet Dependency & Licentie-audit (IMP-0911)

Alle runtime dependencies in `src/` zijn geanalyseerd op licentievoorwaarden en beveiligingsrisico's.

| Package | Versie | Uitgever | Licentie | Toepassing | Risicobeoordeling |
| :--- | :--- | :--- | :--- | :--- | :--- |
| `Blake3` | 0.4.2 | Kexik / BLAKE3 Team | **MIT / Apache-2.0** | Cryptografische content-hashing | Zeer laag. Officiële binding naar SIMD/AVX-512 BLAKE3 C-core. |
| `Microsoft.Data.Sqlite` | 10.0.0 | Microsoft | **MIT** | Staging index en caching | Zeer laag. Lokale SQLite storage in `.dawvc/index.db`. |
| `SQLitePCLRaw.bundle_e_sqlite3` | 2.1.10 | Eric Sink | **Apache-2.0** | Native SQLite engine | Zeer laag. Betrouwbare embedded SQLite binary. |
| `Dapper` | 2.1.66 | Stack Overflow | **Apache-2.0** | Micro-ORM voor SQLite index | Zeer laag. Parameterized queries, geen dynamic SQL injection risico. |
| `YamlDotNet` | 16.3.0 | Antoine Aubry | **MIT** | Repositorieconfiguratie parser | Laag. Strikt getypeerde deserialisatie zonder type-instantiatie injection. |
| `Microsoft.Extensions.FileSystemGlobbing` | 10.0.0 | Microsoft | **MIT** | Bestandsfiltering en ignore patronen | Zeer laag. Managed path matching. |
| `Microsoft.Extensions.DependencyInjection` | 10.0.0 | Microsoft | **MIT** | Inversion of Control in CLI | Zeer laag. Standaard .NET container. |
| `Spectre.Console` | 0.50.0 | Patrik Svensson | **MIT** | Terminal interface rendering | Zeer laag. Geen externe I/O. |
| `System.CommandLine` | 2.0.0-beta4.* | Microsoft | **MIT** | CLI parsing en opties | Zeer laag. Veilige argument parsing. |

**Conclusie licentie-audit:**
1. **100% Permissief:** Alle afhankelijkheden zijn gelicenseerd onder MIT of Apache-2.0.
2. **Geen Copyleft / GPL:** Er zijn geen GPL, AGPL of andere virale/beperkende licenties aanwezig. DAWVC kan zonder juridische restricties worden gedistribueerd.

---

## 3. Threat Model Checklist & Gevalideerde Mitigaties (IMP-0912)

### 3.1 Onbetrouwbare Projectdata & Parser Boundaries (`NFR-SEC-001`, `NFR-SEC-002`)
- **Dreiging:** Kwaadwillig gemanipuleerde `.flp`-bestanden (heap spray, oneindige loops, buffer overflows, gigantische declared lengths).
- **Mitigatie:**
  - `FlpBinaryReader` leest strikt begrensd: maximaal 2 MB (`MaxMetadataScanBytes`) voor header/metadata-inspectie.
  - Event payload limiet: maximaal 10 MB per event (`MaxEventPayloadBytes`).
  - LEB128 shift guard: maximaal 28 bits shift om integer overflows te voorkomen.
  - Bounds checks op stream boundaries: afgeknotte streams worden geclassificeerd als `Suspicious` of `Invalid` en veroorzaken nooit een unhandled exception.
  - Fuzz-getest via `FlpFuzzTests` met willekeurige en misvormde byte streams.

### 3.2 Path Traversal & Symlink Escape (`NFR-SEC-003`, `FR-CHK-012`)
- **Dreiging:** Een project bevat gemanipuleerde paden (`../../Windows/System32` of absolute paden `C:\Windows`) die willekeurige bestanden op het systeem overschrijven bij checkout.
- **Mitigatie:**
  - `PathSecurityGuard.ValidateAll` controleert alle paden vóór materialisatie.
  - Paden met `..`, root-aanwijzingen (`/`, `\`, `C:`) of ontbrekende genormaliseerde segmenten worden onmiddellijk afgewezen met `PathSecurityException`.
  - Materiaalisatie vindt uitsluitend plaats binnen de gecontroleerde doeldirectory.

### 3.3 Geen Plugin Executie (`NFR-SEC-004`)
- **Dreiging:** Uitvoeren van verdachte of gemanipuleerde VST/CLAP/AU binaries tijdens scanning of inspectie.
- **Mitigatie:**
  - DAWVC laadt of executeert onder geen beding plugin-binaries (`.dll`, `.vst3`, `.exe`).
  - Pluginidentificatie geschiedt zuiver declaratief via stringparsing van projectmetadata en bestandsnamen.

### 3.4 Air-Gapped Privacy & Zero Telemetry (`NFR-SEC-005`, `NFR-SEC-006`)
- **Dreiging:** Lekken van projectmetadata of gebruikersidentiteit naar externe servers.
- **Mitigatie:**
  - DAWVC bevat geen netwerkclients of telemetry-subsystemen.
  - Alle operaties (hashing, indexering, branching, diagnostiek) functioneren 100% offline.

### 3.5 Logboekprivacy & Path Redaction (`NFR-SEC-007`, `IMP-0908`)
- **Dreiging:** Lekken van persoonsgegevens of gebruikersnamen via logs en diagnostische rapporten.
- **Mitigatie:**
  - `PathRedactor` redacteert gebruikersspecifieke profielpaden (`C:\Users\Username\...` → `C:\Users\<user>\...`).
  - URL credentials (`user:password@host`) worden automatisch gesaneerd naar `://<redacted>@`.

### 3.6 Gecontroleerde Tijdelijke Bestanden (`NFR-SEC-008`)
- **Dreiging:** Tijdelijke bestanden in openbare mappen (`C:\Temp`) die vatbaar zijn voor race conditions (TOCTOU) of symlink attacks.
- **Mitigatie:**
  - Alle tijdelijke bestanden worden uitsluitend aangemaakt in de projectgebonden directory (`.dawvc/objects/.tmp/` of dezelfde directory voor atomic file rename).
  - Unieke GUID-gebaseerde bestandsnamen garanderen afwezigheid van collisions.

---

## 4. Resilience & Crash Recovery (`NFR-INT-003..005`, `IMP-0909`)

- **Atomic Writes:**
  - Objecten worden streaming geschreven naar `.tmp` en via `File.Move(..., overwrite: false)` atomair gepubliceerd.
  - Ref-updates (`HEAD`, `refs/heads/*`) gebruiken `AtomicFileWriter` (`File.Move(..., overwrite: true)`).
- **Fault Injection Validatie (`FaultInjectionAcTests`):**
  - Gesimuleerde I/O-uitval of crashes direct vóór publicatie beschadigen eerdere bereikbare state niet.
  - `dawvc fsck` bevestigt dat de objectstore en graph na een gefaalde transactie 100% integer blijven.
- **SQLite Crash Recovery:**
  - `SqliteStagingIndex` configureert SQLite met WAL-modus (`PRAGMA journal_mode=WAL; synchronous=NORMAL;`).
  - Bij databasecorruptie treedt automatische reconstructie in werking zonder verlies van gecommitte data.

---

## 5. Performance & Concurrency Baseline (`NFR-PERF-001..008`)

- **5.000 Assets Benchmark (`NFR-PERF-001`, `NFR-PERF-003`, `NFR-PERF-004`):**
  - Referentierepository met 5.000 synthetische audio-assets commit succesvol.
  - Piekgeheugengebruik blijft ruim onder 512 MB.
  - Unchanged warm status voltooit ruim binnen 2 seconden.
- **100 GB Streaming I/O (`NFR-PERF-002`):**
  - Objecten worden in 64KB buffers gestreamd zonder volledige dataset in memory te bufferen.
  - Geheugengebruik blijft stabiel en laag (< 50 MB) ongeacht bestandsgrootte.
- **Bounded Concurrency (`NFR-PERF-006`):**
  - Hashing en scanning maken gebruik van `ConcurrencyLimiter` begrensd op `Math.Clamp(ProcessorCount, 1, 8)`.
