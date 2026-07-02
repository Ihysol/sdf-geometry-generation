# Clustered Chunk-Local Rendering Implementation Plan

**For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) superpowers:executing-plans implement plan task-by-task. Steps use checkbox (`- [ ]`) syntax tracking.

**Goal:** Build one chunk-local FlatOctree volume per neighboring dirty-chunk cluster instead of one per chunk.

**Architecture:** `VolumeMeshRenderer` will expose an internal static clustering helper for tests. The parallel chunk-local path will group requests into clusters, build one local volume per cluster, mesh each chunk against that shared local volume, and fall back to existing per-chunk builds if a cluster build fails.

**Tech Stack:** Unity C#, NUnit Editor tests, existing `FlatOctreeVolumeBuilder`, `OctreeChunkMesher`, and `MeshData`.

---

### Task 1: Clustering Helper Test

**Files:**
- Modify: `Assets/Tests/Editor/VolumeModelRebuildModeTests.cs`
- Modify: `Assets/Scripts/VolumeSystem/Rendering/VolumeMeshRenderer.cs`

- [ ] **Step 1: Write failing test**
  Add tests that call `VolumeMeshRenderer.BuildNeighborChunkClustersForTests` with four chunk bounds: three touching in one connected group and one separated. Assert the cluster sizes are `3` and `1`.

- [ ] **Step 2: Run test verify fails**
  Run Unity Editor tests for `VolumeModelRebuildModeTests`. Expected failure: helper method does not exist.

- [ ] **Step 3: Implement helper**
  Add an internal static helper that clusters request positions by bounds adjacency/touching.

- [ ] **Step 4: Run test verify passes**
  Run the same Editor test selection. Expected: tests pass.

### Task 2: Clustered Chunk-Local Build Path

**Files:**
- Modify: `Assets/Scripts/VolumeSystem/Rendering/VolumeMeshRenderer.cs`

- [ ] **Step 1: Refactor local build into reusable shared-volume method**
  Extract the existing local build body into a method that builds a flat volume for arbitrary bounds and returns `IFlatAdaptiveVolumeData`.

- [ ] **Step 2: Implement clustered parallel path**
  In `RebuildQueuedChunksParallel`, when chunk-local build is active, cluster requests, build one shared local volume per cluster, and generate per-request `MeshData` from the shared volume.

- [ ] **Step 3: Preserve fallback**
  If cluster build fails, run `TryBuildChunkLocalMeshData` for each request in that cluster.

- [ ] **Step 4: Verify tests**
  Run focused Editor tests, then compile/run the broader existing Editor test suite if available.

### Task 3: Measurement Notes

**Files:**
- No source files required unless tests reveal missing diagnostics.

- [ ] **Step 1: Benchmark**
  Run the same Dirty Move Benchmark as the current logs.

- [ ] **Step 2: Compare**
  Compare `rendererChunk` median, p95, and max against the latest 2026-07-01 logs.
