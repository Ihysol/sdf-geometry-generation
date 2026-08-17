# ADR-007: Split undo ownership by state layer

**Status:** Accepted
**Date:** 2026-08-12

## Context

Authoring changes manipulate Unity objects and serialized parameters, while persistent volume edits mutate native density and material data and may contain many samples per user gesture. Forcing both forms of state through either Unity Undo or one custom stack would couple unrelated storage and lifecycle concerns.

## Decision

Use Unity Undo for the **Authoring Composition** and a dedicated **Edit History** for the **Persistent Edit Layer**. Persistent operations are grouped into **Edit Transactions** so a brush stroke or composite edit produces one logical undo step. Edit undo/redo moves a transaction cursor and rematerializes the transaction's affected region from a valid checkpoint or the Authoring Base plus active operations. Mathematical inverse operations are not required; Before/After chunk patches may accelerate rematerialization but do not define a second undo model. Runtime Edit History is optional and disabled unless explicitly configured.

Editor tooling may bridge Ctrl+Z/Redo into the appropriate owner, but the underlying histories remain separate and never duplicate the same state change.

Unity Undo is the exclusive history owner for Editor Authoring Composition changes. A custom runtime command stack may be introduced later behind an explicit runtime-only contract, but must not record the same Editor changes in parallel.

## Consequences

- Transforms, object lifecycle, and serialized shape parameters retain native Unity editor integration.
- Carve, fill, smooth, and paint undo remains correct even when an operation has no lossless mathematical inverse.
- Cross-layer gestures require an explicit coordination transaction and deterministic rollback order.
- The archived ADR-003 is historical context only; this ADR is the authoritative undo-ownership decision.
