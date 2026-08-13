# ADR-009: CPU-authoritative working buffer with dirty GPU mirror

**Status:** Accepted
**Date:** 2026-08-13

## Context

CPU meshers, Burst operations, edit history, checkpoints, and persistence export require deterministic access to current volume channels. Making GPU operations authoritative would require asynchronous readback or per-chunk ownership transitions before those consumers could proceed. The current full-buffer synchronization path is too expensive for normal interactive editing.

## Decision

Keep the Working Buffer CPU-authoritative in the first stable architecture. GPU resources form a derived **GPU Mirror** and synchronize only dirty chunks or regions. Normal editing never performs a full-buffer GPU readback. Direct Volume Outputs and GPU meshers consume the mirror but do not own Effective Volume state.

GPU-authoritative or dynamically owned chunks may be evaluated later only against measured workloads and explicit synchronization semantics.

## Consequences

- Burst operations, undo deltas, checkpoints, and sparse-DAG export observe one deterministic authoritative state.
- GPU upload cost scales with changed chunks rather than total volume size.
- GPU-only edits must either also execute on CPU or produce an explicit regional result applied to CPU state before they are committed.
- The existing global `SyncCpuToGpu` and `SyncGpuToCpu` operations must not remain the normal interactive synchronization mechanism.
