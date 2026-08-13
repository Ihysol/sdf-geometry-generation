# ADR-004: Layered authoring and runtime volume state

**Status:** Accepted
**Date:** 2026-08-12

## Context

The engine must support movable, non-destructive SDF objects as well as destructive edits such as carving and material painting. Treating either the object composition or the materialized buffer as the only source of truth would cause the other kind of change to be lost during rebuilds.

## Decision

Use a layered state model: the **Authoring Composition** remains authoritative for object identity and transforms, a replayable **Persistent Edit Layer** records destructive edits, and their materialized result is the **Effective Volume** consumed by meshers and outputs. A rebuild resamples the affected composition region and reapplies relevant persistent edits before remeshing.

Sparse voxel DAGs remain persistence, compression, and streaming representations rather than the frequently mutated interactive working buffer. They may import into or export from the Effective Volume without changing mesher or output contracts.

## Consequences

- Moving an object preserves later carving and painting by replaying intersecting persistent edits over the resampled dirty region.
- Mesher and output modes can be switched without changing authoring or edit state.
- Persistent edits require spatial indexing, stable ordering, and explicit compaction/checkpoint policies.
- A compressed DAG can be introduced later without forcing interactive editing to mutate shared DAG nodes in place.
