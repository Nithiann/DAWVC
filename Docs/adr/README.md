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
| [ADR-009](ADR-009-sqlite-local-index.md) | SQLite + Dapper for Local Workspace Metadata & Staging Index | Accepted | WP-04 |
| [ADR-CORE-001](ADR-CORE-001-shared-engine-composition.md) | Shared engine composition for independent clients | Proposed | CLI/engine integration |
| [ADR-REM-003](ADR-REM-003-upstream-workflow.md) | Upstream target selection and cancellation | Proposed | WP-SYNC-02 / WP-SYNC-09 |
| [ADR-REM-004](ADR-REM-004-read-only-sync-preview.md) | Bounded fresh metadata previews | Proposed | WP-SYNC-03 / WP-SYNC-09 |
| [ADR-REM-007](ADR-REM-007-sync-workflow-integration.md) | Integrate target selection and previews with ownership/recovery | Proposed | WP-SYNC-09 |
| [ADR-REM-001](ADR-REM-001-filesystem-push.md) | Verified filesystem push | Proposed | WP-PUSH-01 |
| [ADR-REM-002](ADR-REM-002-ssh-hosting.md) | dawvc:// over SSH with transient receiver | Proposed | WP-SSH-01 |
| [ADR-OPS-001](ADR-OPS-001-repository-operation-locking.md) | Operation-wide cooperative writer ownership | Proposed | WP-SYNC-08 |
| [ADR-REC-001](ADR-REC-001-durable-workspace-recovery.md) | Durable workspace publication recovery | Proposed | WP-SYNC-07 |
| [ADR-REL-001](ADR-REL-001-scoped-acceptance-evidence.md) | Scoped packaged-CLI acceptance evidence | Proposed | WP-SYNC-06 |
| [ADR-REM-006](ADR-REM-006-explicit-snapshot-merge.md) | Explicit whole-snapshot divergence resolution | Proposed | WP-SYNC-05 |
| [ADR-REM-005](ADR-REM-005-bare-provisioning.md) | Source-derived bare hosting and clone | Proposed | WP-SYNC-04 |

| [ADR-REM-003](ADR-REM-003-upstream-workflow.md) | Shared sync targets and explicit tracking | Proposed | WP-SYNC-02 |

| [ADR-REM-004](ADR-REM-004-read-only-sync-preview.md) | Fresh read-only sync previews | Proposed | WP-SYNC-03 |

---

## Workflow for New Decisions
1. Copy `ADR-000-template.md` to `ADR-XXX-<short-title>.md`.
2. Document the context, considered options, decision rationale, and consequences.
3. Link the corresponding requirement IDs and register the decision in this index.
