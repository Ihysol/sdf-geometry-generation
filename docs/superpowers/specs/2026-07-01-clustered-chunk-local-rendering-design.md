# Clustered Chunk-Local Rendering Design

## Goal
Reduce Flat Octree Dual Contouring dirty-move renderer spikes by avoiding one local FlatOctree build per dirty chunk.

## Current Behavior
Dirty Flat Octree Dual Contouring renders skip the global volume build, then rebuild each dirty chunk independently. Each chunk-local rebuild creates a new `FlatOctreeVolumeBuilder`, builds a local volume, builds a flat layout runtime cache, and runs `DualContouringFlatOctreeMesher` for that chunk's core bounds.

Recent logs show `volumeBuild` near 0 ms while `rendererChunk` dominates with high p95/max values. This indicates redundant chunk-local volume building and mesh work in `VolumeMeshRenderer`.

## Design
When chunk-local Flat Octree Dual Contouring is active, `VolumeMeshRenderer` will group queued chunk requests into connected neighbor clusters. Each cluster unions its chunk-local build bounds, builds one local FlatOctree volume for that cluster, prepares the flat runtime cache once, then creates individual chunk mesh data by clipping the shared cluster volume to each chunk's core bounds.

If a clustered build fails, the renderer falls back to the existing per-chunk local build behavior for that cluster. Non-flat Dual Contouring and non-chunk-local paths remain unchanged.

## Boundaries
The change stays in the renderer/chunk meshing layer. It does not change `VolumeModel` dirty-bounds generation, `FlatOctreeVolumeBuilder` topology, or `DualContouringFlatOctreeMesher` output semantics.

## Testing
Add focused Editor tests for the neighbor clustering helper. Runtime validation should run the existing Editor test suite and then compare Dirty Move Benchmark logs for `rendererChunk`, especially p95/max.
