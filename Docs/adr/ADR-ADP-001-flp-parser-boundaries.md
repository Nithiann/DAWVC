# ADR-ADP-001: FLP Parser Boundaries & Fallback Behavior

- **Status:** Accepted
- **Date:** 2026-09-13
- **Authors:** DAWVC Contributors
- **Related Requirements:** FR-FLP-001, FR-FLP-002, FR-FLP-003, FR-FLP-004, FR-FLP-005, FR-FLP-008, FR-FLP-009, NFR-SEC-001, NFR-SEC-004
- **Work Package:** WP-01 (Spike A) / WP-05

---

## Context & Problem Statement

DAWVC must be able to detect, inspect, and validate native FL Studio project files (`.flp`) without risking corruption of source files and without unbounded memory allocations when handling corrupted or giant projects (`FR-FLP-001` through `FR-FLP-012`).
Key constraints:
1. **Strictly Read-Only:** The adapter must under no circumstances mutate source project files (`FR-FLP-002`, `FR-FLP-009`).
2. **Bounded Execution (Bounded Parser):** The parser must not hang, crash, or allocate unbounded memory when encountering corrupted chunks or adversarial input.
3. **Graceful Fallback:** If a project uses an unknown or future FL Studio version, DAWVC must not reject or fail parsing, but instead safely degrade to an *opaque project artifact* (`FR-FLP-008`).

## Considered Options

1. **Full Reverse-Engineered AST Parser (In-memory DOM of all events):**
   - Builds a complete object model of all patterns, notes, automations, and plugins.
   - High risk: FL Studio version updates alter internal event structures; binary format changes cause immediate parser crashes.
2. **Native FL Studio COM / Scripting Automation:**
   - Requires FL Studio to be installed and spawned in the background.
   - Violates `FR-FLP-010` (adapter must not launch FL Studio for detection) and cannot run in headless CI environments or systems without an FL Studio license.
3. **Bounded Chunk Streamer with Heuristic Metadata Extraction:**
   - Validates the fixed `FLhd` header (14 bytes: magic, format, channel count, PPQ).
   - Scans only the stream of the `FLdt` data chunk via a bounded reader (max 2 MB scan limit for headers/metadata).
   - Selectively reads reliable variable events (e.g., event 199 = version string, event 201 = title, event 203 = sample paths, event 214 = plugin names) with LEB128 length validation.
   - Controlled fallback to `ProjectDetectionStatus.Unsupported` (opaque snapshotting) for unknown versions.

## Decision Outcome

We choose Option 3: **Bounded Chunk Streamer with Opaque Fallback**.

### Binary Format Boundaries

1. **Header Chunk (`FLhd` - 14 bytes):**
   - Offset `0..3`: `FLhd` (`0x46 0x4C 0x68 0x64`).
   - Offset `4..7`: Chunk payload length (`uint32 = 6`).
   - Offset `8..9`: Format (`uint16`).
   - Offset `10..11`: Channel count (`uint16`).
   - Offset `12..13`: Time division / PPQ (`uint16`).
2. **Data Chunk (`FLdt`):**
   - Offset `0..3`: `FLdt` (`0x46 0x4C 0x64 0x74`).
   - Offset `4..7`: Total data size (`uint32`).
   - Event loop:
     - Events `0..63`: 1-byte data.
     - Events `64..127`: 2-byte data.
     - Events `128..191`: 4-byte data.
     - Events `192..255`: Variable-length quantity (LEB128) + payload bytes.
3. **Version Detection:**
   - Event `199` (`0xC7`) contains the FL Studio version string (e.g., `25.2.5.5319` for FL Studio 2026.x).
   - Versions `25.x`, `24.x`, `21.x`, and `20.x` are accepted with status `Valid`.
   - Unknown or future versions are assigned status `Unsupported` with opacity and stored byte-identically as `SingleFileArtifact` without blocking commits.

## Consequences

### Positive Consequences
- **Proven Read-Only Integrity:** Source streams are opened strictly with `FileAccess.Read` and `FileShare.Read`. Tests verify via BLAKE3 hashing that inspection modifies 0 bytes.
- **Zero External Dependencies:** No FL Studio installation, COM API, or native DLLs are required to detect projects.
- **Corruption Resilience:** Truncated streams, corrupted signatures, and out-of-bounds event lengths are safely handled as `ProjectDetectionStatus.Invalid` without unhandled exceptions.

### Negative Consequences or Risks
- Third-party plugin parameters and deeply nested preset blobs are not semantically decoded in v0.1 (treated as best-effort discovery per `DEC-MVP-005`).

## Verification & Validation Evidence

Verified in `tests/DawVcs.Adapters.FLStudio.Tests/FLStudioAdapterFixtureTests.cs`:
- Real FL Studio 2026.x fixture (`Nithiann & Mr. Unit - ID.flp`) successfully inspected (version `25.2.5.5319`, 37 channels, 96 PPQ).
- BLAKE3 pre/post hash verification proves that fixture bytes remain 100% untouched.
- Negative tests: truncated FLP (<14 bytes), invalid magic bytes (`RIFF`), and future version (`99.0.0` with proper opaque fallback).

