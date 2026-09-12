# DAWVC — DAW Version Control & Dependency Management

[![CI](https://github.com/dawvc/dawvc/actions/workflows/ci.yml/badge.svg)](https://github.com/dawvc/dawvc/actions/workflows/ci.yml)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)
[![.NET](https://img.shields.io/badge/.NET-10.0-purple.svg)](https://dotnet.microsoft.com/)
[![Platform](https://img.shields.io/badge/Platform-Windows%2011%20x64-blue.svg)]()

> **DAW-agnostic version control, asset deduplication, and dependency portability for music production.**

---

## 1. Wat is DAWVC?

**DAWVC** (Digital Audio Workstation Version Control) is een gespecialiseerd version-control- en dependency-managementsysteem voor muziekproducers en audio-engineers.

Traditionele versiebeheersystemen zoals Git slaan bestanden op, maar begrijpen audio- en DAW-projecten niet:
- Ze weten niet welke samples, recordings, wavetables of third-party plugins een project nodig heeft.
- Ze zijn afhankelijk van lokale, absolute bestandspaden die op een andere machine falen (`D:\Samples\Kick.wav` vs `C:\Audio\Kick.wav`).
- Ze bieden geen inzicht in pluginversies of omgevingsreproduceerbaarheid.

DAWVC lost dit op door projectbestanden, audio-assets en plugin-requirements semantisch te koppelen aan **content-identiteit** in plaats van bestandspaden.

```
┌─────────────────────────────────────────────────────────────┐
│                       Kernprincipe:                         │
│       "Een dependency wordt geïdentificeerd door            │
│        zijn inhoud of logische identiteit,                  │
│        NOOIT door zijn lokale bestandspad."                 │
└─────────────────────────────────────────────────────────────┘
```

---

## 2. Kernconcepten & Filosofie

1. **DAW-Agnostische Core**:
   De kernlogica (`Domain`, `Application`, `Infrastructure`) bevat geen kennis van specifieke DAW-formaten. Alle DAW-specifieke functionaliteit (zoals FL Studio `.flp` inspectie) leeft in geïsoleerde, vervangbare adapters.
2. **Native Bytes zijn Heilig**:
   Native projectbestanden worden nooit in-place gemuteerd of onvolledig weggeschreven. De bronbytes blijven altijd de primaire waarheid. Inspectie-metadata is afgeleid en herbouwbaar.
3. **Content-Addressed Storage (BLAKE3)**:
   Alle project-assets en blobs worden geïdentificeerd via snelle, cryptografisch sterke BLAKE3-hashes. Identieke samples over verschillende projecten of branches worden automatisch gededupliceerd.
4. **Strikte Scheiding van Shared vs. Local State**:
   - *Shared (geversioneerd)*: Commits, snapshots, dependency graphs, project-artifacts, assets, plugin-requirements en portability policies.
   - *Local (niet-geversioneerd)*: Absolute bestandspaden, lokale plugin-installaties, lokale samplebibliotheek-locaties, caches en credentials.
5. **Safe Checkout & Integrity-First**:
   Een checkout overschrijft nooit stilzwijgend lokale wijzigingen. Bij een geforceerde checkout (`--force`) wordt eerst een deterministische recovery-kopie gemaakt. Checkout naar een kandidaat-workspace gebeurt altijd staged en atomic.

---

## 3. MVP v0.1 Scope & Feature Set

De eerste release (**MVP v0.1**) focust op een solide, lokale workflow voor **FL Studio** op **Windows 11 x64**:

- [x] **Repository Management**: Lokale repositories per project (`dawvc init`, `dawvc.yaml`).
- [x] **Hybride Staging**: Automatische opname van het primaire projectbestand en gevolgde assets; expliciete staging voor nieuwe dependencies (`dawvc add`).
- [x] **Immutable History**: Commit, snapshot creation, logweergave (`dawvc commit`, `dawvc log`).
- [x] **Branching**: Aanmaken, schakelen en beheren van lokale branches (`dawvc branch`, `dawvc switch`).
- [x] **Read-Only FL Studio Adapter**: Inspectie en validatie van `.flp` projecten (FL Studio 2026.x baseline) met veilige *opaque fallback* voor onbekende versies.
- [x] **Safe Checkout**: Staged validatie, herstel naar actieve workspace of alternatieve map (`dawvc checkout --restore-to <dir>`).
- [x] **Environment Diagnostics**: Project- en omgevingsvalidatie (`dawvc doctor`).
- [x] **Repository Integrity**: Byte- en envelope-validatie (`dawvc fsck`).

---

## 4. CLI Overzicht

| Commando | Beschrijving |
|---|---|
| `dawvc init` | Initialiseert een nieuwe DAWVC-repository in de huidige map. |
| `dawvc scan` | Inspecteert het project op gebruikte samples, recordings en plugins. |
| `dawvc status` | Toont de huidige toestand van de workspace, dirty files en uncommitted changes. |
| `dawvc add <path>` | Voegt een extern bestand of sample toe aan de repository dependencies. |
| `dawvc commit -m <msg>` | Legt een nieuw immutable snapshot vast in de geschiedenis. |
| `dawvc log` | Toont de commit-historie met snapshot-metadata en auteur. |
| `dawvc branch [name]` | Toont bestaande branches of maakt een nieuwe branch aan. |
| `dawvc switch <branch>` | Schakelt over naar een andere branch. |
| `dawvc checkout <commit>` | Herstelt een projecttoestand (optioneel met `--restore-to` of `--force`). |
| `dawvc doctor` | Controleert of alle benodigde samples, plugins en assets lokaal aanwezig zijn. |
| `dawvc fsck` | Valideert de interne integriteit van de object store en repository refs. |

---

## 5. Architectuur & Solutionstructuur

De solution volgt Clean Architecture principes met strikt afgebakende afhankelijkheden:

```
                  ┌──────────────────────┐
                  │      DawVcs.Cli      │ (Presentation / Host)
                  └──────────┬───────────┘
                             │
                             ▼
                  ┌──────────────────────┐
                  │  DawVcs.Application  │ (Use Cases, Ports, Orchestration)
                  └──────┬────────┬──────┘
                         │        │
           ┌─────────────┘        └─────────────┐
           ▼                                    ▼
┌──────────────────────┐             ┌─────────────────────────────┐
│    DawVcs.Domain     │             │ DawVcs.Adapters.Abstractions│
└──────────────────────┘             └──────────────┬──────────────┘
           ▲                                        ▲
           │                                        │
┌──────────┴───────────┐             ┌──────────────┴──────────────┐
│ DawVcs.Infrastructure│             │   DawVcs.Adapters.FLStudio  │
└──────────────────────┘             └─────────────────────────────┘
```

### Mappenstructuur

```text
src/
  DawVcs.Domain/                  # Pure entiteiten, value objects en domain invariants (0 externe deps)
  DawVcs.Application/             # Use cases, interfaces en command orchestration
  DawVcs.Infrastructure/          # Object store, BLAKE3 hasher, filesystem IO en serialization
  DawVcs.Adapters.Abstractions/   # Contracten voor DAW-inspectie en capability checks
  DawVcs.Adapters.FLStudio/       # Read-only FL Studio (.flp) parser en dependency extractor
  DawVcs.Cli/                     # CLI entry point (System.CommandLine + Spectre.Console)

tests/
  DawVcs.Domain.Tests/            # Unit tests & Architectural rule enforcement tests
  DawVcs.Application.Tests/       # Use case tests met testdoubles
  DawVcs.Infrastructure.Tests/   # Object store, hashing en atomic IO tests
  DawVcs.Adapters.FLStudio.Tests/ # FLP fixture parsing en regressietests
  DawVcs.IntegrationTests/        # Eenduidige integratietests tussen componenten
  DawVcs.EndToEndTests/           # CLI acceptance tests (AC-001 t/m AC-014)

docs/
  Technical Design.md            # Normatieve architectuur v1.1
  MVP Requirements.md            # Requirements baseline v1.0
  Implementation plan.md         # Fasedoelen en werkpakketten (WP-00 t/m WP-10)
  adr/                           # Architecture Decision Records
```

---

## 6. Ontwikkeling & Bouwen

### Vereisten
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) (versie `10.0.301` of nieuwer)
- Windows 11 x64 (aanbevolen voor FL Studio adapter-integratie) of Linux/macOS voor core domain ontwikkeling.
- Git

### Builden
```powershell
# Restore en build alle projecten in Release modus
dotnet build -c Release
```

> **Opmerking:** De build staat geconfigureerd met `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>` en strikte C# code style analyzers.

### Testen
```powershell
# Voer alle unit-, architecture- en integratietests uit
dotnet test -c Release --logger "console;verbosity=normal"
```

### Code Formatting
```powershell
# Valideer codeformatting conform .editorconfig
dotnet format --verify-no-changes
```

---

## 7. Documentatie & Specificaties

- [Technical Design](Docs/Technical%20Design.md) — Gedetailleerd technisch ontwerp, objectmodellen, envelope-specificaties en foutafhandeling.
- [MVP Requirements Specification](Docs/MVP%20Requirements.md) — Normatieve functionele en niet-functionele eisen inclusief acceptatiecriteria.
- [Implementation Plan](Docs/Implementation%20plan.md) — Werkpakketten, risicospikes en Definition of Done.
- [Architecture Decision Records](docs/adr/README.md) — Gedocumenteerde technische besluiten.

---

## 8. Licentie

Dit project is gepubliceerd onder de **[MIT License](LICENSE)**.

Copyright (c) 2026 DAWVC Contributors.
