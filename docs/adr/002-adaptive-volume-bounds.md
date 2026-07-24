# ADR-002: Adaptive Volume Bounds — Auto-fit on demand

**Status:** Accepted  
**Date:** 2026-07-24  
**Supersedes:** N/A

## Context

The SDF grid (`VolumeLayout`) is currently static: `Origin`, `Resolution`, and `CellSize` are set once at pipeline initialization and never change. Objects placed outside the grid volume are silently clipped — no warning, no feedback. This causes geometry loss when:

- A user drags an object far from the processor origin
- The scene grows beyond the initial `boundsExtent`
- Multiple objects span a region larger than the grid can cover

## Decision

**Auto-fit on demand** — The VolumeProcessor tracks the union of all registered object bounds. When any object lies outside the current grid, it offers to expand/shift the grid to encompass all content. This is an explicit user action (or opt-in automatic mode), not a per-frame operation.

### Why this over alternatives

| Approach | Rejected because |
|----------|-----------------|
| Oversized static grid | Wastes memory; 512³ = 128M cells ≈ 512MB for sparse scenes |
| Dynamic re-centering (continuous) | Zucks when grid jumps; expensive full rebuild every frame during drags |
| Sparse/Octree (infinite space) | Out of scope — v10 P2 complexity; current dense-buffer pipeline doesn't support it |

Auto-fit on demand hits the sweet spot: zero overhead until needed, single expensive operation (buffer realloc + full rebuild) that only fires when the user explicitly asks or an object escapes the grid.

## Consequences

### Grid expansion algorithm

1. `VolumeObjectRegistry` exposes `GetTotalBounds()` — union of all registered object world-space bounds
2. `VolumeProcessor.CheckBoundsFit()` runs after composition changes (add/remove/transform):
   - Computes padding factor (default 1.25×) around total bounds
   - If current grid fully contains padded bounds → no-op
   - If not → triggers `ResizeGrid(newBounds)` or warns user
3. `ResizeGrid()`:
   - New `VolumeLayout` from expanded bounds (preserves `resolution` and `chunkSize`)
   - Allocates new buffer, disposes old
   - Full rebuild (`MarkAllDirty`)
   - Updates all downstream systems (Scheduler, ChunkRenderers)

### Performance impact

- **Normal operation:** Zero overhead — no per-frame bound checking during drags
- **After add/remove/transform commit:** One `GetTotalBounds()` call (O(n) over objects, n < 100)
- **Resize event:** O(R³) buffer alloc + full rebuild. Acceptable since it's rare (once per scene setup or major layout change)

### Edge cases

| Scenario | Behavior |
|----------|----------|
| Object deleted → grid shrinks? | No — grid only grows, never shrinks. Shrinking would lose data at edges. |
| Objects removed one by one until empty | Grid stays as-is; next add triggers expansion if needed |
| Resolution too small for expanded bounds (CellSize > 1m) | Warn user; suggest increasing resolution or reducing scene extent |

## Implementation plan

1. `VolumeObjectRegistry.GetTotalBounds()` — union of all object world-space AABBs
2. `VolumeProcessor.CheckBoundsFit()` — called after `RebuildPipeline()`, compares total bounds against grid
3. `VolumeProcessor.ResizeGrid(Bounds)` — allocates new layout + buffer, migrates pipeline
4. Optional: `[Toggle] autoExpand` on VolumeProcessor for silent auto-resize vs. manual confirmation
