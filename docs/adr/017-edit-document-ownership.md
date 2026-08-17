# ADR-017: Persistent edit state lives outside the pipeline

**Status:** Accepted
**Date:** 2026-08-13

## Context

The current Volume Pipeline creates and owns its Persistent Edit Layer. Replacing the pipeline during grid resize, layout migration, backend reconfiguration, or lifecycle restart therefore discards semantic edits and undo history. Pipeline instances are disposable processing infrastructure, while edits are durable domain state.

## Decision

Introduce a processor-bound **Volume Edit Document** as the durable aggregate that owns Edit Transactions, Edit History, Edit Anchors, and checkpoint references. A Volume Processor owns or references one document and injects it into each Volume Pipeline instance. The pipeline may read and materialize the document but never creates, replaces, clears, or disposes it.

Storage is provided through a versioned **Volume Edit Store** boundary. The document data model contains no `UnityEngine.Object` references. Tests use an in-memory store, the Editor uses a ScriptableObject-backed store, and later runtime savegame or sparse-DAG stores implement the same load/save/clone/migrate boundary without changing pipeline contracts.

Each Volume Processor owns a separate document by default. Explicit sharing is opt-in. Normal processor duplication deep-clones the document and remaps its Document ID, Volume Object Identities, and matching Edit Anchors as one controlled operation.

## Consequences

- Grid resize, pipeline reinitialization, Meshing Mode changes, and Output Mode changes preserve semantic edits.
- Layout migration can invalidate materialized checkpoints while retaining replayable operations in the document.
- Pipeline tests can use an in-memory document without Unity asset dependencies.
- Large edit histories do not have to live inline in scene or prefab YAML.
- Editor asset lifecycle, orphan cleanup, schema migration, and deliberate shared-document references require explicit tooling.
