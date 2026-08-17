# Volume Buffer Architecture Plan v11

**Status:** Accepted target architecture
**Date:** 2026-08-17
**Supersedes:** `volume_buffer_architecture_plan_v10.md`
**Decision authority:** `CONTEXT.md` and `docs/adr/004` through `docs/adr/017`

## 1. Purpose

This document consolidates the accepted volume architecture and maps it to the current codebase. It is an architecture map and migration order, not a replacement for the rationale in the ADRs.

The engine must support:

- movable, non-destructive SDF authoring objects;
- persistent carve, fill, smooth, paint, and paste edits;
- partial dirty-region rebuilds;
- responsive, budgeted processing;
- independently selectable meshing and output modes;
- CPU-authoritative editing with a regional GPU mirror;
- later sparse voxel DAG persistence and streaming without making a DAG the interactive mutation buffer.

## 2. Architectural invariants

1. Authoring objects and persistent edits are different state layers.
2. A disposable pipeline never owns durable edit state.
3. The Effective Volume is derived state, not the only semantic source of truth.
4. Meshing Mode and Output Mode are independent axes.
5. The interactive Working Buffer and Persistence Backend are separate concerns.
6. User-visible edits are persistent by default; transient buffer mutation is explicit.
7. Old visible output remains until valid replacement output is published.
8. No stale work may publish across incompatible processor, layout, volume, mesher, or output revisions.
9. Hot paths use specialized typed views without sacrificing storage-independent subsystem contracts.
10. Migration is incremental; no second competing end-to-end pipeline is introduced.

## 3. State model

```text
Authoring Composition
VolumeObjects + transforms + boolean roles
                │
                ▼ sample
Authoring Base
                │
                ▼ apply active Edit Transactions
Persistent Edit Layer
                │
                ▼ materialize
Effective Volume
                │
                ▼
CPU-authoritative Working Buffer
```

### 3.1 Authoring Composition

`VolumeObjectRegistry` owns the authoritative object list. Each `VolumeObject` has a serialized stable GUID. SDF snapshots are immutable sampling inputs.

### 3.2 Volume Edit Document

A processor-bound `VolumeEditDocument` owns:

- Document ID and schema version;
- document revision;
- ordered Edit Transactions;
- transaction cursor for undo/redo;
- World, Processor, and Object Edit Anchors;
- stable VolumeObject GUID references;
- Edit Checkpoint references.

The document exists independently of `VolumePipeline`. Grid resize, pipeline disposal, mesher/output changes, and layout migration must not discard it.

### 3.3 Volume Edit Store

```text
IVolumeEditStore
├── InMemoryVolumeEditStore          tests / temporary runtime volumes
├── ScriptableObjectVolumeEditStore  Unity Editor
├── BinaryVolumeEditStore            later runtime savegames
└── SparseDagVolumeStore             later persistence / streaming
```

The document model contains no `UnityEngine.Object` references. Each processor owns a separate document by default. Explicit sharing is opt-in. Controlled duplication deep-clones the document and remaps Document ID, VolumeObject GUIDs, and matching anchors.

### 3.4 Persistent and transient operations

User-visible carve, fill, smooth, paint, and paste actions create `PersistentEditTransaction`s. A transaction may contain many operations, such as all samples in one brush stroke.

`TransientBufferOperation` is reserved for previews, import materialization, conversion, and deliberately temporary runtime effects. It never enters Edit History and may be lost on rebuild unless explicitly baked.

### 3.5 Edit undo/redo

- Unity Undo exclusively owns Editor Authoring Composition changes.
- Edit History exclusively owns Persistent Edit Transactions.
- Edit undo/redo moves the transaction cursor and rematerializes affected regions.
- Mathematical inverse operations are not required.
- Before/After chunk patches may accelerate replay but do not define a second undo model.

## 4. Edit replay and anchoring

Every transaction declares an Edit Anchor:

- `World`: remains fixed in world coordinates;
- `Processor`: follows the Volume Processor;
- `Object`: follows a stable VolumeObject GUID.

Replay uses one immutable `EditReplayContext` containing:

- target world/grid region;
- processor transform;
- stable object-anchor resolver;
- layout generation;
- Authoring-Base revision;
- document revision;
- channel schema version.

The Edit Layer resolves each anchor once. Operations do not search the Unity scene or infer missing transforms. Unresolved Object Anchors suspend their transactions without deleting or reinterpreting them.

## 5. Edit Checkpoints

An Edit Checkpoint is a materialized Effective-Chunk snapshot containing at least:

- Density channel;
- MaterialId channel;
- layout generation;
- channel schema version;
- Authoring-Base revision;
- operation generation;
- chunk coordinate and bounds.

A checkpoint is restored only when all required revisions match. Otherwise it is discarded and semantic operations are replayed. Checkpoint creation is fail-closed: metadata alone must never cause operations to be skipped.

## 6. Working Buffer and views

