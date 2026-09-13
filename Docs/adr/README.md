# Architecture Decision Records (ADR)

This registry contains all formal architecture decisions for DAWVC.

## Decision Index

| ADR ID | Title | Status | Linked Work Packages |
|---|---|---|---|
| [ADR-000](ADR-000-template.md) | Architecture Decision Record Template | Active | All |
| [ADR-ADP-001](ADR-ADP-001-flp-parser-boundaries.md) | FLP Parser boundaries & fallback behavior | Accepted | WP-01 (Spike A), WP-05 |
| [ADR-HASH-001](ADR-HASH-001-blake3-library-selection.md) | BLAKE3 library selection & streaming API | Accepted | WP-01 (Spike B), WP-02 |
| [ADR-OBJ-001](ADR-OBJ-001-canonical-object-envelope.md) | Canonical object envelope format (v1) | Accepted | WP-01 (Spike C), WP-02 |
| [ADR-IO-001](ADR-IO-001-windows-atomic-file-replace.md) | Windows atomic file replace & staging primitives | Accepted | WP-01 (Spike D), WP-02, WP-07 |

---

## Workflow for New Decisions
1. Copy `ADR-000-template.md` to `ADR-XXX-<short-title>.md`.
2. Document the context, considered options, decision rationale, and consequences.
3. Link the corresponding requirement IDs and register the decision in this index.
