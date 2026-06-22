# Flat Octree Burst Frontier Design

## Goal

Reduce Flat Octree dirty-rebuild time by evaluating built-in SDF shapes in parallel with Unity Jobs and Burst. Preserve current geometry, dirty-bounds behavior, subtree reuse, cache semantics, synchronous rebuild completion, and chunk-border correctness.

The initial implementation supports built-in shapes only. Scenes containing a `CustomAsset`, or any source that cannot provide the required snapshot, keep using the existing serial builder.

## Current Constraint

`FlatOctreeVolumeBuilder` evaluates an `IScalarFieldSource` through managed interface calls while recursively deciding topology. That interface, `UnityEngine` object access, managed arrays, and `Mathf`-based evaluation cannot run in Burst. Scheduling one job per node would also create enough scheduling and synchronization overhead to negate the small batches.

`SdfSceneSnapshot` already freezes the built-in shape parameters. The Jobs path will build on this concept with a separate blittable representation instead of changing the public scalar-field interface.

## Architecture

The Flat Octree dirty builder changes internally from depth-first recursive sampling to breadth-first frontier processing. Topology decisions remain on the main thread; only independent SDF samples are evaluated by a Burst job.

For each depth:

1. Build the active node frontier, including dirty nodes that need rebuilding and nodes not satisfied by subtree reuse.
2. Determine the corner and center samples required to classify that frontier.
3. Reuse values already present in the corner and center caches.
4. Deduplicate remaining sample positions by their existing grid-based cache keys.
5. Evaluate the missing positions as one batch.
6. Insert results into the existing caches.
7. Classify nodes and create either leaves or the next child frontier.

This loop continues until the configured maximum depth or existing termination criteria are reached. Node ordering must remain deterministic so that unchanged inputs continue to produce stable layouts and meshes.

Subtree reuse, crossing-cache reuse, and dirty-region invalidation remain main-thread operations. The final volume is returned in the same synchronous rebuild call.

## Burst-Compatible SDF Snapshot

Introduce a blittable shape record containing only primitive values and `Unity.Mathematics` types:

- shape type and composition role
- root-local-to-world and world-to-local transforms
- primitive dimensions for sphere, box, torus, and hyperboloid
- grid type and grid parameters
- flags represented as integers or bytes

Shape records are stored in read-only `NativeArray` containers for add, subtract, and intersect groups. The evaluator reproduces the current operation order exactly: minimum across adds, maximum with negated subtracts, then maximum across intersects.

The Burst evaluator mirrors the formulas in `SdfSceneSnapshot`, using `Unity.Mathematics.math`. It must not read `Transform`, `VolumeObject`, `ScriptableObject`, managed collections, or `IScalarFieldSource` inside a job.

Snapshot creation occurs after `VolumeSceneComposer.RebuildComposition()`, when transforms and shape settings are stable for that rebuild. Native allocations have explicit ownership and are disposed after the synchronous build, including exception and fallback paths.

## Batch Evaluation

Use an `IJobParallelFor` over a `NativeArray<float3>` of root-local sample positions and a matching output `NativeArray<float>`. The job evaluates one position independently against all snapshot shapes.

A configurable internal minimum batch size selects between:

- serial snapshot evaluation for small batches, avoiding scheduling overhead
- scheduled Burst evaluation followed by `Complete()` for larger batches

The first implementation keeps this threshold as a named constant and records batch counts and sample counts. Benchmark results can justify later tuning; no adaptive heuristic is required initially.

## Builder Integration

The existing serial build remains intact as the authoritative fallback. A dedicated Jobs-capable path is selected only when:

- the source is the current scene composer or exposes a compatible built-in snapshot
- the snapshot contains no unsupported shape
- Jobs/Burst data preparation succeeds

The frontier path reuses existing node classification, leaf creation, QEF/crossing logic, packed edge keys, and cache invalidation rules. Those algorithms should be extracted only as needed to let both traversal paths call the same behavior; this project does not include unrelated QEF or mesher refactoring.

Edge refinement remains serial in the first milestone. Once corner and center batching is stable and benchmarked, crossing evaluation can become a separate follow-up.

## Profiling

Extend the detailed Flat Octree profile with:

- frontier preparation
- sample deduplication
- serial batch evaluation
- Burst job scheduling and completion
- result insertion
- total batch and sample counts

Existing top-level timing names remain available so benchmark comparisons with prior runs are meaningful.

## Correctness and Fallbacks

The Jobs path must produce numerically equivalent SDF values to `SdfSceneSnapshot` within a small explicit tolerance. A non-finite result or unsupported snapshot disables the Jobs path for that rebuild and uses the existing serial path.

No partial volume is published. Since jobs complete before topology decisions continue, failures cannot expose half-built geometry. Native allocations are disposed deterministically.

Scenes with custom assets are expected to be slower but retain current behavior and visuals.

## Verification

Automated coverage should include:

- value equivalence for every built-in primitive
- value equivalence for every grid cutter type
- add, subtract, and intersect composition order
- transformed and rotated shapes
- unsupported custom-asset fallback
- deterministic node/layout output between serial and Jobs paths
- dirty rebuilds crossing chunk boundaries
- small-batch serial threshold behavior

Unity validation must include a clean compile, representative full and dirty rebuilds, visual inspection for missing faces and resolution mismatches, and repeated existing Dirty Move Benchmark runs. Performance acceptance requires a lower median Flat Octree dirty-build time without regression in visual rebuild behavior. A single faster run is not sufficient.

## Out of Scope

- Custom SDF assets in Burst
- asynchronous or multi-frame rebuild publication
- jobifying mesh generation or QEF solving
- changing edge-refinement behavior
- changing public scene composition semantics
- replacing all builders with a shared Jobs framework
