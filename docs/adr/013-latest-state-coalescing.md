# ADR-013: Latest-state coalescing with stable visible output

**Status:** Accepted
**Date:** 2026-08-13

## Context

Continuous transform and brush input can invalidate regions faster than sampling, meshing, and publication complete. Fully processing every intermediate state increases latency, while clearing stale output immediately creates visible gaps.

## Decision

Apply **Latest-State Coalescing** to derived Pipeline Work Items. Not-yet-started items for the same stage and overlapping region are replaced or merged under the newest Build Ticket. Running work may finish but cannot publish if its ticket is stale. Existing visible chunks remain until valid replacements are published.

Semantic Persistent Edit Operations and committed Edit Transactions are never dropped by this policy; only their derived rebuild and publication work is coalesced. Visible and camera-near chunks receive priority over background work.

## Consequences

- Interaction tracks the newest state instead of accumulating latency behind obsolete frames.
- Intermediate geometric states may never be displayed.
- Queue keys, overlap rules, and dependency propagation must be deterministic.
- Telemetry must distinguish executed, coalesced, and stale-discarded work.
