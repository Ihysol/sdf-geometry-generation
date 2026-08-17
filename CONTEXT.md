# Context — SDF Geometry Generation

Ubiquitous language / domain glossary. No implementation details.

## Terms

### Volume Processor
The bounded context that owns a single signed distance field volume, orchestrates its sampling pipeline, and drives mesh output. Formerly "VolumeModel" — renamed to reflect that it is an orchestration boundary, not a domain model.

### Volume Object Registry
The aggregate that holds the authoritative list of volume objects for a given processor. It materialises a *snapshot* of all registered objects on demand so that downstream consumers can evaluate the composite SDF without mutable state. Formerly "VolumeSceneComposer".

### Volume Object
A single signed distance field primitive (sphere, box, torus, hyperboloid) or custom asset, positioned in local space with an operation role (add, subtract, intersect). Each object contributes its own SDF to the composite. Objects may carry optional surface grid cutters.

### Volume Object Identity
A serialized stable GUID that identifies a Volume Object across save/reload, Unity Undo, and reconstruction. Controlled duplication remaps object identities and all corresponding Edit Anchors together; instance IDs, names, and hierarchy paths are not durable identities.
_Avoid_: Unity InstanceID, object name

### SDF Snapshot
An immutable point-in-time capture of all volume objects and their transforms. Used as the read-only input for both CPU and Burst-compiled sampling paths. A new snapshot is built on every dirty event — there is no incremental diffing.

### Authoring Composition
The non-destructive definition of movable volume objects, their transforms, and boolean relationships. It is authoritative for object-level editing and can be sampled repeatedly without losing object identity.
_Avoid_: Source scene, object buffer

### Persistent Edit Layer
The ordered set of replayable volume edits applied after sampling the Authoring Composition. It preserves destructive changes such as carving or painting across rebuilds of the underlying composition.
_Avoid_: Runtime buffer, temporary edits

### Volume Edit Document
The processor-bound durable aggregate that owns Edit Transactions, Edit History, Edit Anchors, and checkpoint references independently of any Volume Pipeline instance. Pipelines consume this document but never create, replace, or dispose it.
_Avoid_: Pipeline edit layer, volume buffer

### Volume Edit Store
The persistence boundary that loads, saves, clones, and migrates versioned Volume Edit Documents without changing pipeline contracts. Editor assets, in-memory tests, runtime savegames, and future DAG storage are separate store adapters.
_Avoid_: Pipeline serialization, inline buffer state

### Persistent Edit Operation
A semantic, ordered edit such as carving, filling, smoothing, painting, or pasting that belongs to an Edit Transaction and can be replayed over a resampled region. User-visible volume edits use this lifetime by default.
_Avoid_: Scene command, transient buffer mutation

### Transient Buffer Operation
An explicitly non-persistent mutation used for previews, import materialization, internal conversion, or intentionally temporary runtime effects. It never enters Edit History and may be lost on rebuild unless deliberately baked.
_Avoid_: User edit, persistent operation

### Edit Checkpoint
A materialized Effective-Chunk snapshot that bounds persistent-edit replay cost and is valid only for its recorded layout generation, channel schema, Authoring-Base revision, and operation generation. If any required revision differs, the checkpoint is discarded and semantic operations are replayed.
_Avoid_: Generation marker, base-independent overlay

### Edit Transaction
One logical user edit that may contain many ordered Persistent Edit Operations, such as all samples in a brush stroke. It is the unit committed to Edit History; undo or redo moves the transaction cursor and rematerializes its affected region from valid state.
_Avoid_: Frame update, individual brush sample

### Edit Anchor
The explicit coordinate owner of an Edit Transaction: World, Processor, or a stable Volume Object identity. It determines whether an edit remains fixed, follows the processor, or transforms with an object.
_Avoid_: Implicit local space, current selection

### Edit Replay Context
The immutable context used to replay persistent edits, containing the target region, processor transform, stable object-anchor resolver, layout generation, and document revision. Anchors are resolved centrally through this context rather than independently by each operation.
_Avoid_: Scene lookup, null transform

### Suspended Edit
An Object-anchored Edit Transaction whose stable anchor cannot currently be resolved. It remains persisted and undoable but does not affect the Effective Volume until recovered or explicitly re-anchored, baked, or deleted.
_Avoid_: Deleted edit, world-space fallback

### Edit History
The undoable sequence of committed Edit Transactions that belongs to the Persistent Edit Layer. It is separate from Unity Undo, which owns Authoring Composition changes.
_Avoid_: Unity Undo, command stack

### Effective Volume
The materialized volume state produced by sampling the Authoring Composition and applying the Persistent Edit Layer. Meshers and outputs read this state; they do not define or own it.
_Avoid_: Mesh state, source of truth

### Meshing Mode
The independently selectable strategy that converts the Effective Volume into geometric data, such as Voxel, Greedy Voxel, Marching Cubes, Surface Nets, or Dual Contouring. It does not determine how that geometry is published or rendered.
_Avoid_: Renderer, output mode

