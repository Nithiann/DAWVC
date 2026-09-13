# Architecture Decision Records (ADR)

Dit register bevat alle formele architectuurbesluiten voor DAWVC.

## Index van Besluiten

| ADR ID | Titel | Status | Gekoppelde Werkpakketten |
|---|---|---|---|
| [ADR-000](ADR-000-template.md) | Sjabloon voor Architecture Decision Records | Actief | Alle |
| [ADR-ADP-001](ADR-ADP-001-flp-parser-boundaries.md) | FLP Parser boundaries & fallback behavior | Geaccepteerd | WP-01 (Spike A), WP-05 |
| [ADR-HASH-001](ADR-HASH-001-blake3-library-selection.md) | BLAKE3 library selection & streaming API | Geaccepteerd | WP-01 (Spike B), WP-02 |
| [ADR-OBJ-001](ADR-OBJ-001-canonical-object-envelope.md) | Canonical object envelope format (v1) | Geaccepteerd | WP-01 (Spike C), WP-02 |
| [ADR-IO-001](ADR-IO-001-windows-atomic-file-replace.md) | Windows atomic file replace & staging primitives | Geaccepteerd | WP-01 (Spike D), WP-02, WP-07 |

---

## Werkwijze voor nieuwe besluiten
1. Kopieer `ADR-000-template.md` naar `ADR-XXX-<korte-titel>.md`.
2. Vul de context, afwegingen en het besluit in.
3. Koppel de bijbehorende requirements en voeg het toe aan deze index.
