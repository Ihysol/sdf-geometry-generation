# ADR-006: Hybrid persistent edit history and chunk checkpoints

**Status:** Accepted
**Date:** 2026-08-12

## Context

Persistent carving, filling, smoothing, and painting must survive regional resampling when authoring objects move. Replaying an unbounded operation history makes rebuild cost grow indefinitely, while storing only opaque voxel deltas loses edit intent and behaves poorly when resolution or composition changes.

## Decision

Store the Persistent Edit Layer as ordered semantic **Persistent Edit Operations** plus materialized per-chunk **Edit Checkpoints**. A checkpoint is an Effective-Chunk snapshot containing Density/Material channels and the layout generation, channel schema, Authoring-Base revision, and operation generation for which it was built. Regional rebuilds may restore it only when all required revisions match, then replay newer intersecting operations in deterministic order. Otherwise the checkpoint is discarded and semantic operations are replayed from a valid earlier state.

Checkpoint creation is fail-closed: until the complete chunk snapshot and revision metadata are stored successfully, no generation marker may cause operations to be skipped.

## Consequences

- Dirty-region rebuild cost is bounded by checkpoint age and spatially intersecting operations rather than total edit history.
- Semantic operations remain available for undo, inspection, feature manipulation, and deterministic replay.
- Checkpoint invalidation must account for layout, resolution, channel format, and Authoring-Base revision changes.
- Sparse DAG persistence may encode stable checkpoints while recent operations remain as an append-only overlay.
