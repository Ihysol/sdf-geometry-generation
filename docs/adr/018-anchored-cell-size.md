# ADR-018: Anchored Cell Size — Resolution scales on grid resize

**Date:** 2026-08-18
**Status:** Accepted
**Replaces:** N/A (supersedes dynamic cell size in ResizeGrid)

## Context

`VolumeProcessor` supports auto-expand: when volume objects exceed current grid bounds, the grid is resized to include them. The original implementation kept `Resolution` fixed and recalculated `CellSize = newBounds.size / resolution`. This caused EditLayer coordinates (world-space brush carves, cell operations, dirty regions) to map incorrectly after resize — silent data corruption of persistent edits.

With multiple co-existing processors planned for off-grid geometry, preserving edit validity across layout migrations is a requirement.

## Decision

On initial layout creation, capture `cellSize = boundsExtent / resolution.x` as `_anchoredCellSize`. On subsequent resizes:

1. **CellSize remains constant** (the anchored value).
2. **Resolution scales** to fit the new bounds: `newRes = ceil(newBounds.size / _anchoredCellSize)`.
3. A hard cap (`maxResolutionCap`, default 512 per axis) prevents unbounded memory growth. When hit, a warning is logged and the user is prompted to add a second VolumeProcessor or increase the cap.

## Consequences

| Aspect | Impact |
|--------|--------|
| EditLayer coordinates | ✅ Remain valid across resize — world-to-cell mapping is unchanged |
| Memory predictability | ⚠️ Resolution grows with bounds; capped at 512³ (~134M cells) by default |
| Multiple processors | ✅ Natural fit — each processor has its own anchored cell size and independent grid |
| Backward compat | ⚠️ Existing scenes that relied on dynamic cell size will see different behavior after first resize |

## Alternatives Considered

- **B: Resolution fixed, clear EditLayer on resize** — loses user work; rejected.
- **C: No auto-expand, manual bounds only** — too restrictive for iterative authoring.
