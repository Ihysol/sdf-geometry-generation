# ADR-015: Explicit coordinate anchors for persistent edits

**Status:** Accepted
**Date:** 2026-08-13

## Context

A persistent edit may represent a world-space terrain modification, a processor-local modification, or a feature attached to a movable Volume Object. Inferring this ownership from the current selection or edit position makes replay ambiguous when objects or processors move.

## Decision

Every Edit Transaction declares an **Edit Anchor**: World, Processor, or Object. Object anchors reference a stable Volume Object identity and store operations in that object's coordinate basis; Processor anchors use processor-local coordinates; World anchors remain fixed in world coordinates.

Anchor resolution is explicit during spatial indexing, dirty-bound computation, replay, undo, and persistence serialization.

## Consequences

- Object-attached holes and features move reproducibly with their object.
- Terrain-style edits can remain fixed while authoring objects move through them.
- Stable object identities become durable persistence references rather than transient Unity instance IDs.
- Deleting an anchored object requires an explicit orphan policy and must not silently reinterpret the edit in another coordinate space.
