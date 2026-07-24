# Context — SDF Geometry Generation

Ubiquitous language / domain glossary. No implementation details.

## Terms

### Volume Processor
The bounded context that owns a single signed distance field volume, orchestrates its sampling pipeline, and drives mesh output. Formerly "VolumeModel" — renamed to reflect that it is an orchestration boundary, not a domain model.

### Volume Object Registry
The aggregate that holds the authoritative list of volume objects for a given processor. It materialises a *snapshot* of all registered objects on demand so that downstream consumers can evaluate the composite SDF without mutable state. Formerly "VolumeSceneComposer".

### Volume Object
A single signed distance field primitive (sphere, box, torus, hyperboloid) or custom asset, positioned in local space with an operation role (add, subtract, intersect). Each object contributes its own SDF to the composite. Objects may carry optional surface grid cutters.

### SDF Snapshot
An immutable point-in-time capture of all volume objects and their transforms. Used as the read-only input for both CPU and Burst-compiled sampling paths. A new snapshot is built on every dirty event — there is no incremental diffing.

### Dirty Region
The minimal world-space AABB that covers geometry changes since the last rebuild. Encapsulates multiple object moves within a single frame to avoid undersampling. Translated to grid cell indices, then expanded to chunk boundaries + one-face neighbour padding before sampling.

### Chunk
A fixed-size sub-volume of the global SDF grid (e.g., 8×8×8 cells). The unit of work for parallel meshing: each chunk is sampled, meshed, and rendered independently. Chunks carry a monotonically increasing version number to detect stale results when re-dirtied in-flight.

### Chunk Version
A per-chunk monotonic counter incremented on every dirty event. Ensures that if a chunk is re-dirtied while already queued for meshing, the scheduler picks up the freshest version rather than silently processing stale data.

### Visual Output Wrapper
A Unity Transform node between the processor and its chunk renderers. Carries rotation and scale so that the visual mesh can be transformed without affecting the axis-aligned SDF grid below. See [ADR-001](docs/adr/001-visual-output-wrapper.md).

## Rules

- The SDF grid is always axis-aligned in world space. Rotation and scale live exclusively on the Visual Output Wrapper.
- Dirty regions accumulate via `Encapsulate` within a frame — never overwrite. Reset to zero after each rebuild cycle.
- Snapshots are immutable once created; downstream consumers must not mutate object state through them.
