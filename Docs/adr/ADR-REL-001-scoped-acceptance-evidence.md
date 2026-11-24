# ADR-REL-001: Scoped packaged-CLI acceptance evidence

Date: 24 November 2026. Status: Proposed for PR review.
Requirements: NFR-ACC-001..006; WP-SYNC-06.

Core unit/end-to-end tests and a PE/version packaging smoke test do not establish
that the shipped executable can complete collaboration, or that a real FL Studio
project opens on another machine. Evidence must state its exact scope.

Use a Python-standard-library QA runner to verify/extract the Windows ZIP and run
that executable through a disposable CLI lifecycle. Keep Linux DLL checks separate.
Record version, entrypoint/archive hashes, platform, UTC time and per-check results
as versioned JSON, with explicit false native-DAW/two-machine/SSH validation flags.
Fail the CI step on any unexpected exit, timeout or content/ref mismatch. Upload
only scoped JSON, not project/native-inspection output, credentials or full logs.

Generated PCM and a minimal opaque/legacy FLP are controlled fixtures, not claims
of native FL Studio playback. An optional filesystem hosting parent can exercise
mounted storage from one client using only its own temporary child. Host ACLs,
SSH authorization and actual multi-machine/native DAW acceptance stay manual,
with a gate matrix and evidence template. Native execution with invalid runtime
roots is useful evidence but does not replace a clean VM without an SDK/runtime.

Alternatives rejected: treating in-process CLI tests as distribution acceptance;
claiming two local folders simulate actual two-machine/NAS locking; auto-uploading
musical projects or SSH logs; introducing Python as a DAWVC runtime requirement.
Python 3.11+ is QA-only. There are no engine/wire schemas or product API changes;
JSON schema version 1 here belongs to QA evidence, not CLI operation responses.

A checksum checks integrity against the supplied checksum file, not publisher
identity. Archive extraction validates bounds and unsafe aliases/paths before
writing. Parents must be operator-controlled; cleanup is not crash recovery or
secure erasure. Nonzero exit remains authoritative even when an argument error
leaves a previous report untouched. Field gates remain pending until executed.
