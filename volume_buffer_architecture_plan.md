# Architektur- und Implementationsplan: Modulare Volume-Pipeline für Unity

## Ziel

Ziel ist eine modulare Volume-Engine in Unity, bei der Datenquelle, Speicherform, Rechenbackend, Mesher und Ausgabe unabhängig voneinander austauschbar sind.

Die Pipeline soll folgende Kombinationen ermöglichen:

```text
Voxel + CPU/Burst + Unity Mesh
SDF + CPU/Burst + Dual Contouring + Unity Mesh
Voxel + GPU Compute Shader + Direct GPU Render
SDF + GPU Compute Shader + Marching Cubes + Unity Mesh
SDF + GPU Compute Shader + Dual Contouring + Procedural Draw
```

Der wichtigste Architekturpunkt ist ein gemeinsamer `VolumeBuffer`, auf dem sowohl CPU-Skripte als auch GPU-Compute-Shader Operationen ausführen können.

---

## Grundidee

Die Pipeline wird nicht als ein einzelner Renderer gedacht, sondern als Kette austauschbarer Stages:

```text
Volume Source
→ Volume Storage / Volume Buffer
→ Operations
→ Mesher Backend
→ Output Target
→ Unity Rendering / Shader
```

Die Volume-Pipeline endet bei Meshdaten oder GPU-Buffern. Materialien, Vertex Shader, Fragment Shader und Post-Processing gehören danach zur normalen Unity-Renderpipeline.

---

## Inspector-Konfiguration

Das zentrale `VolumeModel` bleibt ein `MonoBehaviour` und stellt die wichtigsten Optionen im Inspector bereit.

```csharp
public enum DataStructureType
{
    Sdf,
    VoxelGrid,
    SparseVoxelOctree
}

public enum StorageMode
{
    Tree,
    Flat
}

public enum ComputeBackend
{
    CPU,
    GPU
}

public enum MesherType
{
    Voxel,
    GreedyVoxel,
    MarchingCubes,
    SurfaceNets,
    DualContouring
}

public enum OutputMode
{
    UnityMesh,
    ProceduralDraw,
    RaymarchVolume,
    Debug
}
```

Beispielkonfiguration:

```text
DataStructure: SparseVoxelOctree
StorageMode: Flat
ComputeBackend: GPU
Mesher: MarchingCubes
OutputMode: ProceduralDraw
```

---

## Zielarchitektur

```text
VolumeModel
│
├─ Config
│   ├─ DataStructure
│   ├─ StorageMode
│   ├─ ComputeBackend
│   ├─ Mesher
│   └─ OutputMode
│
├─ VolumeSource
│   ├─ SdfSource
│   ├─ VoxelSource
│   ├─ FeatureGraphSource
│   └─ NoiseSource
│
├─ VolumeBuffer
│   ├─ CPU View: NativeArray
│   ├─ GPU View: GraphicsBuffer
│   ├─ Density Buffer
│   ├─ Material Buffer
│   ├─ Node Buffer
│   └─ Dirty Regions
│
├─ OperationSystem
│   ├─ Add / Subtract / Paint / Smooth
│   ├─ CPU Operations
│   └─ GPU Compute Operations
│
├─ StorageSystem
│   ├─ FlatGridStorage
│   ├─ FlatOctreeStorage
│   └─ TreeOctreeStorage
│
├─ MeshingSystem
│   ├─ CPU Burst Meshers
│   │   ├─ Voxel
│   │   ├─ MarchingCubes
│   │   ├─ SurfaceNets
│   │   └─ DualContouring
│   │
│   └─ GPU Compute Meshers
│       ├─ Voxel
│       ├─ MarchingCubes
│       ├─ SurfaceNets
│       └─ DualContouring
│
└─ OutputSystem
    ├─ UnityMeshOutput
    ├─ ProceduralDrawOutput
    ├─ MeshColliderOutput
    └─ DebugOutput
```

---

## Zentrale Regel

Die Stufen dürfen möglichst wenig voneinander wissen.

```text
Source weiß nichts vom Mesher.
Storage weiß nichts von Unity-Materialien.
Mesher weiß nichts von GameObjects.
Output weiß nichts von SDF-Operationen.
Shader verändern nur die Darstellung, nicht automatisch das Volume.
```

Dadurch bleibt jede Stufe austauschbar.

---

## Volume Source

