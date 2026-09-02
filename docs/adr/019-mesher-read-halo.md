# ADR-019: Mesher-declared read halo and shared partial-rebuild region policy

**Date:** 2026-09-02
**Status:** Accepted
**Replaces:** Hardcoded ±2 sample-halo expansion in `VolumePipeline.Rebuild` partial path

## Context

A partial rebuild must resample a region large enough that every remeshed chunk's mesher sees only fresh SDF values. Two defects arose from encoding this requirement in two independent places:

1. **Corner-gap bug (fixed 2026-08-19, verified pending):** `DualContouringMesher` reads a trailing +1 halo cell and the far corner of that cell — i.e. up to 2 cells past a chunk region's max edge per axis. The old sampling expansion (+1 cell) left that outer shell stale, so boundary halo cells were misclassified and faces went missing at chunk corners. The stopgap fix hardcoded ±2 cells into `VolumePipeline.Rebuild`.
2. **Region-policy duplication:** sampling-region expansion (`ExpandToChunkRegions`) and remeshing-region expansion (`DirtyChunkSystem.MarkDirty` ±1 chunk) each recompute the same chunk expansion. Any drift between them silently corrupts partial rebuilds — which is exactly how defect 1 escaped.

The hardcoded ±2 is also mesher-specific: voxel-style meshers only read a 6-neighbour cell (halo 1). If a non-DC mesher is selected, the DC-derived halo is wasted sampling work — and if a new mesher reads further, the pipeline silently regresses to stale reads.

## Decision

1. **Mesher declares its read range.** `IVolumeMesher` gains `int ReadHaloCells { get; }` — the maximum number of cells a chunk mesher may read outside a chunk's cell region on any axis. Values are derived from each mesher's actual read pattern:
   - `DualContouringMesher` (trailing halo + far corner): **2**
   - `VoxelMesher` (6-neighbour face + face corners): **1**
   - Default for `IVolumeMesher` is **2** (safe upper bound, matches current behaviour); chunk-capable meshers that read less override it.
   - Mesher implementations that grow their read pattern must raise the value — the contract is that the declared value is *always sufficient*, never merely typical.
2. **One region policy for both stages.** `PartialRebuildPlan` (static, allocation-free) computes from a dirty region + layout:
   - `SampleRegion`: dirty region expanded to chunk boundaries, +1 face-adjacent chunk, then +`mesher.ReadHaloCells` cells, clamped to the grid.
   - `RemeshChunkRange`: the affected chunk index range +1 face-adjacent chunk, clamped to the chunk grid.
   Both `VolumePipeline` (sampling) and `DirtyChunkSystem.MarkDirty` (remeshing) consume the same plan, so the two stages can no longer drift.
3. **No-overlap partial rebuild is a no-op.** If the dirty region does not intersect the grid at all, nothing in the buffer changed. The pipeline logs a warning and returns without sampling or remeshing (previously it fell back to a full O(n³) rebuild for nothing).

## Consequences

| Aspect | Impact |
|--------|--------|
| Corner-gap fix | ✅ Preserved — DC still samples with a 2-cell halo, now derived from the mesher contract instead of a magic constant |
| Meshing Mode switching | ✅ Halo tracks the active mesher; Staged Hot-Swap (ADR-011) stays correct under mode transitions |
| Non-DC meshers | ✅ Sample with their own (smaller) halo — less wasted work; new meshers cannot silently regress by forgetting to declare a read range |
| Region drift | ✅ Structurally impossible — sampling and remeshing consume one plan |
| API surface | ⚠️ `IVolumeMesher` is a public contract — external or future meshers must declare `ReadHaloCells` (default 2 keeps them safe) |
| No-overlap behaviour change | ⚠️ Previously a (wasteful) full rebuild; now a no-op. Callers passing garbage bounds no longer trigger a full rebuild — bounds hygiene is the caller's responsibility |

## Alternatives Considered

- **B: Central named constant (keep ±2, hoist to one place)** — still mesher-blind; rejected in favour of the contract.
- **C: Status quo (inline constant + duplicated region code)** — the duplication is the root cause of the original bug; rejected.
- **No-overlap: keep full-rebuild fallback** — O(n³) sampling to "fix" a grid that provably didn't change; rejected.
- **No-overlap: auto-expand dirty region near grid edges** — changes data semantics for a case that should be an input-validation failure; rejected.
