# ADR-016: Incremental strangler migration of the volume pipeline

**Status:** Accepted
**Date:** 2026-08-13

## Context

The current pipeline already supports object composition, partial dirty-region rebuilds, several real meshers, chunk rendering, and budgeted remeshing. Replacing all contracts and implementations at once would combine behavioral, performance, storage, scheduling, and presentation risk and could regress established fixes.

## Decision

Adopt an incremental strangler migration using tested **Migration Seams**. Introduce Build Tickets and typed work first, adapt the existing Chunked Flat Volume Buffer behind Volume Views, separate authoring and effective state, add persistent operations, then add checkpoints, staged mode switching, regional GPU synchronization, and finally sparse-DAG persistence.

Each migration step preserves a runnable production path and must pass characterization, correctness, stale-work, and performance tests before the old seam is removed. Do not build a second competing end-to-end pipeline.

Verification reports three explicit lanes: the **Core Volume Gate** for active composition, flat-buffer, scheduler, mesher, and edit behavior; the **Legacy/Adaptive Gate** for older octree and adaptive paths; and the **Package/Environment Gate** for Unity package/import health. A lane may be quarantined with named failures, but its failures must never be hidden behind an unqualified “suite green” claim.

## Consequences

- Existing behavior remains available throughout the migration.
- Temporary adapters and duplicated contracts are accepted but tracked for removal.
- Architectural progress occurs in smaller independently reviewable changes.
- Sparse DAG and advanced GPU work cannot bypass foundational state, view, and scheduling seams.