Eine `VolumeSource` beschreibt die ursprüngliche Form oder Welt.

Beispiele:

```text
SDF-Kugel
SDF-Noise-Terrain
Voxel-Grid
Feature-Instancing
OperationStackSource
```

Interface:

```csharp
public interface IVolumeSource
{
    float Sample(float3 worldPosition);
    int GetMaterial(float3 worldPosition);
}
```

Für Dual Contouring kann zusätzlich eine Normalenfunktion sinnvoll sein:

```csharp
public interface IAnalyticVolumeSource : IVolumeSource
{
    float3 GetNormal(float3 worldPosition);
}
```

Falls keine analytischen Normalen vorhanden sind, können Normalen über finite differences geschätzt werden.

---

## Volume Buffer

Der `VolumeBuffer` ist der zentrale Datencontainer.

Er hält die aktuelle, ausgewertete Volume-Repräsentation. Operationen verändern diesen Buffer. Mesher lesen aus diesem Buffer.

```text
VolumeSource
→ Sampling/Baking
→ VolumeBuffer
→ Operations
→ Mesher
```

Interface:

```csharp
public interface IVolumeBuffer
{
    VolumeLayout Layout { get; }

    bool HasCpuAccess { get; }
    bool HasGpuAccess { get; }

    NativeArray<float> DensityCpu { get; }
    NativeArray<int> MaterialCpu { get; }

    GraphicsBuffer DensityGpu { get; }
    GraphicsBuffer MaterialGpu { get; }

    void MarkDirty(BoundsInt region);
    IReadOnlyList<BoundsInt> GetDirtyRegions();
    void ClearDirtyRegions();
}
```

Beispiel für das Layout:

```csharp
public struct VolumeLayout
{
    public int3 Resolution;
    public float CellSize;
    public float3 Origin;
    public int ChunkSize;
    public float IsoLevel;
}
```

---

## CPU/GPU-Synchronisation

Da CPU und GPU auf unterschiedliche Speicherbereiche zugreifen, braucht der Buffer einen Synchronisationszustand.

```csharp
public enum BufferSyncState
{
    Synced,
    CpuDirty,
    GpuDirty
}
```

Regeln:

```text
CPU schreibt → CPU-Daten aktuell, GPU-Daten veraltet.
GPU schreibt → GPU-Daten aktuell, CPU-Daten veraltet.
CPU-Mesher braucht CPU-Daten.
GPU-Mesher braucht GPU-Daten.
Readback von GPU zu CPU nur wenn wirklich nötig.
```

Typische Fälle für GPU → CPU Readback:

```text
MeshCollider
Savegame
Export
Debugging
CPU-Gameplay-Abfragen
CPU-Meshing nach GPU-Operation
```

---

## Operations

Operationen ändern die Volume-Daten, nicht das fertige Mesh.

Beispiele:

```text
AddSphere
SubtractSphere
PlaceVoxel
RemoveVoxel
PaintMaterial
Smooth
Erode
NoiseDisplace
```

Interface:

```csharp
public interface IVolumeOperation
{
    BoundsInt AffectedRegion { get; }

    bool SupportsCpu { get; }
    bool SupportsGpu { get; }

    void ApplyCpu(IVolumeBuffer buffer);
    void ApplyGpu(IVolumeBuffer buffer, CommandBuffer commandBuffer);
}
```

GPU-Operationen können als Compute Shader umgesetzt werden.

Beispiel-GPU-Operation:

```text
DensityBuffer
→ AddSphere.compute
→ DensityBuffer aktualisiert
→ Dirty Region markieren
```

Eine Operation kann auch in einen OperationBuffer geschrieben werden:

```csharp
public struct VolumeOperationGpu
{
    public int Type;
    public float3 Position;
    public float Radius;
    public float Strength;
    public int MaterialId;
}
```

Dann kann ein Compute Shader mehrere Operationen in einem Pass anwenden.

---

## Operation Stack vs. Baked Buffer

Es gibt zwei mögliche Modi.

### Nicht-destruktiver Operation Stack

```text
BaseVolume + OperationStack = EvaluatedVolume
```

Vorteile:

```text
Undo/Redo einfach
Operationen bleiben editierbar
Gut für Editor und Tools
```

Nachteile:

```text
Viele Operationen können teuer werden
Sampling wird komplexer
```

### Eingebackener Buffer

