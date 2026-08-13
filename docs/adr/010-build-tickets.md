# ADR-010: Dependency-aware build tickets for pipeline work

**Status:** Accepted
**Date:** 2026-08-13

## Context

Sampling, operations, meshing, GPU synchronization, and output publication can overlap across frames. A single global build version safely rejects stale work but also invalidates unrelated results, while chunk versions alone cannot detect layout, mesher, output, or processor lifecycle changes.

## Decision

Attach an immutable, dependency-aware **Build Ticket** to every asynchronous or budgeted work item. A ticket may carry processor generation, layout generation, Effective Volume revision, chunk version, Meshing Mode revision, and Output Mode revision. Each completion/commit stage validates the dimensions its result actually depends on.

Mode and lifecycle transitions increment their own revisions rather than synchronously draining old work.

## Consequences

- Meshing Mode changes invalidate old geometry without forcing resampling of unchanged Effective Volume data.
- Output changes can reuse compatible geometry while preventing publication into a disposed or replaced output.
- Layout changes reject all work addressing the previous grid.
- Work-item and commit APIs become more explicit, and tests must cover stale results across each relevant revision boundary.
