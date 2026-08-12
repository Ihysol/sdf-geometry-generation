# ADR-003: Undo/Redo via Unity's Built-in Undo API

**Status:** Superseded in part by ADR-007
**Date:** 2026-07-24

## Context

Users need undo/redo for VolumeProcessor edits. Three operation classes exist:

1. **Transform changes** — moving/resizing `VolumeObject` children in scene
2. **Composition changes** — Add/Remove/Clear objects via Inspector
3. **Parameter changes** — shape parameters (radius, extents, etc.) in Inspector

The Pipeline rebuilds the SDF grid + mesh on every change. Undo must revert state
and trigger a rebuild — without storing full grid snapshots per step.

## Decision

Use **Unity's built-in `Undo.RecordObject()`** for composition/parameter changes.
For transform changes, use **`Undo.RegisterCompleteObjectUndo()`** on the moved
`VolumeObject` + track Dirty Bounds for automatic partial rebuild.

### Why not custom Command Pattern?

| Factor | Unity Undo API | Custom Command Stack |
|--------|---------------|---------------------|
| Integration | Native Ctrl+Z/Y, Edit menu | Manual keybinding needed |
| Serialization | `Undo.RecordObject` captures serialized fields automatically | Manual state capture per command type |
| Memory | Delta-based (Unity serializes changes) | Must design delta format ourselves |
| Pipeline coupling | Low — just mark dirty after undo | High — stack owns rebuild triggers |
| GC pressure | Minimal (Unity handles pooling) | Depends on implementation |

Unity's API already covers our cases:
- `Undo.RecordObject(go, "label")` — records serialized field deltas
- `Undo.RegisterCompleteObjectUndo(obj, "label")` — for non-serialized transforms
- Both integrate with Edit → Undo/Redo menu and Ctrl+Z/Y natively

### What gets recorded?

| Action | Undo Target | Rebuild Trigger |
|--------|-------------|----------------|
| Add object | `VolumeProcessor` (triggers `AddSelectedObject()`) | `RebuildModel()` called in same tick |
| Remove object | `VolumeProcessor` (triggers `RemoveLastObject()`) | `RebuildModel()` called in same tick |
| Clear all | `VolumeProcessor` (triggers `ClearObjects()`) | `RebuildModel()` called in same tick |
| Move VolumeObject | `VolumeObject` via `Undo.RegisterCompleteObjectUndo()` | `MarkDirtyBounds()` + scheduler |
| Change shape params | `VolumeObject` via `serializedObject` change | `RebuildModel()` in editor callback |

### Implementation approach

1. **Composition** — Already uses `Undo.RecordObject()` in `VolumeProcessorEditor`. Keep as-is.
2. **Transform** — Hook into `VolumeObject.OnValidate()` or a custom Editor that calls `Undo.RegisterCompleteObjectUndo()` before applying transform changes, then `MarkDirtyBounds()`.
3. **Parameter** — `VolumeObjectEditor` already rebuilds on serialized change. Add `Undo.RecordObject(obj, "Change <param>")`.

### What we DON'T do

- No custom command stack / IRevoke pattern
- No grid snapshots per step (64MB+ per snapshot at 128³)
- Pipeline-internal operations were originally treated as destructive without Undo. ADR-007 supersedes this for Persistent Edit Operations while retaining Unity Undo for Authoring Composition changes.

## Consequences

**Positive:**
- Zero custom infrastructure — Unity handles the stack, serialization, GC
- Ctrl+Z/Y works out of the box
- Pipeline always rebuilds after undo (via editor callback or dirty bounds)

**Negative:**
- Undo only works in Editor (no PlayMode undo needed for this project)
- Transform undo doesn't cover inspector-driven moves — only scene view drag
- No "undo to specific step" — only sequential LIFO