```text
Operation wird direkt in DensityBuffer/MaterialBuffer geschrieben
```

Vorteile:

```text
Schnell zur Laufzeit
Mesher liest direkt finale Daten
Einfach für GPU
```

Nachteile:

```text
Undo/Redo schwieriger
Originalzustand muss separat gespeichert werden
```

Empfehlung:

```text
Editor/Authoring: Operation Stack
Runtime: Baked VolumeBuffer
```

---

## Storage System

`StorageMode` beschreibt die interne Speicherform.

Aktuell relevant:

```text
Tree = klassische Node-Hierarchie
Flat = linearisierte, bufferfähige Struktur
```

Für CPU/GPU ist `Flat` der wichtigste Pfad.

```text
Tree:
- einfacher zu verstehen
- gut für rekursive Algorithmen
- schlecht für GPU
- schwieriger für Burst

Flat:
- arraybasiert
- gut für Burst
- gut für Compute Shader
- geeignet für GraphicsBuffer
- besser für spätere GPU-Pipeline
```

Empfohlene Hauptstruktur:

```text
ChunkedFlatVolume
```

Also nicht ein riesiger globaler Buffer, sondern viele Chunks:

```text
World
└─ Chunk 0
   ├─ DensityBuffer
   ├─ MaterialBuffer
   ├─ Optional NodeBuffer
   └─ Dirty Flag

└─ Chunk 1
   ├─ DensityBuffer
   ├─ MaterialBuffer
   ├─ Optional NodeBuffer
   └─ Dirty Flag
```

---

## Mesher

Der Mesher erzeugt Geometrie aus dem VolumeBuffer.

Mögliche Mesher:

```text
VoxelMesher
GreedyVoxelMesher
MarchingCubesMesher
SurfaceNetsMesher
DualContouringMesher
```

Interface:

```csharp
public interface IVolumeMesher
{
    bool SupportsCpu { get; }
    bool SupportsGpu { get; }

    CpuMeshData BuildCpu(IVolumeBuffer buffer, MeshingContext context);
    GpuMeshData BuildGpu(IVolumeBuffer buffer, MeshingContext context);
}
```

Kontext:

```csharp
public struct MeshingContext
{
    public float IsoLevel;
    public float CellSize;
    public BoundsInt Region;
    public bool GenerateNormals;
    public bool GenerateMaterials;
}
```

---

## CPU-Meshing

CPU-Meshing sollte mit Jobs/Burst umgesetzt werden.

Gute Kandidaten:

```text
Density Sampling
Cell Classification
Edge Intersection
Normal Calculation
QEF für Dual Contouring
Marching-Cubes-Lookup
Surface-Nets-Vertexberechnung
Voxel-Face-Erzeugung
Greedy-Merge
Vertex-/Index-Buffer-Aufbau
```

CPU-Ergebnis:

```csharp
public struct CpuMeshData
{
    public NativeList<float3> Vertices;
    public NativeList<float3> Normals;
    public NativeList<float2> UVs;
    public NativeList<int> Indices;
    public NativeList<int> MaterialIds;
}
```

---

## GPU-Meshing

GPU-Meshing arbeitet mit Compute Shadern.

Typischer Ablauf:

```text
DensityBuffer / MaterialBuffer
→ Classification.compute
→ GenerateVertices.compute
→ GenerateTriangles.compute
→ TriangleBuffer / VertexBuffer
```

GPU-Ergebnis:

```csharp
public struct GpuMeshData
{
    public GraphicsBuffer VertexBuffer;
    public GraphicsBuffer IndexBuffer;
    public GraphicsBuffer ArgsBuffer;
    public int VertexCount;
    public int IndexCount;
}
```

Für dynamische GPU-Darstellung ist `DrawProceduralIndirect` besonders interessant.

```text
Compute Shader erzeugt TriangleBuffer
→ Render Shader liest TriangleBuffer
→ DrawProceduralIndirect
```

Wenn ein Unity Mesh gebraucht wird:

```text
Compute Shader erzeugt Buffer
→ AsyncGPUReadback
→ Unity Mesh setzen
```

Achtung: GPU → CPU Readback ist teuer und sollte nicht unnötig pro Frame passieren.

---

## Output System

Output entscheidet, was mit den erzeugten Daten passiert.

```text
UnityMeshOutput
ProceduralDrawOutput
MeshColliderOutput
DebugOutput
RaymarchOutput
```

