# ADR-008: Storage-independent volume views with specialized job views

**Status:** Accepted
**Date:** 2026-08-12

## Context

The current buffer interface exposes global NativeArrays and concrete Unity GPU buffers, which is fast for a flat grid but couples subsystem contracts to one storage layout. Replacing every hot-path access with managed interfaces would preserve abstraction at unacceptable meshing and operation cost.

## Decision

Define storage-independent **Volume Views** for subsystem boundaries and explicit backend-specific **Volume Job Views** for Burst, GPU, and other hot paths. Working Buffers provide capability-checked views; consumers request the view they require rather than downcasting to a concrete storage type.

The initial Chunked Flat Volume Buffer may expose a contiguous flat job view directly. A future streamed or DAG-backed cache may materialize job views only for active chunks without changing the high-level contracts.

The versioned **Volume Channel Schema** defines mandatory Density (`float`) and MaterialId (`int`) core channels. Optional channels use typed descriptors. Consumers declare channel capabilities up front; inner voxel loops use resolved typed views and never perform string or dictionary lookup.

## Consequences

- Storage backends can evolve without placing virtual calls inside voxel or cell loops.
- Mesher and operation capabilities become explicit and testable.
- Checkpoints, Build Tickets, and persistence manifests can reject incompatible channel schemas.
- Job views have bounded lifetimes and must carry layout/version information to reject stale results.
- The current `IVolumeBuffer` must eventually stop exposing concrete GraphicsBuffer, ComputeBuffer, and global NativeArray members as its universal contract.
