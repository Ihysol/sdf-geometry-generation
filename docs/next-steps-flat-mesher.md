# Next Steps: Flat Mesher Migration

Goal: move from mixed tree/flat usage to a flat-first meshing pipeline for better CPU cache behavior and future Jobs/Burst/Compute.

## Current State

- `VolumeStorageMode` exists (`Tree` / `Flat`) in `VolumeModel`.
- `FlatOctreeLayout` exists and is built/cached via:
  - `OctreeVolume.GetFlatLayout()`
  - `SparseVoxelOctreeVolume.GetFlatLayout()`
- Mesher paths still mostly use tree traversal (`OctreeNode`) in runtime.
- `SparseVoxelOctree` now has its own sampler/builder/volume path and can be adapted to `OctreeVolume` for existing meshers.

## Recommended Implementation Order

1. Introduce flat mesher input interfaces
- `IFlatAdaptiveVolumeData` (octree/svo-style)
- `IFlatDenseVolumeData` (voxel-grid style)
- Keep `IVolumeData` as base.

2. Adapt volumes to provide flat views
- `OctreeVolume` and `SparseVoxelOctreeVolume` implement `IFlatAdaptiveVolumeData`.
- `VoxelGrid` implements `IFlatDenseVolumeData` (already array-based; mostly interface mapping).

3. Migrate meshers to flat input (no behavior changes first)
- Start with `DualContouringOctreeMesher` and `SurfaceNetsOctreeMesher`:
  - replace tree walk with index-based loop over `FlatOctreeLayout`.
- Then migrate `DualMarchingCubes*` and `DualMarchingTetrahedra*` to flat dense loops.

4. Switch runtime to flat-first
- In `VolumeMeshRenderer` and chunk meshers, route to flat mesher methods if `storageMode == Flat`.
- Keep temporary fallback to tree mode behind a guard for validation.

5. Validation pass
- Compare output topology and triangle counts between tree and flat for same inputs.
- Validate chunk seams and dirty chunk rebuild behavior.

6. Performance pass
- Add timing metrics:
  - rebuild setup time
  - chunk meshing total time (queue start -> queue empty)
- Confirm reduction in frame spikes and total meshing duration.

7. Parallelization (after flat is stable)
- Move flat meshing loops to Jobs + Burst.
- Keep data as POD/SoA for direct NativeArray usage.

## Cleanup Targets After Migration

- Remove duplicate tree-specific traversal code in meshers.
- Reduce adapter hops (`SparseVoxelOctreeVolume -> AsOctreeVolume`) where no longer needed.
- Keep one canonical data path per structure.

## Notes

- `VoxelGrid` already benefits from dense contiguous arrays; major wins are expected in octree/svo traversal paths.
- Do not remove tree fallback until flat output is validated for:
  - chunk boundaries
  - boolean updates
  - high `maxDepth` cases.
