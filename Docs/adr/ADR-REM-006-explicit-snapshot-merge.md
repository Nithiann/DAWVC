# ADR-REM-006: Explicit whole-snapshot merge for diverged FLP history

Date: 23 November 2026. Status: Proposed for PR review.
Requirements: FR-MRG-001..009; WP-SYNC-05.

Fast-forward-only Push/Pull cannot integrate two independent commits. The existing
commit format already supports multiple parents, but there is no safe resolution
workflow. Native FLP files remain immutable opaque source bytes; no semantic
merger is introduced for this CLI/FL Studio preview.

Add `merge <reference> --take local|incoming -m <message>`. The reference resolves
from local branches, fetched tracking refs or commit IDs. The choice is mandatory
and selects the **entire** existing snapshot: project, assets, dependencies,
metadata and completeness flag. A new commit has current local tip as first
parent, incoming tip second, and the chosen snapshot ID unchanged. Both versions
remain reachable/restorable. Log exposes both parent IDs while traversal remains
first-parent. No implicit Fetch, force replacement, partial/per-file merge,
persistent merge state, or merge-abort command.

Require genuinely diverged histories with a common ancestor. Validate both local
commit graphs (50,000 unique commits, 1,000,000 parent edges, 2 MiB per metadata
object, 64 MiB cumulative metadata payload) and both tip snapshots/blobs. Reuse
existing integrity/path checks. Earlier snapshots/blobs are not exhaustively
verified: use Fsck for the full repository audit. Missing history fails closed;
unknown schemas/types/oversized or corrupt metadata are rejected. Blob verification
streams both tip snapshots, even if one was not chosen.

Reject staged/modified/missing tracked files and selected incoming untracked
collisions. Noncolliding untracked files can remain. Reuse checkout's staged
publication journal for workspace/ref/index, with no force option. Opt-in expected
branch/tip checks occur before journal application, after the public progress
callback. This narrows observable branch-switch windows; it is not a global
workspace lock or an atomic branch compare-and-swap spanning file publication.
Do not run local mutating commands concurrently. Existing compensation failures
surface IncompleteRollbackException and retained recovery information.

Failure/cancellation can leave an unreachable immutable merge object; no automatic
GC is added. A post-publication dependency-resolution error/cancellation need not
undo the advanced branch. Hard-crash recovery remains a separate WP. Source refs
and remote-tracking refs are not rewritten by Merge. Subsequent Push/Pull use
existing fast-forward rules; the merge commit is a descendant of both old tips.
Remote advancement can still cause another non-fast-forward rejection.

Rejected alternatives: rewriting FLP bytes without a validated semantic adapter;
selecting only FLP while silently mixing incompatible assets; overwriting one
branch tip and losing ancestry; weakening Push with force. Manual editing can
follow this merge and be recorded as a normal new commit, with explicit staging
for assets taken from the other version.

Merge-specific mutation guards also reject reserved `.dawvc`/`.git`/`dawvc.yaml`
artifact roots and link/junction ancestry in affected workspace/metadata paths.
Checks repeat after the publication progress callback; trusted local writers
must still avoid concurrent path replacement. Snapshot container shape and
aggregate hash must match their entries. The new merge object must itself fit
the metadata/graph limits (including its two parent edges).