Interface:

```csharp
public interface IVolumeOutput
{
    void ApplyCpuMesh(CpuMeshData meshData);
    void ApplyGpuMesh(GpuMeshData meshData);
}
```

### UnityMeshOutput

```text
CpuMeshData
→ UnityEngine.Mesh
→ MeshFilter.sharedMesh
→ MeshRenderer
```

Oder:

```text
GpuMeshData
→ AsyncGPUReadback
→ UnityEngine.Mesh
→ MeshFilter.sharedMesh
```

### ProceduralDrawOutput

```text
GpuMeshData
→ Material.SetBuffer(...)
→ Graphics.DrawProceduralIndirect(...)
```

### MeshColliderOutput

Benötigt normalerweise CPU-Meshdaten.

```text
CpuMeshData
→ Mesh
→ MeshCollider.sharedMesh
```

Bei GPU-Meshing ist dafür ein Readback nötig.

---

## Shader nach der Pipeline

Shader sind nicht Teil der Volume-Pipeline, sondern Teil der Unity-Renderpipeline.

```text
Volume → Storage → Mesher → Mesh/Buffer
→ Material
→ Vertex Shader
→ Fragment Shader
→ Post Processing
```

Ein fertiges Mesh kann danach beliebige Materialien und Shader erhalten:

```text
Triplanar Shader
Toon Shader
PBR Shader
Dissolve Shader
Wireframe Shader
Snow Shader
Water Shader
```

Vertex Shader können das Mesh visuell verändern.

Wichtig:

```text
Vertex Shader ändern nur die Darstellung auf der GPU.
Das echte Unity-Mesh, Collider, Raycasts und Exportdaten ändern sich nicht automatisch.
```

Wenn eine Änderung gameplay-relevant ist, muss sie als VolumeOperation vor dem Mesher passieren.

---

## Empfohlene Implementationsphasen

## Phase 1: Bestehende Pipeline entkoppeln

Ziel: `VolumeModel` soll nicht mehr direkt Dual Contouring enthalten.

Aufgaben:

```text
- IVolumeSource einführen
- IVolumeMesher einführen
- CpuMeshData einführen
- Bestehendes Dual Contouring in DualContouringMesher verschieben
- VolumeModel nur noch als Orchestrator verwenden
```

Ergebnis:

```text
SDF Source → DualContouringMesher → Unity Mesh
```

---

## Phase 2: VolumeBuffer einführen

Ziel: Gemeinsame Datenbasis für CPU und später GPU schaffen.

Aufgaben:

```text
- IVolumeBuffer definieren
- FlatGridVolumeBuffer implementieren
- DensityCpu und MaterialCpu als NativeArray verwenden
- Sampling aus SDF in Buffer schreiben
- Mesher liest nicht mehr direkt Source, sondern Buffer
```

Ergebnis:

```text
SDF Source → Flat VolumeBuffer → DualContouringMesher → Unity Mesh
```

---

## Phase 3: Weitere Mesher als Module

Ziel: `MesherType` sinnvoll nutzen.

Aufgaben:

```text
- VoxelMesher implementieren
- MarchingCubesMesher implementieren
- SurfaceNetsMesher implementieren
- MesherFactory erstellen
- Inspector-Auswahl anbinden
```

Ergebnis:

```text
Ein VolumeBuffer → mehrere Mesher auswählbar
```

---

## Phase 4: CPU Jobs/Burst

Ziel: Rechenintensive Meshing-Phasen parallelisieren.

Aufgaben:

```text
- SamplingJob
- CellClassificationJob
- VertexGenerationJob
- IndexGenerationJob
- NativeList/NativeStream für dynamische Ausgaben prüfen
- Burst-kompatible Datenstrukturen verwenden
```

Ergebnis:

```text
Flat VolumeBuffer → CPU Burst Mesher → Unity Mesh
```

---

## Phase 5: OperationSystem CPU

Ziel: Dynamische Änderungen am Volume ermöglichen.

Aufgaben:

```text
- IVolumeOperation definieren
- AddSphereOperation
- SubtractSphereOperation
- PaintMaterialOperation
- Dirty Regions einführen
- Nur betroffene Chunks remeshen
```

Ergebnis:

```text
Operation → VolumeBuffer ändern → Dirty Chunks remeshen
```

---