### 6.1 Working Buffer

The initial interactive backend remains `ChunkedFlatVolumeBuffer`. It is CPU-authoritative and mutable. It is not a save format.

### 6.2 Volume Channel Schema

Mandatory core channels:

- `Density: float`
- `MaterialId: int`

Optional channels use versioned typed descriptors. No string or dictionary lookup occurs inside voxel hot loops.

### 6.3 Views

- `VolumeView`: storage-independent regional read/write boundary.
- `VolumeJobView`: short-lived backend-specific typed native view for Burst/GPU hot paths.

Consumers declare required channel and view capabilities. A future streamed cache can materialize flat job views for active chunks without changing high-level contracts.

## 7. Independent pipeline axes

### 7.1 Meshing Mode

Examples:

- Voxel;
- Greedy Voxel;
- Marching Cubes;
- Surface Nets;
- Dual Contouring;
- GPU Voxel.

Changing Meshing Mode preserves Authoring Composition, Volume Edit Document, Effective Volume, and Working Buffer.

### 7.2 Output Mode

Geometry outputs:

- Unity Mesh;
- Procedural Geometry;
- Debug Mesh.

Direct Volume Outputs:

- Raymarch Volume;
- Volume Debug.

A Direct Volume Output bypasses surface extraction. The selected Meshing Mode remains configured but inactive.

### 7.3 Capability validation

Unsupported combinations fail explicit validation. The pipeline must never silently switch to another mode. Outputs declare incremental versus atomic publication capability.

## 8. Scheduling, revisions, and publication

One backend-neutral Pipeline Scheduler owns typed work stages:

```text
SampleRegion
ApplyPersistentEdits
SyncGpuRegion
BuildGeometry
PublishOutput
BuildEditCheckpoint
ExportPersistenceRegion
```

Each Pipeline Work Item contains:

- work stage;
- region or chunk;
- priority;
- budget class;
- dependencies;
- immutable Build Ticket.

A Build Ticket may carry:

- processor generation;
- layout generation;
- Authoring-Base revision;
- Effective-Volume revision;
- document revision;
- chunk version;
- Meshing-Mode revision;
- Output-Mode revision;
- channel-schema version.

Completion validates only the revision dimensions on which the result depends.

### 8.1 Latest-State Coalescing

Not-yet-started derived work for the same stage and overlapping region is merged or replaced by newer work. Running stale work may finish but cannot publish. Semantic Edit Transactions are never discarded by coalescing.

### 8.2 Staged Hot-Swap

Existing output remains visible during Meshing-/Output-Mode changes. Replacement is chunkwise by default. Outputs that cannot mix revisions stage a complete result and publish atomically. Old resources are disposed only after no visible output or valid pending work depends on them.

## 9. CPU/GPU policy

The Working Buffer is CPU-authoritative. GPU resources are a derived regional mirror.

- Dirty chunks/regions synchronize CPU → GPU.
- Normal editing performs no full-buffer GPU readback.
- Direct Volume Outputs and GPU meshers consume the mirror but do not own Effective Volume state.
- GPU-generated edits must produce an explicit regional result committed to CPU state before the edit transaction is accepted.

## 10. Sparse voxel DAG compatibility

A sparse voxel DAG is a Persistence Backend, checkpoint codec, compression format, and streaming source. It is not directly mutated by ordinary interactive edits.

```text
Sparse DAG
    ↓ materialize active data
Chunked Working Buffer
    ↓ edit / remesh
Versioned checkpoints + recent operation overlay
    ↓ export / compact
Sparse DAG
```

Mesher and Output contracts remain independent of DAG storage. Active DAG regions may be materialized as Volume Views or Volume Job Views.

## 11. Current implementation audit

Status date: 2026-08-17.

