# ADR-011: Staged hot-swap for meshing and output modes

**Status:** Accepted
**Date:** 2026-08-13

## Context

Changing Meshing Mode or Output Mode can require rebuilding geometry or presentation resources for many chunks. Clearing current output first causes visible gaps, while synchronously rebuilding everything blocks editor and runtime interaction.

## Decision

Use a **Staged Hot-Swap** for mode transitions. Existing output remains visible while replacement work is generated under a new Build Ticket revision. Mesh-capable outputs replace chunks incrementally by default. Outputs that cannot safely mix revisions declare an atomic-swap capability and publish only after their complete staging set is ready.

Old resources are retired only after they are no longer visible and no valid pending work depends on them.

## Consequences

- Mode changes remain responsive and avoid blank frames.
- Mixed chunk revisions may be briefly visible for outputs that permit incremental replacement.
- Output capability contracts must declare incremental versus atomic publication.
- Resource lifetime management must account for pending tickets, staging data, and delayed disposal.