## Phase 6: Chunked Flat Storage

Ziel: Große Welten und partielle Updates ermöglichen.

Aufgaben:

```text
- Chunk-Klasse erstellen
- Pro Chunk eigene Density/Material Arrays
- Dirty Flag pro Chunk
- Chunk Bounds und Nachbarschaft verwalten
- Mesher pro Chunk ausführen
```

Ergebnis:

```text
World → Chunks → Dirty Chunks → lokale Mesh Updates
```

---

## Phase 7: GPU Buffer View

Ziel: VolumeBuffer GPU-fähig machen.

Aufgaben:

```text
- GraphicsBuffer für DensityGpu und MaterialGpu ergänzen
- Upload CPU → GPU implementieren
- BufferSyncState einführen
- ComputeBackend enum anbinden
- Erste Debug-Compute-Operation testen
```

Ergebnis:

```text
Flat VolumeBuffer mit CPU- und GPU-Zugriff
```

---

## Phase 8: GPU Operations

Ziel: Operations auch mit Compute Shadern ausführen.

Aufgaben:

```text
- ApplyOperations.compute erstellen
- OperationBuffer definieren
- Add/Subtract/Paint auf GPU umsetzen
- Dirty Regions weiterhin CPU-seitig verwalten
- Optional GPU → CPU Readback für Debug
```

Ergebnis:

```text
C# Operation oder Compute Operation → gleicher VolumeBuffer
```

---

## Phase 9: GPU Meshing

Ziel: Erste Mesher als Compute Shader implementieren.

Empfohlene Reihenfolge:

```text
1. VoxelMesher GPU
2. MarchingCubes GPU
3. SurfaceNets GPU
4. DualContouring GPU
```

Grund:

```text
Voxel und Marching Cubes sind deutlich einfacher auf GPU.
Dual Contouring braucht QEF, Normalen und Topologiebehandlung.
```

Ergebnis:

```text
VolumeBuffer GPU → Compute Mesher → GpuMeshData
```

---

## Phase 10: Procedural GPU Rendering

Ziel: Direct GPU Render ohne Unity Mesh.

Aufgaben:

```text
- GpuMeshData mit VertexBuffer/IndexBuffer/ArgsBuffer definieren
- ProceduralDrawOutput implementieren
- Material.SetBuffer verwenden
- DrawProceduralIndirect verwenden
- Shader liest Vertex-/TriangleBuffer
```

Ergebnis:

```text
GPU Compute Mesher → DrawProceduralIndirect
```

---

## Phase 11: GPU Compute → Unity Mesh

Ziel: GPU-generierte Meshes als Unity Mesh nutzbar machen.

Aufgaben:

```text
- AsyncGPUReadback implementieren
- GpuMeshData zu CpuMeshData konvertieren
- Unity Mesh setzen
- Optional MeshCollider aktualisieren
```

Ergebnis:

```text
GPU Compute Mesher → AsyncGPUReadback → Unity Mesh
```

---

## Phase 12: Erweiterungen

Mögliche spätere Erweiterungen:

```text
- Flat Octree Nodes
- Hash-DAG / Shared Subtrees
- Feature Library und Feature Instances
- LOD pro Chunk
- Material-Blending
- Triplanar Terrain Shader
- Water Mesher
- Collider-Mesher separat vom Render-Mesher
- Save/Load für VolumeBuffer und OperationStack
```

---

## Empfohlene Startentscheidung

Nicht direkt mit GPU anfangen.

Bester Start:

```text
1. Bestehendes Dual Contouring entkoppeln
2. Flat VolumeBuffer einführen
3. VoxelMesher als zweite Ausgabe implementieren
4. CPU Operations und Dirty Chunks bauen
5. Danach GPU Buffer und Compute Shader ergänzen
```

Damit bleibt das Projekt jederzeit lauffähig und wächst kontrolliert.

---

## Kurzfassung

Die Zielarchitektur ist:

```text
VolumeModel
→ VolumeSource
→ VolumeBuffer
→ OperationSystem
→ MeshingSystem
→ OutputSystem
→ Unity Shader/Rendering
```

Der `VolumeBuffer` ist die zentrale Schnittstelle.

Dadurch ist es egal, ob eine Änderung von C#, Jobs/Burst oder Compute Shadern kommt. Alle Stufen arbeiten auf derselben logischen Volume-Repräsentation.