### Output Mode
The independently selectable strategy that publishes generated geometry or volume data, such as Unity Mesh, Procedural Draw, Raymarch Volume, or Debug output. It does not determine how surface geometry is extracted.
_Avoid_: Mesher, meshing mode

### Direct Volume Output
An Output Mode that reads the Effective Volume directly without requesting surface extraction. Raymarching is a Direct Volume Output; the configured Meshing Mode remains selected but inactive while this output is used.
_Avoid_: Mesher, raymarch meshing

### Working Buffer
The mutable, chunked materialization of the Effective Volume used for interactive operations, dirty-region updates, and meshing. Its initial implementation is a Chunked Flat Volume Buffer; it is not a persistence format.
_Avoid_: Save format, sparse DAG

### Volume View
A storage-independent, read-only or writable view of volume channels and regions used at subsystem boundaries. It describes available data without exposing a concrete global array, GPU buffer, or persistence layout.
_Avoid_: NativeArray buffer, job view

### Volume Channel Schema
The versioned definition of channels available in a Working Buffer. Density and MaterialId are mandatory core channels; optional channels use typed descriptors and are requested through capabilities rather than string lookups in voxel hot paths.
_Avoid_: Per-voxel dictionary, unversioned channel list

### Volume Job View
A short-lived, backend-specific struct view that exposes contiguous native data required by Burst jobs or other hot paths. It is derived from a Working Buffer or active chunk cache and is not the public storage contract.
_Avoid_: Volume buffer interface, persistent view

### GPU Mirror
A derived GPU-resident copy of CPU-authoritative Working Buffer channels used by GPU consumers. Only dirty chunks are synchronized; the mirror never independently defines the Effective Volume.
_Avoid_: GPU source of truth, full-buffer readback

### Persistence Backend
A storage, compression, or streaming representation that imports data into and exports data from the Working Buffer. A Sparse Voxel DAG belongs to this role and is not mutated directly by normal interactive edits.
_Avoid_: Working buffer, meshing backend

### Dirty Region
The minimal world-space AABB that covers geometry changes since the last rebuild. Encapsulates multiple object moves within a single frame to avoid undersampling. Translated to grid cell indices, then expanded to chunk boundaries + one-face neighbour padding before sampling.

### Chunk
A fixed-size sub-volume of the global SDF grid (e.g., 8×8×8 cells). The unit of work for parallel meshing: each chunk is sampled, meshed, and rendered independently. Chunks carry a monotonically increasing version number to detect stale results when re-dirtied in-flight.

### Chunk Version
A per-chunk monotonic counter incremented on every dirty event. Ensures that if a chunk is re-dirtied while already queued for meshing, the scheduler picks up the freshest version rather than silently processing stale data.

### Build Ticket
An immutable set of revisions attached to a pipeline work item, covering the processor, layout, Effective Volume, chunk, Meshing Mode, and Output Mode as applicable. Publication validates only the dimensions on which that result depends.
_Avoid_: Global build number, chunk version

### Staged Hot-Swap
A non-blocking Meshing Mode or Output Mode transition in which the current representation remains visible while ticketed replacement results are prepared and published. Replacement is chunkwise by default but may be atomic when required by an output capability.
_Avoid_: Immediate rebuild, synchronous mode switch

### Pipeline Work Item
A typed, prioritized unit of regional or chunk work carrying a Build Ticket and a budget class. Sampling, edit replay, GPU synchronization, geometry building, output publication, checkpointing, and persistence export are distinct work-item stages.
_Avoid_: Mesh task, renderer command

### Pipeline Scheduler
The single backend-neutral orchestrator for budgeted Pipeline Work Items. Subsystems provide stage handlers but do not own competing scheduling loops.
_Avoid_: Meshing scheduler, renderer scheduler

### Latest-State Coalescing
A scheduling policy that replaces not-yet-started derived work with newer work for the same stage and region while retaining the old visible result until a valid replacement is published. Semantic Edit Transactions are preserved; only their derived pipeline work is coalesced.
_Avoid_: Edit squashing, process every intermediate state

### Layout Migration
A change to resolution, cell size, grid origin, chunk layout, or channel schema that creates a new layout generation. Authoring Composition and semantic Persistent Edit Operations are replayed, while layout-bound checkpoints, mirrors, geometry, and job views are discarded.
_Avoid_: Checkpoint resampling, implicit grid conversion

### Migration Seam
A tested compatibility boundary that lets an existing pipeline responsibility remain operational while its replacement is introduced incrementally. Each seam is removed only after equivalent behavior and performance are verified.
_Avoid_: Parallel pipeline, big-bang rewrite

### Visual Output Wrapper
A Unity Transform node between the processor and its chunk renderers. Carries rotation and scale so that the visual mesh can be transformed without affecting the axis-aligned SDF grid below. See [ADR-001](docs/adr/001-visual-output-wrapper.md).

## Rules

- The SDF grid is always axis-aligned in world space. Rotation and scale live exclusively on the Visual Output Wrapper.
- Dirty regions accumulate via `Encapsulate` within a frame — never overwrite. Reset to zero after each rebuild cycle.
- Snapshots are immutable once created; downstream consumers must not mutate object state through them.
