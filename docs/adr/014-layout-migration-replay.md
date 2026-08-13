# ADR-014: Replay semantic edits across layout migrations

**Status:** Accepted
**Date:** 2026-08-13

## Context

Resolution, cell size, grid origin, chunk layout, and channel schema determine the meaning and indexing of materialized voxel data. Reusing checkpoints or deltas across incompatible layouts risks silent corruption, while permanently locking layout would prevent quality and bounds changes.

## Decision

Treat such changes as a **Layout Migration** with a new layout generation. Preserve the Authoring Composition and world-space semantic Persistent Edit Operations. Discard layout-bound Edit Checkpoints, GPU mirrors, geometry, active job views, and pending work, then rematerialize the Effective Volume and replay the semantic edits.

Persistence manifests declare their source layout. Import either preserves it exactly or performs an explicit, user-visible resampling operation.

## Consequences

- Layout changes are correct and reproducible but may require an expensive full rebuild.
- Semantic operations must be defined in world or normalized domain coordinates rather than raw buffer indices.
- Checkpoints are caches, not durable substitutes for semantic edit history.
- Build Tickets reject all results from the previous layout generation.
