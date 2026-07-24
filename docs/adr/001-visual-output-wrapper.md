# ADR-001: Visual Output Wrapper für Rotation/Scale

**Status:** Accepted
**Date:** 2026-07-24
**Context:** Grill-with-docs Session 1

## Problem

User wollen das finale Mesh rotieren und skalieren. Das VolumeGrid ist axis-aligned in World-space und erwartet, dass dirty bounds als axis-aligned BoundingBoxen transformiert werden können. Wenn das VolumeModel selbst rotiert/skaliert wird, entsteht ein Koordinatenraum-Konflikt:

- Grid bleibt axis-aligned (Zellen sind orthogonal)
- Dirty Bounds müssen von Object-local → World-space → Grid-Index transformiert werden
- Bei nicht-identity Parent-Transform bricht die entire dirty bounds chain

## Decision

Einen `VisualOutput` GameObject-Wrapper zwischen VolumeModel und Chunk-Meshes einführen. Das VolumeModel bleibt immer identity-transformed (logischer Container); der VisualOutput übernimmt alle visuellen Transformationen.

```
VolumeModel (identity — logischer Container)
├── Objects/          ← VolumeObjects (definieren Geometrie-Position in Model-local = World-space)
└── VisualOutput/     ← User rotiert/scaliert HIER
    └── Chunks/       ← Mesh vertices bleiben world-space;
        └── Chunk_x_y_z/   Unity transformiert sie visuell via parent chain
```

## Consequences

### Positiv
- Zero Pipeline-Änderung — `ChunkRenderer.ApplyMesh()` macht bereits `worldToLocalMatrix` und transformiert korrekt
- Rotation/Scale ist GPU-accelerated (Unity's Transform-Hierarchie)
- Kein Remesh bei visueller Transformation → nur SDF sampling + meshing bei Geometrie-Änderungen
- Culling, LOD, Bounding-Boxes funktionieren automatisch

### Negativ
- User müssen den VisualOutput transformieren statt des VolumeModels (Inspector/Scene-View Konvention)
- `Bounds` Abfragen müssen über VisualOutput gehen (nicht am Model)
- Screenshot/Gizmo Tools müssen beide Transforms berücksichtigen

## Constraints

VolumeModel bleibt **immer identity** rotated und scaled. Diese Invariante wird in der Pipeline vorausgesetzt:
- `VolumeLayout.WorldToIndex()` erwartet axis-aligned world-space input
- Dirty bounds accumulation (`_dirtyBoundsWorld`) ist axis-aligned
- Chunk-Vertex-Generierung outputet world-space coordinates

## TODO
- [ ] `VisualOutput` GameObject in `InitializePipeline()` erstellen
- [ ] Chunks unter VisualOutput parenten statt direkt unter VolumeModel
- [ ] `VolumeModel.visualOutput` serialisierbar machen (Editor-Persistenz)
- [ ] Gizmos am VisualOutput zeichnen statt am Model
- [ ] OnValidate/OnEnable: sicherstellen, dass User versehentliche Rotation am Model abgefangen werden
