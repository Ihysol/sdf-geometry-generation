# ADR-004: Chunk Version Validation — Single Source of Truth

**Status:** Accepted  
**Date:** 2026-07-29  
**Author:** Hermes Agent  
**Tags:** pipeline, chunking, scheduler, versioning

## Context

The SDF pipeline tracks chunk freshness through two parallel version systems:

1. **`DirtyChunkSystem._versions[]`** — global monotonic counter per chunk coordinate, used by the Scheduler for stale-entry detection
2. **`ChunkManager.VolumeChunk.Version`** — per-chunk struct field, never read by the Scheduler (dead code)

This duplication caused confusion and masked several validation gaps:

| Issue | Severity | Description |
|-------|----------|-------------|
| Dead version field | Low | `ChunkManager.Version` incremented but never consumed |
| Re-dirty delay | Medium | Chunk re-dirtied during Tick() waits until next Tick() cycle |
| Hotpath console spam | Medium | `Debug.LogWarning` allocates strings per stale entry |
| Queue duplicates | Low | `_remeshQueue` accepts duplicate entries for same chunk |

## Decision

**Single source of truth:** `DirtyChunkSystem._versions[]` remains the authoritative version tracker. The Scheduler validates against it exclusively.

**Changes made:**

1. **Remove dead code:** Strip `ChunkManager.Version`, `IncrementChunkVersion()`, and `ResetAllVersions()` — they served no purpose.
2. **Immediate re-enqueue on re-dirty:** When `MarkChunk()` detects a chunk already in the queue (`DirtyState.MeshingQueued`), bump its version in-place instead of appending a duplicate entry. This eliminates duplicates AND ensures the scheduler picks up the fresh version immediately.
3. **Replace `Debug.LogWarning` with counter-based logging:** Track stale entries per Tick() cycle and log once at the end if count > 0. Zero-alloc during normal operation.
4. **Guard against duplicate queue entries:** `MarkChunk()` now checks `_states[idx] == MeshingQueued` and skips the push if already queued — version bump is sufficient since the existing entry gets validated against the new version.

## Consequences

### Positive
- Zero GC alloc during stale-entry detection (counter-based)
- Re-dirtied chunks meshed in same Tick() cycle, not deferred to next
- Cleaner API surface — `ChunkManager` no longer carries orphaned version methods
- Queue stays compact — no duplicate entries accumulate during rapid dirty operations

### Negative
- `ChunkManager` loses `IncrementChunkVersion()` — if P2 refactoring needs per-chunk metadata versions, a new field with explicit purpose must be added (not this one).
- Counter-based logging loses per-entry detail — acceptable since stale entries are normal during re-dirty operations.

## Implementation Details

### DirtyChunkSystem changes:
- `MarkChunk()` now checks `_states[idx] == MeshingQueued` before pushing to `_remeshQueue`
- If already queued: bump `_versions[idx]` + global counter, skip the push
- New property `StaleEntriesSkipped` exposed for scheduler feedback

### VolumeScheduler changes:
- Removed `Debug.LogWarning` from Tick() hotpath
- Added `staleCount` per-cycle tracking
- Log once at end of Tick() if `staleCount > 0`: `[Scheduler] Skipped {N} stale entries this frame`

### ChunkManager changes:
- Removed `IncrementChunkVersion()`, `ResetAllVersions()` from interface + implementation
- Removed `VolumeChunk.Version` field (struct stays, version removed)
