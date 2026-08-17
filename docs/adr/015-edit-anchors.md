# ADR-015: Explicit coordinate anchors for persistent edits

**Status:** Accepted
**Date:** 2026-08-13

## Context

A persistent edit may represent a world-space terrain modification, a processor-local modification, or a feature attached to a movable Volume Object. Inferring this ownership from the current selection or edit position makes replay ambiguous when objects or processors move.

## Decision

Every Edit Transaction declares an **Edit Anchor**: World, Processor, or Object. Object anchors reference a serialized GUID-based **Volume Object Identity** and store operations in that object's coordinate basis; Processor anchors use processor-local coordinates; World anchors remain fixed in world coordinates. Unity instance IDs, names, and hierarchy paths are not durable anchor identities.

Normal object duplication creates a new identity. Controlled duplication of a complete Volume Processor and its Volume Edit Document remaps copied object identities and every corresponding Edit Anchor as one operation.

Anchor resolution is explicit during spatial indexing, dirty-bound computation, replay, undo, and persistence serialization. Replay uses an immutable **Edit Replay Context** with the processor transform, stable object resolver, layout generation, document revision, and target region. The Edit Layer resolves an anchor once; individual operations do not search the Unity scene or infer missing transforms.

## Consequences

- Object-attached holes and features move reproducibly with their object.
- Terrain-style edits can remain fixed while authoring objects move through them.
- Stable object identities become durable persistence references rather than transient Unity instance IDs.
- Clone/import tooling must detect duplicate identities and apply an explicit remap policy.
- Deleting an anchored object requires an explicit orphan policy and must not silently reinterpret the edit in another coordinate space.
