# ADR-CORE-001: Shared engine composition for independent clients

- Date: 23 November 2026
- Status: Proposed (subject to PR review)

## Context

The CLI currently owns engine registration. Independently maintained clients need
the same functional behavior without referencing the CLI or a desktop framework.
The existing native-byte, portability and integrity requirements still apply,
including INV-001/002 (native bytes), INV-007 (private paths), INV-011 (core
boundaries) and INV-012 (recovery paths).

## Decision

Move dependency registration into `DawVcs.Bootstrap.AddDawVcs`. Bootstrap references
Application, Infrastructure and adapters; Domain and Application remain inward
dependencies and do not reference a presentation framework. The CLI consumes the
shared composition root. Hosts own service providers, factory callers own created
resources, and each use-case call owns its repository context.

Expose presentation-neutral progress, cancellation, typed failure classification,
repository opening and paged snapshot/history queries through Application. Keep
existing CLI entry points. No desktop app, IPC service, public package publishing
or new persisted schema is part of this decision.

## Consequences

Clients share engine fixes and registration tests without duplicating CLI setup.
Hosts must implement operation serialization, dispatching, diagnostics redaction
and orderly shutdown themselves. Cancellation remains cooperative and incomplete
compensation is surfaced with retained recovery paths. Binary/API versioning for
a future published SDK requires a separate distribution decision.

See [Engine integration](../Engine_Integration.md) for the caller contract.
