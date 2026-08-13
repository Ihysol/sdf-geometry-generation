# ADR-012: One typed backend-neutral pipeline scheduler

**Status:** Accepted
**Date:** 2026-08-13

## Context

Interactive updates involve sampling, persistent-edit replay, GPU synchronization, geometry construction, Unity publication, checkpointing, and persistence export. A meshing-only scheduler leaves other expensive stages synchronous, while independent subsystem schedulers compete for budgets and make dependency/version handling inconsistent.

## Decision

Use one backend-neutral **Pipeline Scheduler** for typed **Pipeline Work Items**. Work stages include regional sampling, edit replay, GPU mirror synchronization, geometry building, output publication, edit checkpointing, and persistence export. Each item carries a region or chunk, priority, budget class, dependencies, and Build Ticket.

Subsystems register stage handlers. They do not create independent scheduling loops. Main-thread-only stages such as Unity Mesh publication remain explicitly identified and separately budgeted.

## Consequences

- One place controls frame budgets, priorities, cancellation, coalescing, and telemetry.
- Mesher, Output Mode, and Persistence Backend remain replaceable handlers rather than scheduler owners.
- Work-item dependencies and result lifetimes require explicit modeling.
- The current mesh-specific `VolumeScheduler` must evolve rather than be duplicated.
