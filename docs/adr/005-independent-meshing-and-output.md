# ADR-005: Meshing and output are independent axes

**Status:** Accepted
**Date:** 2026-08-12

## Context

The engine must support several surface-extraction strategies and several ways to publish or render their results. Coupling choices such as Dual Contouring directly to Unity Mesh rendering would prevent either side from evolving independently and would leak storage details into presentation.

## Decision

Treat **Meshing Mode** and **Output Mode** as independent configuration axes. A Meshing Mode reads the Effective Volume and produces geometric data; an Output Mode publishes compatible geometry or volume data without owning the underlying state. Switching either axis preserves the Authoring Composition, Persistent Edit Layer, and Effective Volume.

Compatibility is validated explicitly: mesh-producing modes can feed mesh-capable outputs, while direct-volume outputs such as raymarching may bypass surface extraction through a declared capability contract rather than an implicit enum combination.

## Consequences

- Switching Voxel, Greedy Voxel, Marching Cubes, Surface Nets, or Dual Contouring does not resample or mutate the Effective Volume.
- Switching Unity Mesh, Procedural Draw, or Debug output does not alter the selected meshing algorithm.
- Unsupported Meshing/Output combinations fail validation instead of silently selecting another mode.
- A future sparse DAG storage backend remains independent from surface extraction and presentation choices.
