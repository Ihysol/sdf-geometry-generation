# ADR-006: Hybrid persistent edit history and chunk checkpoints

**Status:** Accepted
**Date:** 2026-08-12

## Context

Persistent carving, filling, smoothing, and painting must survive regional resampling when authoring objects move. Replaying an unbounded operation history makes rebuild cost grow indefinitely, while storing only opaque voxel deltas loses edit intent and behaves poorly when resolution or composition changes.

## Decision

Store the Persistent Edit Layer as ordered semantic **Persistent Edit Operations** plus materialized per-chunk **Edit Checkpoints**. Regional rebuilds restore the relevant checkpoint and replay only newer intersecting operations in deterministic order. Checkpoints compact accumulated edits without replacing editor history semantics.

## Consequences

- Dirty-region rebuild cost is bounded by checkpoint age and spatially intersecting operations rather than total edit history.
- Semantic operations remain available for undo, inspection, feature manipulation, and deterministic replay.
- Checkpoint invalidation must account for layout, resolution, channel format, and authoring-base version changes.
- Sparse DAG persistence may encode stable checkpoints while recent operations remain as an append-only overlay.
