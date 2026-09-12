# Architecture Decision Records (ADR)

Dit register bevat alle formele architectuurbesluiten voor DAWVC.

## Index van Besluiten

| ADR ID | Titel | Status | Gekoppelde Werkpakketten |
|---|---|---|---|
| [ADR-000](ADR-000-template.md) | Sjabloon voor Architecture Decision Records | Actief | Alle |
| *ADR-ADP-001* | *FLP Parser boundaries & fallback behavior* | Gepland (WP-01 Spike A) | WP-01, WP-05 |
| *ADR-HASH-001* | *BLAKE3 library selection & streaming API* | Gepland (WP-01 Spike B) | WP-01, WP-02 |
| *ADR-OBJ-001* | *Canonical object envelope format (v1)* | Gepland (WP-01 Spike C) | WP-01, WP-02 |
| *ADR-IO-001* | *Windows atomic file replace & staging primitives* | Gepland (WP-01 Spike D) | WP-01, WP-07 |

---

## Werkwijze voor nieuwe besluiten
1. Kopieer `ADR-000-template.md` naar `ADR-XXX-<korte-titel>.md`.
2. Vul de context, afwegingen en het besluit in.
3. Koppel de bijbehorende requirements en voeg het toe aan deze index.