| Area | Status | Current implementation / issue |
|---|---|---|
| Authoring Composition | Implemented | `VolumeObjectRegistry`, `VolumeObject`, immutable SDF snapshots |
| Partial sampling | Implemented | Burst regional sampling with meshing halo |
| Working Buffer | Implemented | `ChunkedFlatVolumeBuffer`, global Density/Material arrays |
| Dirty chunks | Implemented | bounds conversion, neighbor expansion, chunk versions |
| Budgeted remeshing | Partial | mesh-specific `VolumeScheduler`; full rebuild can still drain synchronously |
| Persistent Edit Layer | Partial/broken | operation list and cursor exist inside disposable pipeline |
| Carve operation/tool | Partial/broken | World carve exists; no transaction grouping; replay/full rebuild issues |
| Edit anchors | Partial | enum exists; Processor replay context is broken; Object resolver is TODO |
| Edit checkpoints | Unsafe stub | metadata only; can skip edits without restoring chunk state |
| Edit History | Partial/broken | operation cursor exists; direction detection and transaction semantics missing |
| Volume Views | Partial | Density-only mutable interface and flat-buffer adapter |
| Volume Edit Document | Missing | edit state is pipeline-owned and not durable |
| Volume Edit Store | Missing | no editor/in-memory/runtime adapter boundary |
| Stable VolumeObject GUID | Missing | anchor string exists without durable object identity/resolver |
| Meshing Mode selection | Partial | factories exist; changing inspector enum does not hot-switch initialized pipeline |
| Output Mode selection | Missing/legacy | no orthogonal runtime output axis in `VolumeProcessor` |
| Direct Volume Output | Missing | Raymarch enum exists, no target data flow |
| Build Tickets | Missing | `_buildVersion` and chunk versions are insufficient |
| Typed scheduler stages | Missing | scheduler directly meshes and commits chunks |
| Latest-state coalescing | Partial/buggy | exact chunk dedup only; duplicate control-flow defect exists |
| Staged hot-swap | Missing | no retained old/new representation transition |
| GPU mirror | Partial/broken | full-buffer sync paths remain |
| Sparse DAG persistence | Missing | older SVO code is separate from the new persistence boundary |
| Authoring Undo ownership | Broken | Unity Undo and custom CommandStack both record Authoring changes |
| Persistent operation lifetime | Broken | inspector executes direct buffer operations followed by rebuild |
| Tests for edit state | Missing | no direct tests for document, edit layer, anchors, checkpoints, or carve replay |

## 12. Verified current baseline

Fresh isolated Unity EditMode run:

```text
Total: 83
Passed: 73
Failed: 10
```

Known failing groups:

- Core Volume: SubtractSphere, Copy/Paste fixture, Add/Intersect regressions, one VoxelMesher fixture;
- Legacy/Adaptive: Dual Contouring octree cache and flat-octree profiling/reuse;
- Package/Environment: initial ShaderGraph API-updater/package-import issue;
- Unity also reported TempJob/Persistent allocation leaks at shutdown.

Quality reporting uses three explicit lanes:

1. Core Volume Gate;
2. Legacy/Adaptive Gate;
3. Package/Environment Gate.

No unqualified “suite green” claim is allowed while any lane is red.

## 13. Migration order

### Milestone 1 — Persistent Edit Correctness Gate

1. Classify and repair Core Volume baseline regressions.
2. Enforce Unity Undo ownership for Editor Authoring.
3. Introduce pipeline-independent Volume Edit Document and in-memory store.
4. Inject the document into pipeline instances.
5. Preserve edits across full rebuild, resize, dispose, and reinitialize.
6. Introduce Edit Replay Context and stable anchor resolver.
7. Add transaction-based replay undo/redo.
8. Disable metadata-only checkpoints; add revisions before real snapshots.
9. Add Density/Material Volume Views.
10. Migrate user operations to persistent transactions.

### Milestone 2 — Build Tickets and typed scheduling

Implement ADR-010, ADR-012, and ADR-013 only after Milestone 1 is green.

### Milestone 3 — Independent mode switching

Implement capability validation and staged Meshing-/Output-Mode hot-swap.

### Milestone 4 — Regional GPU mirror

Replace full-buffer synchronization with channel- and region-aware transfer.

### Milestone 5 — Edit checkpoints and compaction

Implement real revision-bound Effective-Chunk snapshots based on measured replay cost.

### Milestone 6 — Sparse DAG persistence

Add DAG import/export/streaming behind the Persistence Backend contract.

## 14. Persistent Edit Correctness Gate acceptance criteria

The gate is complete only when automated tests prove:

- a committed carve survives partial rebuild;
- a committed carve survives explicit full rebuild;
- edits survive grid resize and pipeline replacement;
- undo and redo rematerialize the correct region;
- one brush stroke is one transaction;
- World and Processor anchors resolve correctly;
- Object Anchors resolve by stable GUID or remain suspended;
- deleting/restoring an object suspends/reactivates its edits;
- missing or stale checkpoints never skip operations;
- Density and MaterialId replay through the same view contract;
- user-facing operations enter Edit History;
- transient operations are explicitly marked and may be discarded;
- Core Volume Gate is green;
- Legacy/Adaptive and Package/Environment status are reported separately;
- no production change is committed without explicit user confirmation.

## 15. ADR index

- ADR-004 — layered authoring and runtime state
- ADR-005 — independent Meshing and Output axes
- ADR-006 — hybrid edit history and revision-bound checkpoints
- ADR-007 — split undo ownership
- ADR-008 — storage-independent views and typed job views
- ADR-009 — CPU-authoritative Working Buffer and dirty GPU mirror
- ADR-010 — dependency-aware Build Tickets
- ADR-011 — staged mode hot-swap
- ADR-012 — one typed backend-neutral scheduler
- ADR-013 — latest-state coalescing
- ADR-014 — layout migration replay
- ADR-015 — explicit anchors and stable object identities
- ADR-016 — incremental strangler migration and test gates
- ADR-017 — Volume Edit Document ownership and store boundary
