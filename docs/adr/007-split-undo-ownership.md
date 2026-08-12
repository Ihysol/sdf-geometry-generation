# ADR-007: Split undo ownership by state layer

**Status:** Accepted
**Date:** 2026-08-12

## Context

Authoring changes manipulate Unity objects and serialized parameters, while persistent volume edits mutate native density and material data and may contain many samples per user gesture. Forcing both forms of state through either Unity Undo or one custom stack would couple unrelated storage and lifecycle concerns.

## Decision

Use Unity Undo for the **Authoring Composition** and a dedicated **Edit History** for the **Persistent Edit Layer**. Persistent operations are grouped into **Edit Transactions** so a brush stroke or composite edit produces one logical undo step. Runtime Edit History is optional and disabled unless explicitly configured.

Editor tooling may bridge Ctrl+Z/Redo into the appropriate owner, but the underlying histories remain separate and never duplicate the same state change.

## Consequences

- Transforms, object lifecycle, and serialized shape parameters retain native Unity editor integration.
- Carve, fill, smooth, and paint operations can use compact inverse deltas without serializing whole buffers through Unity Undo.
- Cross-layer gestures require an explicit coordination transaction and deterministic rollback order.
- ADR-003 remains valid for Authoring Composition but is superseded for persistent buffer edits.
