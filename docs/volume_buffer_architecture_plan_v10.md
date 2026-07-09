# Volume Buffer Architecture Plan v10

## Zweck des Dokuments

Dieses Dokument beschreibt die Zielarchitektur für eine modulare Volume-Engine in Unity.

Der zentrale Gedanke bleibt:

```text
VolumeSource
→ InitialBufferBuilder
→ ChunkManager
→ VolumeBuffer
→ OperationSystem
→ DirtyChunkSystem
→ Scheduler
→ MeshingSystem
→ OutputSystem
→ Unity Rendering
```

Der **VolumeBuffer** ist dabei die gemeinsame Runtime-Repräsentation des Volumens.  
Alle Operationen, Mesher, Outputs und später auch CPU-/GPU-Backends arbeiten auf derselben logischen Datenbasis.

---

# 1. Architekturziele

## 1.1 Hauptziele

- ein gemeinsamer Runtime-Zustand für Voxel, Greedy Voxel, Marching Cubes, Surface Nets und Dual Contouring
- dynamische Bearbeitung über Operationen
- Undo/Redo im Editor
- direkte Runtime-Operationen für Gameplay, Brushes und Simulation
- Dirty-Chunk-Remeshing statt vollständigem Rebuild
- CPU-first Implementierung mit späterer GPU-Erweiterung
- klare Trennung von Source, Buffer, Operationen, Meshing und Output
- spätere Unterstützung von SVO, HashDAG, Streaming und Savegames

## 1.2 Nicht-Ziele der Kernpipeline

Folgende Systeme gehören langfristig nicht direkt in die Kernpipeline:

- Savegame-Format
- HashDAG als Arbeitsstruktur
- externe Asset-Datenbanken
- Shader-Implementierungen
- konkrete Unity-Materiallogik
- Editor-UI-Details

Diese Systeme dürfen die Pipeline verwenden, sollen aber nicht deren Grundstruktur bestimmen.

---

# 2. High-Level-Zielarchitektur

```text
VolumeModel
│
├── Pipeline Configuration
│   ├── Source Settings
│   ├── Buffer Settings
│   ├── Data Structure Settings
│   ├── Operation Settings
│   ├── Meshing Settings
│   ├── Output Settings
│   └── Debug Settings
│
├── VolumeSource
│   ├── SdfSource
│   ├── VoxelSource
│   ├── FeatureSource
│   ├── MeshSource
│   └── NoiseSource
│
├── InitialBufferBuilder
│
├── ChunkManager
│   ├── Chunk Creation
│   ├── Chunk Lookup
│   ├── Chunk Pooling
│   ├── Chunk Streaming
│   └── Chunk Visibility
│
├── VolumeBuffer
│   ├── Density Channel
│   ├── Material Channel
│   ├── Attribute Channel
│   ├── Flags
│   ├── Dirty State
│   ├── CPU View
│   └── GPU View (später)
│
├── OperationSystem
│   ├── Persistent Operation Stack
│   ├── Direct Runtime Operations
│   ├── Operation Executor
│   ├── Operation History
│   └── Clipboard
│
├── DirtyChunkSystem
│   ├── Dirty Chunk Tracker
│   ├── Dependency Expansion
│   ├── Remesh Queue
│   └── Mesh Upload Queue
│
├── Scheduler
│   ├── Work Prioritization
│   ├── Frame Budgeting
│   ├── CPU Job Dispatch
│   ├── GPU Job Dispatch
│   └── Result Validation
│
├── MeshingSystem
│   ├── VoxelMesher
│   ├── GreedyVoxelMesher
│   ├── MarchingCubesMesher
│   ├── SurfaceNetsMesher
│   └── DualContouringMesher
│
└── OutputSystem
    ├── UnityMeshOutput
    ├── MeshColliderOutput
    ├── ProceduralDrawOutput
    └── DebugOutput
```

---

# 3. Finaler Datenfluss

## 3.1 Initialer Aufbau

```text
VolumeModel
    │
    ▼
VolumeSource
    │
    ▼
InitialBufferBuilder
    │
    ▼
VolumeBuffer
    │
    ▼
Initial Dirty Marking
    │
    ▼
MeshingSystem
    │
    ▼
OutputSystem
```

## 3.2 Runtime-Änderung

```text
User / Gameplay / Brush
    │
    ▼
Create IVolumeOperation
    │
    ▼
OperationSystem
    │
    ▼
OperationExecutor
    │
    ▼
Modify VolumeBuffer
    │
    ▼
DirtyChunkSystem
    │
    ▼
Scheduler
    │
    ▼
Remesh affected chunks
    │
    ▼
OutputSystem
```

## 3.3 Mesher-Wechsel

```text
SwitchMesher(...)
    │
    ▼
Keep same VolumeBuffer
    │
    ▼
Clear current mesh output
    │
    ▼
Rebuild mesh from VolumeBuffer
    │
    ▼
Apply new Output
```

Der Weltzustand bleibt identisch. Nur die Darstellung ändert sich.

---

# 4. VolumeModel

`VolumeModel` ist der zentrale Einstiegspunkt der Engine.

Es ist **kein Mesher**, **kein Buffer** und **kein Renderer**.  
Es besitzt die Konfiguration und koordiniert die Subsysteme.

## 4.1 Verantwortlichkeiten

```text
VolumeModel
│
├── besitzt Pipeline-Konfiguration
├── erzeugt und verbindet Runtime-Systeme
├── stellt Public API bereit
├── hält Referenzen auf aktive Systeme
├── koordiniert Rebuilds
├── koordiniert Mesher-Wechsel
├── koordiniert Output-Wechsel
└── bietet Debug-/Benchmark-Einstiegspunkte
```

## 4.2 Geforderte Kernfunktionen in Phase 1

Die erste Implementierungsphase muss eine stabile High-Level-API im `VolumeModel` bereitstellen.
Diese Funktionen sind keine Altlasten, sondern explizite Anforderungen an die neue Volume-Engine.

```text
VolumeModel
├── AddSdf(...)
├── AddObject(...)
├── RemoveObject(...)
├── ApplyMaterial(...)
├── Rebuild()
├── Preview()
├── Benchmark()
└── Pipeline Setup
```

Das `VolumeModel` darf diese Funktionen öffentlich anbieten, ohne deren gesamte Implementierung selbst zu enthalten.
Intern sollen sie möglichst früh auf spezialisierte Systeme wie `OperationSystem`, `MeshingSystem`, `OutputSystem` und `VolumeDebugDraw` delegieren.

## 4.3 Langfristige Zielstruktur

```text
VolumeModel
├── Pipeline Configuration
├── Public API
└── System References

VolumeSceneComposer
VolumeOperationSystem
VolumeMeshingSystem
VolumeOutputSystem
VolumeBenchmarkSystem
```

Die öffentliche API des `VolumeModel` kann gleich bleiben, während die Implementierung intern ausgelagert wird.

---

## 4.4 Interne Struktur des VolumeModel

Das `VolumeModel` stellt die zentrale Einstiegsklasse der Engine dar.

Es besitzt keine eigentliche Rendering- oder Meshing-Logik, sondern verwaltet die Konfiguration sowie die Referenzen auf alle aktiven Runtime-Systeme.

```text
VolumeModel
│
├── Pipeline Configuration
│
├── Sources
│   ├── Active VolumeSource
│   └── Optional Feature Library
│
├── Runtime Systems
│   ├── InitialBufferBuilder
│   ├── ChunkManager
│   ├── VolumeBuffer
│   ├── OperationSystem
│   ├── DirtyChunkSystem
│   ├── Scheduler
│   ├── MeshingSystem
│   ├── OutputSystem
│   └── VolumeDebugDraw
│
├── Runtime State
│   ├── Initialized
│   ├── Current Mesher
│   ├── Current Output
│   ├── Current Data Structure
│   ├── Build Version
│   └── Statistics
│
└── Public API
```

## 4.5 VolumeModel Felder

Eine mögliche interne Struktur könnte folgendermaßen aussehen:

```csharp
public class VolumeModel : MonoBehaviour
{
    // Configuration
    [SerializeField] private VolumePipelineConfig _pipelineConfig;

    [Header("Source")]
    [SerializeField] private VolumeSourceType _sourceType;
    [SerializeField] private ScriptableObject _sourceAsset;
    [SerializeField] private Bounds _initialBounds;
    [SerializeField] private int _initialMaterialId;

    [Header("Buffer")]
    [SerializeField] private Vector3Int _resolution;
    [SerializeField] private float _cellSize;
    [SerializeField] private int _chunkSize;
    [SerializeField] private float _isoLevel;
    [SerializeField] private VolumeBounds _worldBounds;
    [SerializeField] private DensityFormat _densityFormat;
    [SerializeField] private MaterialFormat _materialFormat;
    [SerializeField] private AttributeFormat _attributeFormat;

    [Header("Data Structure")]
    [SerializeField] private VolumeDataStructure _dataStructure;
    [SerializeField] private VolumeStorageMode _storageMode;
    [SerializeField] private int _octreeMinDepth;
    [SerializeField] private int _octreeMaxDepth;
    [SerializeField] private bool _adaptive;
    [SerializeField] private float _errorThreshold;
    [SerializeField] private bool _collapseUniformNodes;

    [Header("Compute")]
    [SerializeField] private ComputeBackend _computeBackend;
    [SerializeField] private bool _useJobs;
    [SerializeField] private bool _useBurst;
    [SerializeField] private bool _useGpuCompute;
    [SerializeField] private GpuSyncMode _gpuSyncMode;

    [Header("Operations")]
    [SerializeField] private bool _enableRuntimeEditing;
    [SerializeField] private bool _enableUndoRedo;
    [SerializeField] private int _maximumUndoSteps;
    [SerializeField] private bool _directApplyOperations;
    [SerializeField] private bool _mergeConsecutiveOperations;
    [SerializeField] private bool _storeInverseDeltas;
    [SerializeField] private OperationHistoryMode _operationHistoryMode;

    [Header("Meshing")]
    [SerializeField] private VolumeMesherType _mesherType;
    [SerializeField] private bool _generateNormals;
    [SerializeField] private NormalMode _normalMode;
    [SerializeField] private bool _generateMaterials;
    [SerializeField] private bool _generateCollider;
    [SerializeField] private bool _remeshDirtyOnly;
    [SerializeField] private LodMode _lodMode;
    [SerializeField] private bool _seamStitching;
    [SerializeField] private QefVertexMode _qefVertexMode;

    [Header("Output")]
    [SerializeField] private VolumeOutputType _outputType;
    [SerializeField] private Material _material;
    [SerializeField] private bool _useVertexColors;
    [SerializeField] private bool _useSubMeshes;
    [SerializeField] private MeshUpdateMode _meshUpdateMode;
    [SerializeField] private bool _updateMeshCollider;
    [SerializeField] private bool _castShadows;
    [SerializeField] private bool _receiveShadows;

    [Header("Debug")]
    [SerializeField] private bool _drawChunks;
    [SerializeField] private bool _drawBounds;
    [SerializeField] private bool _drawDirtyChunks;
    [SerializeField] private bool _drawStatistics;
    [SerializeField] private bool _enableBenchmarks;

    // Runtime Systems
    private IVolumeSource _source;
    private InitialBufferBuilder _bufferBuilder;
    private ChunkManager _chunkManager;
    private IVolumeBuffer _buffer;
    private VolumeOperationSystem _operations;
    private DirtyChunkSystem _dirtyChunks;
    private VolumeScheduler _scheduler;
    private IVolumeMesher _mesher;
    private IVolumeOutput _output;

    // Runtime State
    private bool _initialized;
    private int _buildVersion;
    private VolumeRuntimeStatistics _statistics;
}
```

Die konkreten Implementierungen können später vollständig austauschbar bleiben, da sämtliche Systeme möglichst über Interfaces kommunizieren.

Die serialisierten Felder können in Unity entweder direkt im `VolumeModel` liegen oder langfristig in eine eigene `VolumePipelineConfig` ausgelagert werden. Für Phase 1 ist es akzeptabel, die wichtigsten Parameter im `VolumeModel` sichtbar zu halten, damit die bestehende Arbeitsweise nicht sofort gebrochen wird.

## 4.6 VolumePipelineConfig als Zielstruktur

Langfristig sollte die reine Konfiguration aus dem `VolumeModel` herausgelöst werden.

```csharp
[Serializable]
public class VolumePipelineConfig
{
    public VolumeSourceSettings Source;
    public VolumeBufferSettings Buffer;
    public VolumeDataStructureSettings DataStructure;
    public VolumeComputeSettings Compute;
    public VolumeOperationSettings Operations;
    public VolumeMeshingSettings Meshing;
    public VolumeOutputSettings Output;
    public VolumeDebugSettings Debug;
}
```

Das `VolumeModel` besitzt dann nur noch eine Konfigurationsinstanz und baut daraus die Runtime-Systeme auf.

```text
VolumeModel
├── VolumePipelineConfig
├── Runtime System References
├── Runtime State
└── Public API
```

Dadurch bleibt das `VolumeModel` übersichtlich, während alle Parameter trotzdem zentral konfigurierbar bleiben.

## 4.7 Parametergruppen

### Source Settings

```csharp
[Serializable]
public struct VolumeSourceSettings
{
    public VolumeSourceType SourceType;
    public ScriptableObject SourceAsset;
    public SamplingMode SamplingMode;
    public VolumeBounds InitialBounds;
    public int InitialMaterialId;
}
```

Bedeutung:

```text
SourceType          Welche Art von Eingabe verwendet wird
SourceAsset         Referenz auf SDF, Mesh, Voxelgrid, FeatureLibrary oder Generator
SamplingMode        Wie die Source in den Buffer gesampelt wird
InitialBounds       Welcher Weltbereich initial aufgebaut wird
InitialMaterialId   Standardmaterial für neu erzeugte Voxeldaten
```

### Buffer Settings

```csharp
[Serializable]
public struct VolumeBufferSettings
{
    public Vector3Int Resolution;
    public float CellSize;
    public int ChunkSize;
    public float IsoLevel;
    public VolumeBounds WorldBounds;
    public DensityFormat DensityFormat;
    public MaterialFormat MaterialFormat;
    public AttributeFormat AttributeFormat;
    public CompressionMode Compression;
}
```

Bedeutung:

```text
Resolution       Anzahl der Zellen/Voxel im initialen Volumen
CellSize         Weltgröße einer Zelle
ChunkSize        Anzahl Zellen pro Chunk-Kante
IsoLevel         Schwellwert für Surface-Extraktion
WorldBounds      Weltbereich des Volumens
DensityFormat    Speicherformat der Dichtewerte
MaterialFormat   Speicherformat der Material-IDs
AttributeFormat  Speicherformat zusätzlicher Attribute
Compression      Optionale Kompression innerhalb der Chunks
```

### Data Structure Settings

```csharp
[Serializable]
public struct VolumeDataStructureSettings
{
    public VolumeDataStructure DataStructure;
    public VolumeStorageMode StorageMode;
    public int OctreeMinDepth;
    public int OctreeMaxDepth;
    public bool Adaptive;
    public float ErrorThreshold;
    public bool CollapseUniformNodes;
}
```

Bedeutung:

```text
DataStructure         Flat Grid, Chunked Grid, SVO oder später HashDAG
StorageMode           Dense, Sparse, Streamed oder Hybrid
OctreeMinDepth        Minimale Octree-Unterteilung
OctreeMaxDepth        Maximale Octree-Unterteilung
Adaptive              Ob adaptive Auflösung erlaubt ist
ErrorThreshold        Fehlergrenze für Vereinfachung/Adaptivität
CollapseUniformNodes  Ob homogene Bereiche zusammengefasst werden
```

### Compute Settings

```csharp
[Serializable]
public struct VolumeComputeSettings
{
    public ComputeBackend Backend;
    public bool UseJobs;
    public bool UseBurst;
    public bool UseGpuCompute;
    public GpuSyncMode GpuSyncMode;
}
```

Bedeutung:

```text
Backend        CPU, Jobs, GPU oder Hybrid
UseJobs        Unity Jobs verwenden
UseBurst       Burst für Jobs verwenden
UseGpuCompute  Compute Shader verwenden
GpuSyncMode    CPU/GPU-Synchronisationsstrategie
```

### Operation Settings

```csharp
[Serializable]
public struct VolumeOperationSettings
{
    public bool EnableRuntimeEditing;
    public bool EnableUndoRedo;
    public int MaximumUndoSteps;
    public bool DirectApplyOperations;
    public bool MergeConsecutiveOperations;
    public bool StoreInverseDeltas;
    public OperationHistoryMode HistoryMode;
}
```

Bedeutung:

```text
EnableRuntimeEditing       Änderungen während Play Mode erlauben
EnableUndoRedo             Persistent Operation Stack aktivieren
MaximumUndoSteps           Maximale Anzahl Undo-Schritte
DirectApplyOperations      Operationen direkt auf Buffer schreiben
MergeConsecutiveOperations Brush-Samples/ähnliche Edits zusammenfassen
StoreInverseDeltas         Inverse Deltas für Undo speichern
HistoryMode                Keine History, Delta-History oder Command-History
```

### Meshing Settings

```csharp
[Serializable]
public struct VolumeMeshingSettings
{
    public VolumeMesherType Mesher;
    public bool GenerateNormals;
    public NormalMode NormalMode;
    public bool GenerateMaterials;
    public bool GenerateCollider;
    public bool RemeshDirtyOnly;
    public LodMode LodMode;
    public bool SeamStitching;
    public QefVertexMode QefVertexMode;
}
```

Bedeutung:

```text
Mesher             Voxel, Greedy, Marching Cubes, Surface Nets oder Dual Contouring
GenerateNormals    Normals erzeugen
NormalMode         Face, Smooth, Gradient oder QEF-basiert
GenerateMaterials  Materialdaten in Mesh übertragen
GenerateCollider   Colliderdaten erzeugen
RemeshDirtyOnly    Nur geänderte Chunks neu vermeshen
LodMode            Kein LOD, Distance LOD, Octree LOD oder Streaming LOD
SeamStitching      Übergänge zwischen Chunks/LODs schließen
QefVertexMode      Dual-Contouring-Strategie für Zellvertexpositionen
```

### Output Settings

```csharp
[Serializable]
public struct VolumeOutputSettings
{
    public VolumeOutputType OutputType;
    public Material Material;
    public bool UseVertexColors;
    public bool UseSubMeshes;
    public MeshUpdateMode MeshUpdateMode;
    public bool UpdateMeshCollider;
    public bool CastShadows;
    public bool ReceiveShadows;
    public bool DebugVisualization;
}
```

Bedeutung:

```text
OutputType          Unity Mesh, MeshCollider, Procedural Draw oder Debug
Material            Standardmaterial für erzeugte Meshes
UseVertexColors     Material-/Debugdaten über Vertex Colors ausgeben
UseSubMeshes        Materialien über Submeshes trennen
MeshUpdateMode      Recreate, Update Existing oder Persistent Mesh Pool
UpdateMeshCollider  MeshCollider bei Meshänderungen aktualisieren
CastShadows         Schattenwurf aktivieren
ReceiveShadows      Schattenempfang aktivieren
DebugVisualization  Debug-Ausgabe aktivieren
```

### Debug Settings

```csharp
[Serializable]
public struct VolumeDebugSettings
{
    public bool DrawChunks;
    public bool DrawBounds;
    public bool DrawDirtyChunks;
    public bool DrawVoxelValues;
    public bool DrawNormals;
    public bool DrawQefData;
    public bool ShowStatistics;
    public bool EnableBenchmarks;
}
```

Bedeutung:

```text
DrawChunks        Chunk-Grenzen anzeigen
DrawBounds        Volume Bounds anzeigen
DrawDirtyChunks   Dirty Chunks anzeigen
DrawVoxelValues   Dichte-/Materialwerte visualisieren
DrawNormals       Normalen anzeigen
DrawQefData       QEF-Punkte/Features für Dual Contouring anzeigen
ShowStatistics    Runtime-Statistiken anzeigen
EnableBenchmarks  Benchmark-Funktionen aktivieren
```

## 4.8 Laufzeitinformationen

Neben der Konfiguration verwaltet das `VolumeModel` auch den aktuellen Runtime-Zustand.

```text
Runtime State
│
├── Current Mesher
├── Current Output
├── Current Buffer
├── Active Chunks
├── Dirty Chunks
├── Pending Jobs
├── Build Version
├── Last Build Time
├── Statistics
└── Debug Information
```

Diese Informationen dienen ausschließlich der Laufzeit und werden nicht dauerhaft gespeichert.

## 4.9 Öffentliche API

Das `VolumeModel` stellt eine möglichst stabile API bereit.

```text
Initialization
├── Initialize()
├── Rebuild()
├── Dispose()

Sources / Composition
├── SetSource()
├── RebuildFromSource()
├── AddSdf(...)
├── AddObject(...)
├── AddFeature(...)
├── RemoveObject(...)
└── ClearObjects()

Material / Attributes
├── ApplyMaterial(bounds, materialId)
├── ApplyMaterial(objectId, materialId)
├── PaintMaterial(brush, materialId)
├── SetAttribute(bounds, channel, value)
└── ClearMaterial(bounds)

Operations
├── ExecuteOperation()
├── ExecutePersistentOperation()
├── ExecuteDirectOperation()
├── Undo()
└── Redo()

Meshing
├── SwitchMesher()
├── RemeshDirty()
└── RemeshAll()

Output / Rendering
├── SwitchOutput()
├── SwitchRenderBackend()
├── ClearOutput()
└── RefreshOutput()

Runtime
├── Tick()
└── UpdateScheduler()

Debug / Gizmos
├── SetDebugMode(mode, enabled)
├── SetVoxelDebugMode(mode)
├── DrawDebug()
├── GetStatistics()
└── Benchmark()
```

Dadurch bleibt die öffentliche API auch dann stabil, wenn einzelne Subsysteme später ausgelagert oder ersetzt werden.

Die API beschreibt bewusst gewünschte Fähigkeiten, nicht zwingend konkrete Implementierungsorte.
`AddSdf`, `AddObject`, `RemoveObject` und `ApplyMaterial` sollen nach außen wie direkte Methoden wirken, intern aber über Operationen und Dirty-Chunk-Verarbeitung laufen.


## 4.10 Funktionsanforderungen des VolumeModel

Das `VolumeModel` muss die wichtigsten Benutzeraktionen als einfache High-Level-Funktionen verfügbar machen.
Diese Funktionen bilden die Arbeitsweise im Unity-Inspector, in Editor-Tools und in Runtime-Skripten ab.

### 4.10.1 SDFs hinzufügen

```csharp
public VolumeObjectId AddSdf(
    IVolumeSdf sdf,
    Transform transform,
    VolumeCombineMode combineMode,
    int materialId
);
```

Ziel:

```text
SDF-Objekt hinzufügen
    ▼
Create AddSdfOperation / FeaturePlaceOperation
    ▼
OperationSystem
    ▼
Write Density + Material into VolumeBuffer
    ▼
DirtyChunkSystem
    ▼
Scheduler
    ▼
Meshing / Output Refresh
```

Diese Funktion soll sowohl einfache primitive SDFs als auch zusammengesetzte SDF-Objekte unterstützen.
Der `combineMode` entscheidet, ob das SDF addiert, subtrahiert, geschnitten oder gemischt wird.

Mögliche Modi:

```text
Union
Subtract
Intersect
Replace
SmoothUnion
SmoothSubtract
```

### 4.10.2 Objekte hinzufügen

```csharp
public VolumeObjectId AddObject(
    VolumeObjectDefinition definition,
    Transform transform,
    VolumeCombineMode combineMode,
    int materialId
);
```

`AddObject(...)` ist die allgemeine Variante für konkrete Volume-Bausteine.
Ein Objekt kann intern aus verschiedenen Quellen stammen.

```text
VolumeObjectDefinition
├── SDF Primitive
├── SDF Graph
├── Mesh Source
├── Voxel Asset
├── Feature Definition
└── Procedural Generator
```

Die Funktion erzeugt keine direkte Mesh-Geometrie.
Sie schreibt über eine Operation in den `VolumeBuffer`.
Der aktuell gewählte Mesher entscheidet erst danach, ob daraus Voxel, Greedy Voxels, Marching Cubes, Surface Nets oder Dual Contouring entstehen.

### 4.10.3 Objekte entfernen

```csharp
public void RemoveObject(VolumeObjectId objectId);
```

Entfernen ist ebenfalls eine Operation.
Je nach History-Modus kann das Entfernen entweder als gespeichertes Delta oder als inverse Operation umgesetzt werden.

```text
RemoveObject
    ▼
Create RemoveObjectOperation / CarveOperation
    ▼
Store Undo Delta if enabled
    ▼
Modify VolumeBuffer
    ▼
Mark affected chunks dirty
    ▼
Remesh affected chunks
```

### 4.10.4 Material anwenden

```csharp
public void ApplyMaterial(VolumeBounds bounds, int materialId);
public void ApplyMaterial(VolumeObjectId objectId, int materialId);
public void PaintMaterial(VolumeBrush brush, int materialId);
```

Materialänderungen sind keine reine Renderer-Eigenschaft.
Sie gehören als eigener Channel in den `VolumeBuffer`.

```text
ApplyMaterial
    ▼
Create PaintOperation
    ▼
Write Material Channel
    ▼
DirtyChunkSystem.Mark(bounds, MaterialChanged)
    ▼
Update Mesh / Output
```

Der Mesher muss Materialdaten optional auslesen können.
Das Output-System entscheidet anschließend, ob Materialien über Vertex Colors, Submeshes, Material-IDs, Textur-Indizes oder Debugfarben dargestellt werden.

### 4.10.5 Debug- und Gizmo-Anzeige

Das `VolumeModel` soll Debug-Visualisierung zentral konfigurierbar machen, aber die eigentliche Zeichenlogik an `VolumeDebugDraw` delegieren.

```csharp
public void SetDebugMode(VolumeDebugMode mode, bool enabled);
public void SetVoxelDebugMode(VoxelDebugMode mode);
public VolumeRuntimeStatistics GetStatistics();
```

Mögliche Debug-Anzeigen:

```text
Debug / Gizmos
├── Volume Bounds
├── Chunk Bounds
├── Loaded Chunks
├── Dirty Chunks
├── Active Voxels
├── Density Samples
├── Material IDs
├── Surface Crossings
├── Normals / Gradients
├── QEF Points
├── Dual Contouring Vertices
├── LOD Boundaries
├── Operation Bounds
├── Brush Radius
└── Runtime Statistics
```

Für Unity kann die erste Version über `OnDrawGizmos()` und `OnDrawGizmosSelected()` arbeiten.
Langfristig sollte daraus ein eigenes Debug-System werden, damit Debug-Rendering nicht mit Meshing oder Runtime-Operationen vermischt wird.

```text
VolumeModel.OnDrawGizmos
    ▼
VolumeDebugDraw.Draw(model, debugSettings)
    ▼
Read-only access to ChunkManager / VolumeBuffer / DirtyChunkSystem
```

### 4.10.6 Preview und Benchmark

```csharp
public void Preview();
public VolumeBenchmarkResult Benchmark();
```

Preview und Benchmark sind explizite Werkzeuge der Engine.
Sie dürfen über das `VolumeModel` erreichbar sein, sollen aber intern nicht die Architektur umgehen.

```text
Preview / Benchmark
    ▼
Use current VolumePipelineConfig
    ▼
Run controlled rebuild / remesh / output update
    ▼
Collect timing, chunk count, vertex count, memory stats
```

Wichtige Messwerte:

```text
Build Time
Operation Time
Dirty Expansion Time
Meshing Time
Upload Time
Chunk Count
Dirty Chunk Count
Vertex Count
Triangle Count
Memory Usage
GPU Upload Bytes
```

---

## 4.11 VolumeModel als Fassade

Das `VolumeModel` ist nach außen die einfache Hauptklasse der Engine.
Intern ist es eine Fassade über spezialisierte Systeme.

```text
External Code / Inspector
    ▼
VolumeModel.AddSdf(...)
VolumeModel.AddObject(...)
VolumeModel.ApplyMaterial(...)
VolumeModel.SetDebugMode(...)
    ▼
Internal Systems
    ├── OperationSystem
    ├── DirtyChunkSystem
    ├── Scheduler
    ├── MeshingSystem
    ├── OutputSystem
    └── VolumeDebugDraw
```

Dadurch bleibt die Nutzung einfach, während die Implementierung modular bleibt.

---

## 4.12 Inspector-Konfiguration

Im Unity-Inspector sollte das `VolumeModel` sämtliche wichtigen Parameter zentral bündeln.

```text
VolumeModel
│
├── Source
│   ├── Source Type
│   ├── Source Asset
│   ├── Sampling Mode
│   └── Initial Bounds
│
├── Buffer
│   ├── Resolution
│   ├── Cell Size
│   ├── Chunk Size
│   ├── Iso Level
│   ├── World Bounds
│   ├── Density Format
│   ├── Material Format
│   └── Attribute Format
│
├── Data Structure
│   ├── Flat Grid
│   ├── Chunked Grid
│   ├── Sparse Voxel Octree
│   ├── Storage Mode
│   ├── Octree Min Depth
│   ├── Octree Max Depth
│   ├── Adaptive
│   └── Error Threshold
│
├── Compute
│   ├── CPU
│   ├── Burst
│   ├── Jobs
│   ├── GPU Compute
│   └── Sync Mode
│
├── Operations
│   ├── Runtime Editing
│   ├── Undo / Redo
│   ├── History Size
│   ├── Direct Apply
│   ├── Merge Operations
│   └── Store Inverse Deltas
│
├── Meshing
│   ├── Voxel
│   ├── Greedy
│   ├── Marching Cubes
│   ├── Surface Nets
│   ├── Dual Contouring
│   ├── Generate Normals
│   ├── Generate Collider
│   ├── LOD
│   ├── Seam Stitching
│   └── QEF Settings
│
├── Output
│   ├── Unity Mesh
│   ├── Procedural Draw
│   ├── Mesh Collider
│   ├── Material
│   ├── Vertex Colors
│   ├── SubMeshes
│   └── Shadow Settings
│
└── Debug
    ├── Draw Chunks
    ├── Draw Bounds
    ├── Draw Dirty Chunks
    ├── Draw QEF Data
    ├── Statistics
    └── Benchmark
```

Damit dient das `VolumeModel` als zentrale Konfigurations- und Verwaltungsinstanz der gesamten Volume-Pipeline.

---

# 5. Pipeline Configuration

Die Pipeline wird vollständig über das `VolumeModel` konfiguriert.

## 5.1 Source Settings

```text
SourceType
SourceAsset
SamplingMode
InitialMaterial
InitialBounds
```

Mögliche Sources:

- SDF
- Voxel Grid
- Mesh
- Feature Library
- Noise
- Procedural Generator

## 5.2 Buffer Settings

```text
Resolution
Cell Size
Chunk Size
Iso Level
World Bounds
Voxel Format
Density Format
Material Format
Attribute Format
Compression
```

## 5.3 Data Structure Settings

```text
DataStructure
StorageMode
Octree Max Depth
Octree Min Depth
Adaptive
Error Threshold
Collapse Uniform Nodes
```

Mögliche Datenstrukturen:

- Flat Grid
- Chunked Flat Grid
- Sparse Voxel Octree
- HashDAG (später, primär persistent/compressed)

## 5.4 Compute Settings

```text
ComputeBackend
Use Jobs
Use Burst
Use GPU Compute
GPU Sync Mode
```

## 5.5 Meshing Settings

```text
Mesher
Generate Normals
Normal Mode
Generate Materials
Generate Collider
Remesh Dirty Only
LOD Mode
Seam Stitching
QEF Vertex Mode
```

`QEF Vertex Mode` ist nur für Dual Contouring relevant.

## 5.6 Operation Settings

```text
Enable Runtime Editing
Enable Undo / Redo
Maximum Undo Steps
Direct Apply Operations
Merge Consecutive Operations
Store Inverse Deltas
Operation History Mode
```

## 5.7 Output Settings

```text
Output Type
Material
Vertex Colors
SubMeshes
Mesh Update Mode
Update MeshCollider
Shadow Casting
Receive Shadows
Debug Visualization
```

---

# 6. VolumeSource

Eine `VolumeSource` erzeugt nur die Ausgangsdaten.

Nach dem initialen Aufbau spielt die Source für die Runtime keine direkte Rolle mehr.

## 6.1 Beispiele

```text
IVolumeSource
├── SdfVolumeSource
├── VoxelGridSource
├── MeshVolumeSource
├── FeatureVolumeSource
├── NoiseVolumeSource
└── CompositeVolumeSource
```

## 6.2 Interface

```csharp
public interface IVolumeSource
{
    VolumeBounds GetBounds();
    void SampleInto(IVolumeWriteTarget target, VolumeSamplingContext context);
}
```

## 6.3 Warum die Source getrennt bleibt

Die Source beschreibt, woher die Anfangsdaten kommen.  
Der Buffer beschreibt den aktuellen Zustand.

Das verhindert, dass Runtime-Änderungen direkt an SDFs, Meshes oder externen Assets hängen.

---

# 7. InitialBufferBuilder

Der `InitialBufferBuilder` übersetzt beliebige Sources in den Runtime-Buffer.

## 7.1 Aufgaben

- Bounds bestimmen
- Chunks anlegen
- Source sampeln
- Dichtewerte schreiben
- Materialwerte schreiben
- Initial Dirty Chunks markieren
- optional Normals/Gradienten vorberechnen

## 7.2 Datenfluss

```text
IVolumeSource
    │
    ▼
InitialBufferBuilder
    │
    ▼
IVolumeBuffer
```

---

# 8. ChunkManager

Der `ChunkManager` verwaltet die Lebensdauer und Organisation der Chunks.

Er ist bewusst vom `VolumeBuffer` getrennt:

```text
ChunkManager = Welche Chunks existieren?
VolumeBuffer = Welche Daten enthalten diese Chunks?
DirtyChunkSystem = Welche Chunks müssen neu verarbeitet werden?
```

## 8.1 Aufgaben

```text
ChunkManager
│
├── Create Chunk
├── Destroy Chunk
├── Lookup Chunk
├── Pool Chunks
├── Stream Chunks
├── Track Visibility
├── Track Loaded State
└── Provide Job Views
```

## 8.2 Warum ein eigener ChunkManager sinnvoll ist

Ohne eigenen ChunkManager würde der `VolumeBuffer` zu viele Aufgaben übernehmen:

- Daten speichern
- Chunks erzeugen
- Chunks löschen
- Chunks streamen
- Sichtbarkeit verwalten
- Speicher-Pooling verwalten

Das würde den Buffer unnötig aufblähen.

Mit getrenntem ChunkManager bleibt der Buffer eine reine Datenstruktur.

## 8.3 Chunk Lifecycle

```text
Unloaded
    ↓
Requested
    ↓
Allocated
    ↓
Initialized
    ↓
Visible / Active
    ↓
Dirty
    ↓
MeshingQueued
    ↓
MeshReady
    ↓
Uploaded
    ↓
Inactive / Pooled
    ↓
Unloaded
```

## 8.4 Chunk Lookup

Für Runtime und Jobs wird schneller Zugriff auf Nachbar-Chunks benötigt.

High-Level:

```csharp
VolumeChunk GetChunk(ChunkCoord coord);
bool TryGetChunk(ChunkCoord coord, out VolumeChunk chunk);
```

Job-Level:

```csharp
NativeParallelHashMap<ChunkCoord, ChunkJobView>
```

Dadurch können Mesher-Jobs Nachbar-Chunks ohne Managed-Objekte und ohne virtuelle Interface-Aufrufe lesen.

## 8.5 Chunk Pooling

Chunks sollten später gepoolt werden.

```text
ChunkPool
├── Free Chunks
├── Active Chunks
├── Reusable NativeArrays
└── Optional GPU Buffers
```

Das reduziert Garbage Collection und verhindert teure Allokationen während Runtime-Edits.

## 8.6 Sichtbarkeit und Streaming

Der ChunkManager kann später entscheiden:

```text
Chunk loaded?
Chunk visible?
Chunk near camera?
Chunk should be meshed?
Chunk should have collider?
Chunk can be unloaded?
```

Dadurch wird Streaming möglich, ohne Mesher oder Operationen umzubauen.


---

# 9. VolumeBuffer

Der `VolumeBuffer` ist der aktuelle Weltzustand.

Er ist die wichtigste Schnittstelle der gesamten Engine.

```text
VolumeBuffer
│
├── Chunk Map
├── Density Channel
├── Material Channel
├── Attribute Channel
├── Flags Channel
├── Dirty State
├── Versioning
├── CPU View
└── GPU View
```

## 8.1 Grundsatz

Alle Systeme arbeiten auf dem `VolumeBuffer`.

```text
Source erzeugt Buffer
Operation verändert Buffer
Mesher liest Buffer
Output zeigt Mesh
SaveSystem serialisiert Buffer oder Deltas
GPUView spiegelt Buffer
```

## 8.2 Chunked Flat Buffer als erste Implementierung

Die erste Implementierung sollte ein `ChunkedFlatVolumeBuffer` sein.

```text
ChunkedFlatVolumeBuffer
│
├── Dictionary<ChunkCoord, VolumeChunk>
├── Chunk Size
├── Cell Size
├── World Bounds
└── Access Methods
```

Vorteile:

- einfach zu debuggen
- gut für Dirty-Chunk-Remeshing
- kompatibel mit Jobs/Burst
- später GPU-freundlich
- leichter als sofortiger Octree

## 8.3 VolumeChunk

```text
VolumeChunk
│
├── ChunkCoord
├── Density[]
├── MaterialId[]
├── Attribute[]
├── Flags[]
├── Dirty State
├── Version
└── Bounds
```

## 8.4 Speicherlayout

Für die erste Version wird ein SoA-Layout empfohlen.

```text
Density[]     float / half / sbyte
MaterialId[]  ushort / int
Flags[]       byte / uint
Attributes[]  optional
```

SoA = Structure of Arrays.

Vorteile:

- einzelne Channels können unabhängig gelesen werden
- Mesher muss nicht immer Materialdaten lesen
- Operationen können gezielt Channels ändern
- GPU-Buffer können einfacher gespiegelt werden

## 8.5 Zugriff

```csharp
public interface IVolumeBuffer
{
    float GetDensity(int x, int y, int z);
    void SetDensity(int x, int y, int z, float value);

    int GetMaterial(int x, int y, int z);
    void SetMaterial(int x, int y, int z, int materialId);

    VolumeChunk GetChunk(ChunkCoord coord);
}
```

Der `VolumeBuffer` markiert keine Chunks selbst als dirty.
Er bleibt eine passive Datenstruktur für den aktuellen Weltzustand.

Dirty-Markierung erfolgt ausschließlich außerhalb des Buffers:

```text
OperationExecutor
    ├── Execute Operation
    ├── Collect Affected Bounds
    └── DirtyChunkSystem.MarkDirty(bounds, reason)
```

Dadurch bleibt die Verantwortung klar getrennt:

```text
VolumeBuffer       = speichert Daten
OperationExecutor  = führt Änderungen aus
DirtyChunkSystem   = entscheidet, was neu verarbeitet werden muss
Scheduler          = plant die Verarbeitung
```

## 8.6 Boundary Voxels / Chunk Overlap

Mesher benötigen Nachbarwerte an Chunk-Grenzen.

Optionen:

```text
A) Chunks speichern exakt ihre Zellen
B) Mesher liest Nachbar-Chunks bei Bedarf
C) Chunks besitzen Ghost Cells / Padding
```

Empfehlung für Phase 1:

```text
B) Mesher liest Nachbar-Chunks bei Bedarf
```

Später kann für GPU/Jobs ein gepaddetes Layout ergänzt werden.


## 9.8 Job-/Burst-kompatible Views

Die High-Level-Interfaces sind gut für Architektur und Main-Thread-API.

Unity Jobs und Burst sollten jedoch keine Managed Interfaces verwenden.

Deshalb braucht der Buffer zusätzlich reine Struct-Views:

```csharp
public readonly struct ChunkJobView
{
    public readonly ChunkCoord Coord;
    public readonly NativeArray<float> Density;
    public readonly NativeArray<int> MaterialId;
    public readonly NativeArray<byte> Flags;
    public readonly int Size;
    public readonly int Version;
}
```

Für Mesher-Jobs:

```csharp
public readonly struct VolumeJobView
{
    public readonly NativeParallelHashMap<ChunkCoord, ChunkJobView> Chunks;
    public readonly int ChunkSize;
    public readonly float CellSize;
}
```

Damit können Jobs Nachbar-Chunks schnell lesen, ohne virtuelle Calls, ohne Garbage Collection und ohne Managed-Objekte.


---

# 10. OperationSystem

Das `OperationSystem` ist für alle Änderungen am `VolumeBuffer` zuständig.

Es gibt zwei Arten von Operationen:

```text
Persistent Operations
    ├── Editor
    ├── Undo
    ├── Redo
    └── History

Direct Runtime Operations
    ├── Gameplay
    ├── Brushes
    ├── Physics
    └── Temporary Runtime Changes
```

## 9.1 Warum alle Änderungen über Operationen laufen

Ohne Operationen entstehen viele Sonderwege:

```text
AddObject schreibt direkt in Buffer
Brush schreibt direkt in Buffer
Runtime schreibt direkt in Buffer
Undo kennt Änderungen nicht
Dirty Chunks werden vergessen
```

Mit Operationen wird daraus:

```text
Jede Änderung
    ▼
IVolumeOperation
    ▼
OperationExecutor
    ▼
VolumeBuffer
    ▼
DirtyChunkSystem
```

## 9.2 Operation-Pipeline

```text
Create Operation
    │
    ▼
Validate
    │
    ▼
Compute Affected Bounds
    │
    ▼
Execute
    │
    ▼
Write VolumeBuffer
    │
    ▼
Collect Changed Voxels / Chunks
    │
    ▼
Mark Dirty
    │
    ▼
Push History Entry (optional)
    │
    ▼
Schedule Remesh
```

## 9.3 Interface

```csharp
public interface IVolumeOperation
{
    VolumeBounds GetAffectedBounds();
    bool CanMergeWith(IVolumeOperation other);
    void Execute(IVolumeBuffer buffer, VolumeOperationContext context);
}
```

## 9.4 Reversible Operation

Für Undo/Redo:

```csharp
public interface IReversibleVolumeOperation : IVolumeOperation
{
    IVolumeOperation CreateInverse(IVolumeBuffer beforeState);
}
```

## 9.5 Delta-basierter Undo

Statt den ganzen Buffer zu kopieren, speichert man Deltas.

```text
VolumeDelta
│
├── Changed Voxels
├── Old Density
├── New Density
├── Old Material
├── New Material
└── Affected Chunks
```

Vorteil:

- deutlich weniger Speicher
- Undo ist schnell
- Redo ist symmetrisch

## 9.6 Persistent Operation Stack

```text
PersistentOperationStack
│
├── Undo Stack
├── Redo Stack
├── Current Transaction
├── Max Steps
└── Merge Policy
```

Typische Verwendung:

- Editor AddObject
- Editor RemoveObject
- Brush Stroke
- Copy/Paste
- Feature Placement

## 9.7 Direct Runtime Operations

Direct Operations werden nicht zwingend in den Undo-Stack geschrieben.

Verwendung:

- Gameplay-Zerstörung
- temporäre Effekte
- Explosionen
- Physics-Interaktion
- Runtime Brushes ohne Editor-History

```text
DirectOperation
    ▼
Execute immediately
    ▼
Mark Dirty
    ▼
No Undo Entry
```

## 9.8 Operation-Typen

```text
IVolumeOperation
│
├── AddSdfOperation
├── AddObjectOperation
├── RemoveObjectOperation
├── CarveOperation
├── FillOperation
├── PaintOperation
├── PaintMaterialOperation
├── SmoothOperation
├── CopyOperation
├── PasteOperation
├── BooleanUnionOperation
├── BooleanDifferenceOperation
├── BooleanIntersectionOperation
├── NoiseOperation
├── ErodeOperation
├── DilateOperation
├── FeaturePlaceOperation
└── CompositeOperation
```

## 9.9 Brush Operations

Brushes sollten nicht pro Frame einen eigenen Undo-Eintrag erzeugen.

```text
Brush Stroke
│
├── Begin Transaction
├── Apply many BrushSampleOperations
├── Merge samples
└── Commit single Undo Entry
```

## 9.10 CompositeOperation

Mehrere Operationen können zu einer logischen Operation zusammengefasst werden.

Beispiel:

```text
Place Feature
│
├── Carve old space
├── Fill new volume
├── Paint material
└── Smooth edges
```

Aus Sicht von Undo/Redo ist das ein einziger Schritt.

## 9.11 OperationExecutor

Der Executor ist die einzige Stelle, die Operationen tatsächlich ausführt.

```text
OperationExecutor
│
├── Validate Operation
├── Capture Delta
├── Execute Operation
├── Collect Affected Bounds
├── Send Bounds to DirtyChunkSystem
├── Push History
└── Notify Systems
```

## 9.12 OperationContext

```csharp
public struct VolumeOperationContext
{
    public bool StoreUndo;
    public bool DirectApply;
    public bool ReportDirtyBounds;
    public bool ScheduleRemesh;
    public float DeltaTime;
}
```

`ReportDirtyBounds` bedeutet nicht, dass der Buffer selbst Dirty-State verwaltet.
Es steuert nur, ob der `OperationExecutor` die betroffenen Bounds an das `DirtyChunkSystem` weitergibt.

---

# 11. DirtyChunkSystem

Das `DirtyChunkSystem` verfolgt, welche Chunks neu vermesht werden müssen.

## 10.1 Warum es ein eigenes System sein sollte

Dirty Tracking ist nicht Aufgabe des Meshers und nicht Aufgabe einzelner Operationen.

Operationen melden nur:

```text
Diese Bounds wurden verändert.
```

Das Dirty-System entscheidet:

```text
Welche Chunks sind betroffen?
Welche Nachbar-Chunks müssen wegen Grenzflächen ebenfalls neu?
In welcher Reihenfolge werden sie neu vermesht?
```

## 10.2 Dirty-Pipeline

```text
Changed Bounds
    │
    ▼
Find affected chunks
    │
    ▼
Expand for mesher dependencies
    │
    ▼
Mark dirty
    │
    ▼
Enqueue remesh jobs
    │
    ▼
Upload mesh results
```

## 10.3 Dependency Expansion

Mesher brauchen oft Nachbarinformationen.

```text
Voxel Mesher:          eigener Chunk + Nachbarwerte an Grenze
Greedy Mesher:         eigener Chunk + Grenze
Marching Cubes:        eigener Chunk + 1 Voxel Padding
Surface Nets:          eigener Chunk + 1 Voxel Padding
Dual Contouring:       eigener Chunk + Zell-/Feature-Nachbarn
```

Deshalb muss das Dirty-System bei Änderungen an Chunk-Grenzen Nachbarn mitmarkieren.

## 10.4 Dirty State

```text
Clean
DirtyData
DirtyMesh
MeshingQueued
MeshReady
Uploaded
```

## 10.5 Remesh Queue

```text
RemeshQueue
│
├── ChunkCoord
├── Priority
├── Reason
├── Version
└── Mesher Type
```

Versioning verhindert, dass veraltete Mesh-Jobs falsche Ergebnisse hochladen.

---

# 12. Scheduler

Der `Scheduler` entscheidet, wann und wie Arbeit ausgeführt wird.

Das Dirty-System sagt:

```text
Diese Chunks müssen neu verarbeitet werden.
```

Der Scheduler entscheidet:

```text
Welche Chunks zuerst?
CPU oder GPU?
Wie viel Arbeit pro Frame?
Welche Jobs sind veraltet?
Welche Ergebnisse dürfen hochgeladen werden?
```

## 12.1 Aufgaben

```text
Scheduler
│
├── Prioritize Work
├── Respect Frame Budget
├── Dispatch CPU Jobs
├── Dispatch GPU Jobs
├── Validate Chunk Versions
├── Drop Outdated Results
├── Upload Mesh Results
└── Balance Render / Collider Work
```

## 12.2 Prioritäten

Mögliche Prioritätskriterien:

- Nähe zur Kamera
- Sichtbarkeit
- Spielerinteraktion
- Chunk-Größe
- Collider wichtiger als Render-Mesh
- Debug-Modus
- LOD-Stufe

## 12.3 Frame Budget

Der Scheduler sollte später ein Budget pro Frame erhalten.

```text
Max Meshing Jobs Per Frame
Max Mesh Uploads Per Frame
Max Collider Updates Per Frame
Max GPU Upload Bytes Per Frame
```

Damit bleiben Brush-Edits und Gameplay flüssig.

## 12.4 Version Validation

Jeder Mesh-Job speichert die Chunk-Version beim Start.

```text
Job starts for Chunk X version 12
Chunk X changes to version 13
Job finishes with version 12
Result is discarded
```

Dadurch werden Flackern, falsche Meshes und Race Conditions verhindert.

## 12.5 CPU/GPU Dispatch

```text
RemeshQueue
    ↓
Scheduler
    ├── CPU Jobs / Burst
    └── GPU Compute
```

In den ersten Phasen ist CPU authoritative.  
GPU wird zunächst nur als Mirror oder Beschleuniger verwendet.


---

# 13. MeshingSystem

Das `MeshingSystem` erzeugt Meshdaten aus dem `VolumeBuffer`.

Der Mesher darf den Buffer nicht verändern.

## 11.1 Interface

```csharp
public interface IVolumeMesher
{
    VolumeMeshData BuildChunkMesh(
        IVolumeBuffer buffer,
        ChunkCoord chunk,
        VolumeMeshingContext context
    );
}
```

## 11.2 Mesher-Implementierungen

```text
IVolumeMesher
│
├── VoxelMesher
├── GreedyVoxelMesher
├── MarchingCubesMesher
├── SurfaceNetsMesher
└── DualContouringMesher
```

## 11.3 VoxelMesher

Eigenschaften:

- blockige Darstellung
- einfach
- gut zum Debuggen
- schneller erster Backend-Test

## 11.4 GreedyVoxelMesher

Eigenschaften:

- reduziert Quads
- sehr gut für Minecraft-artige Darstellung
- arbeitet gut auf Chunked Flat Buffer

## 11.5 Marching Cubes

Eigenschaften:

- glatte Iso-Surface
- einfacheres Verfahren als Dual Contouring
- gut als Vergleichsbackend

## 11.6 Surface Nets

Eigenschaften:

- simpler als Dual Contouring
- gute Zwischenlösung
- nützlich für adaptive Erweiterungen

## 11.7 Dual Contouring

Eigenschaften:

- erhält scharfe Features besser
- braucht zusätzliche Feature-/Gradienteninformationen
- QEF-Lösung pro Zelle
- später gut für deinen bestehenden Renderer

## 11.8 Mesher-Wechsel

```text
SwitchMesher
    ▼
Keep VolumeBuffer
    ▼
Clear Output Meshes
    ▼
Mark all chunks DirtyMesh
    ▼
Remesh with selected backend
```

---

# 14. OutputSystem

Das `OutputSystem` nimmt Meshdaten entgegen und bringt sie in Unity zur Anzeige.

## 12.1 Output-Typen

```text
IVolumeOutput
│
├── UnityMeshOutput
├── MeshColliderOutput
├── ProceduralDrawOutput
└── DebugOutput
```

## 12.2 Interface

```csharp
public interface IVolumeOutput
{
    void ApplyMesh(ChunkCoord chunk, VolumeMeshData meshData);
    void RemoveChunk(ChunkCoord chunk);
    void Clear();
}
```

## 12.3 UnityMeshOutput

Erzeugt oder aktualisiert pro Chunk ein Unity Mesh.

```text
ChunkMeshObject
│
├── MeshFilter
├── MeshRenderer
├── Mesh
└── Material
```

## 12.4 MeshColliderOutput

Kann getrennt vom Render-Mesh laufen.

Optionen:

```text
No Collider
Collider From Render Mesh
Simplified Collider
Separate Collider Mesher
```

## 12.5 ProceduralDrawOutput

Spätere GPU-Variante.

```text
GraphicsBuffer
DrawProcedural
Indirect Draw
GPU Meshlets
```

---

# 15. CPU-/GPU-Architektur

## 13.1 Grundsatz

GPU ist ein Backend, nicht die Architektur selbst.

```text
Logical VolumeBuffer
│
├── CPU View
└── GPU View
```

## 13.2 CPU View

Erste Implementierung:

```text
NativeArray<float> Density
NativeArray<int> Material
NativeArray<byte> Flags
```

## 13.3 GPU View

Spätere Implementierung:

```text
GraphicsBuffer DensityBuffer
GraphicsBuffer MaterialBuffer
GraphicsBuffer FlagsBuffer
GraphicsBuffer DirtyBuffer
```

## 13.4 Synchronisation

```text
CPU Operation
    ▼
CPU Buffer Dirty
    ▼
Upload affected chunks
    ▼
GPU Buffer updated
```

oder:

```text
GPU Operation
    ▼
GPU Buffer Dirty
    ▼
Readback optional
    ▼
CPU Buffer update / invalidation
```

## 13.5 Empfehlung

Phase 1 bis 8:

```text
CPU ist authoritative
GPU ist optionaler Mirror
```

Später kann GPU-authoritative geprüft werden.

---

# 16. Editor vs Runtime

## 14.1 Editor Workflow

```text
Editor Tool
    ▼
Create Persistent Operation
    ▼
Store Undo Delta
    ▼
Execute
    ▼
Dirty Chunks
    ▼
Remesh
```

## 14.2 Runtime Workflow

```text
Gameplay Event
    ▼
Create Direct Operation
    ▼
Execute
    ▼
Dirty Chunks
    ▼
Remesh
```

## 14.3 Hybrid Workflow

Brushes können runtime-artig laufen, aber am Ende einen persistenten Undo-Eintrag erzeugen.

```text
Begin Brush Stroke
    ▼
Apply Direct Samples
    ▼
Collect Delta
    ▼
Commit Persistent Operation
```

---

# 17. Event System

Das Event-System entkoppelt Systeme, die auf Volume-Änderungen reagieren.

Operationen sollten nicht direkt Mesher, Collider, Debug oder Streaming ansprechen.

Stattdessen:

```text
OperationExecutor
    ↓
VolumeChangedEvent
    ↓
DirtyChunkSystem
    ↓
Scheduler
    ↓
Meshing / Collider / Output
```

## 17.1 Wichtige Events

```text
VolumeChangedEvent
ChunkDirtyEvent
ChunkMeshingQueuedEvent
ChunkMeshReadyEvent
ChunkMeshUploadedEvent
MesherChangedEvent
OutputChangedEvent
BufferRebuiltEvent
```

## 17.2 Vorteile

- Systeme bleiben entkoppelt
- Debugging wird einfacher
- spätere Erweiterungen brauchen keine Änderungen an Operationen
- Collider, Lighting, NavMesh oder Streaming können unabhängig reagieren


---

# 18. Feature System

Features sind wiederverwendbare Volumenbausteine.

```text
FeatureLibrary
│
├── FeatureDefinition
│   ├── Source
│   ├── Bounds
│   ├── Default Material
│   └── Metadata
│
└── FeatureInstance
    ├── Transform
    ├── Material Override
    └── Operation Mode
```

## 15.1 Feature Placement

```text
FeatureInstance
    ▼
FeaturePlaceOperation
    ▼
OperationExecutor
    ▼
VolumeBuffer
```

---

# 19. Clipboard System

Das Clipboard arbeitet als Operation-Erweiterung.

```text
VolumeClipboard
│
├── Copied Bounds
├── Density Data
├── Material Data
├── Attribute Data
└── Pivot
```

## 16.1 Copy

```text
CopyOperation
    ▼
Read VolumeBuffer
    ▼
Store in VolumeClipboard
```

## 16.2 Paste

```text
PasteOperation
    ▼
Read VolumeClipboard
    ▼
Write VolumeBuffer
    ▼
Mark Dirty
```

---

# 20. Save / Load / HashDAG

## 17.1 Grundsatz

HashDAG gehört nicht in die direkte Kernpipeline.

```text
PersistentStorage
├── Savegame
├── Sparse Voxel Octree
└── HashDAG
        │
        ▼
Runtime VolumeBuffer
        │
        ▼
OperationSystem
        │
        ▼
MeshingSystem
```

## 17.2 Warum HashDAG nicht als Runtime-Arbeitsbuffer

HashDAG ist sehr gut für:

- Kompression
- wiederholte Strukturen
- Speicherung
- statische Daten

Aber schwieriger für:

- häufige lokale Edits
- Undo/Redo
- Brush Operations
- Dirty-Chunk-Remeshing
- GPU/CPU Sync in Echtzeit

Deshalb:

```text
HashDAG = Persistenz / Kompression / Streaming
VolumeBuffer = Runtime Editing / Meshing
```

---

# 21. Namespaces und Ordnerstruktur

```text
Runtime/
│
├── Core/
│   ├── VolumeModel.cs
│   ├── VolumePipelineConfig.cs
│   └── VolumeTypes.cs
│
├── Sources/
│   ├── IVolumeSource.cs
│   ├── SdfVolumeSource.cs
│   ├── VoxelGridSource.cs
│   └── FeatureVolumeSource.cs
│
├── Buffer/
│   ├── IVolumeBuffer.cs
│   ├── ChunkedFlatVolumeBuffer.cs
│   ├── VolumeChunk.cs
│   ├── ChunkCoord.cs
│   ├── VolumeChannels.cs
│   ├── VolumeChannel.cs
│   ├── ChunkJobView.cs
│   └── VolumeJobView.cs
│
├── Chunks/
│   ├── ChunkManager.cs
│   ├── ChunkPool.cs
│   ├── ChunkLifecycle.cs
│   └── ChunkVisibility.cs
│
├── Operations/
│   ├── IVolumeOperation.cs
│   ├── OperationExecutor.cs
│   ├── OperationStack.cs
│   ├── VolumeCommand.cs
│   ├── VolumeDelta.cs
│   ├── CarveOperation.cs
│   ├── FillOperation.cs
│   ├── PaintOperation.cs
│   ├── SmoothOperation.cs
│   └── CompositeOperation.cs
│
├── Dirty/
│   ├── DirtyChunkSystem.cs
│   ├── DirtyChunkTracker.cs
│   ├── RemeshQueue.cs
│   └── ChunkVersion.cs
│
├── Scheduling/
│   ├── VolumeScheduler.cs
│   ├── WorkPriority.cs
│   ├── FrameBudget.cs
│   └── MeshJobResult.cs
│
├── Events/
│   ├── VolumeEventBus.cs
│   ├── VolumeChangedEvent.cs
│   ├── ChunkDirtyEvent.cs
│   └── ChunkMeshReadyEvent.cs
│
├── Meshing/
│   ├── IVolumeMesher.cs
│   ├── VolumeMeshData.cs
│   ├── VoxelMesher.cs
│   ├── GreedyVoxelMesher.cs
│   ├── MarchingCubesMesher.cs
│   ├── SurfaceNetsMesher.cs
│   └── DualContouringMesher.cs
│
├── Output/
│   ├── IVolumeOutput.cs
│   ├── UnityMeshOutput.cs
│   ├── MeshColliderOutput.cs
│   └── DebugOutput.cs
│
├── Features/
│   ├── FeatureLibrary.cs
│   ├── FeatureDefinition.cs
│   └── FeatureInstance.cs
│
├── Clipboard/
│   └── VolumeClipboard.cs
│
└── Debug/
    ├── VolumeDebugDraw.cs
    └── VolumeBenchmark.cs
```

---

# 22. Zentrale Interfaces

## 19.1 IVolumeSource

```csharp
public interface IVolumeSource
{
    VolumeBounds GetBounds();
    void SampleInto(IVolumeWriteTarget target, VolumeSamplingContext context);
}
```

## 19.2 IVolumeBuffer

```csharp
public interface IVolumeBuffer
{
    float GetDensity(int x, int y, int z);
    void SetDensity(int x, int y, int z, float value);

    int GetMaterial(int x, int y, int z);
    void SetMaterial(int x, int y, int z, int materialId);

    VolumeChunk GetChunk(ChunkCoord coord);
}
```

Wichtig: `IVolumeBuffer` besitzt bewusst keine `MarkDirty(...)`-Methode.
Dirty Tracking ist Aufgabe des `DirtyChunkSystem`, nicht des Buffers.

## 19.3 IVolumeOperation

```csharp
public interface IVolumeOperation
{
    VolumeBounds GetAffectedBounds();
    void Execute(IVolumeBuffer buffer, VolumeOperationContext context);
}
```

## 19.4 IVolumeMesher

```csharp
public interface IVolumeMesher
{
    VolumeMeshData BuildChunkMesh(
        IVolumeBuffer buffer,
        ChunkCoord chunk,
        VolumeMeshingContext context
    );
}
```

## 19.5 IVolumeOutput

```csharp
public interface IVolumeOutput
{
    void ApplyMesh(ChunkCoord chunk, VolumeMeshData meshData);
    void RemoveChunk(ChunkCoord chunk);
    void Clear();
}
```

---

# 23. Wichtige Sequenzen

## 20.1 AddObject

```text
VolumeModel.AddObject(source)
    ▼
Create FeaturePlaceOperation / AddSourceOperation
    ▼
OperationExecutor.ExecutePersistent(...)
    ▼
Write to VolumeBuffer
    ▼
DirtyChunkSystem.Mark(...)
    ▼
MeshingSystem.RemeshDirty(...)
    ▼
OutputSystem.ApplyMesh(...)
```

## 20.2 RemoveObject

```text
VolumeModel.RemoveObject(objectId)
    ▼
Create RemoveFeatureOperation / CarveOperation
    ▼
Execute Persistent Operation
    ▼
Store Delta for Undo
    ▼
Mark Dirty Chunks
    ▼
Remesh
```

## 20.3 Brush Stroke

```text
BeginBrush
    ▼
Begin Operation Transaction
    ▼
Apply Brush Samples
    ▼
Merge Samples
    ▼
Commit Transaction
    ▼
Single Undo Entry
```

## 20.4 Rebuild

```text
VolumeModel.Rebuild()
    ▼
Clear Runtime Systems
    ▼
Source → InitialBufferBuilder
    ▼
New VolumeBuffer
    ▼
Mark all chunks dirty
    ▼
Remesh all
    ▼
Output
```

## 20.5 SwitchMesher

```text
VolumeModel.SwitchMesher(newMesher)
    ▼
Keep VolumeBuffer
    ▼
Replace Mesher
    ▼
Clear Output Meshes
    ▼
Mark all chunks DirtyMesh
    ▼
Remesh all visible chunks
```

## 20.6 AddSdf

```text
VolumeModel.AddSdf(sdf, transform, combineMode, materialId)
    ▼
Create AddSdfOperation
    ▼
Sample SDF into affected bounds
    ▼
Write Density Channel
    ▼
Write Material Channel
    ▼
Mark affected chunks dirty
    ▼
Remesh affected chunks
```

## 20.7 ApplyMaterial

```text
VolumeModel.ApplyMaterial(bounds, materialId)
    ▼
Create PaintMaterialOperation
    ▼
Write Material Channel
    ▼
Mark chunks DirtyMaterial / DirtyMesh
    ▼
Refresh mesh or output representation
```

## 20.8 Debug Gizmos

```text
VolumeModel.OnDrawGizmos()
    ▼
Read Debug Settings
    ▼
VolumeDebugDraw
    ▼
Draw Bounds / Chunks / Voxels / Dirty State / QEF Data
```

---

# 24. Implementierungsplan

## Phase 1 — Stabilisierung

- `VolumeModel` als stabile High-Level-Fassade definieren
- Pipeline-Konfiguration vereinheitlichen
- AddSdf/AddObject/RemoveObject/Rebuild als öffentliche API bereitstellen
- ApplyMaterial/PaintMaterial als Material-Operationen bereitstellen
- Debug- und Gizmo-Anzeigen für Bounds, Chunks, Voxels und QEF-Daten ergänzen
- DualContouringMesher als erstes vollwertiges Meshing-Backend anbinden

## Phase 2 — VolumeSource und BufferBuilder

- `IVolumeSource` einführen
- `InitialBufferBuilder` implementieren
- aktuelle SDF-Daten in neuen Buffer übersetzen
- Source und Runtime-Zustand trennen

## Phase 3 — ChunkManager und ChunkedFlatVolumeBuffer

- `ChunkManager` einführen
- Chunk Lifecycle definieren
- Chunk Pool vorbereiten
- `IVolumeBuffer` definieren
- `ChunkedFlatVolumeBuffer` implementieren
- `VolumeChunk` mit Density/Material/Flags
- ChunkCoord / Bounds / Indexing
- Dirty-State nicht im `IVolumeBuffer`-Interface exponieren
- Dirty Flags/Versionen über `DirtyChunkSystem` und Chunk-Metadaten verwalten

## Phase 4 — Mesher an Buffer anbinden

- Dual Contouring liest aus `IVolumeBuffer`
- VoxelMesher implementieren
- GreedyVoxelMesher implementieren
- Mesher-Auswahl im Inspector
- SwitchMesher ohne neuen Source-Build

## Phase 5 — DirtyChunkSystem und Scheduler

- DirtyChunkTracker
- Dependency Expansion
- RemeshQueue
- Versioning
- Scheduler einführen
- Work Priorities
- Frame Budget
- veraltete Mesh-Ergebnisse verwerfen
- nur betroffene Chunks neu vermeshen

## Phase 6 — OperationSystem Basis

- `IVolumeOperation`
- `OperationExecutor`
- Carve
- Fill
- Paint
- Smooth
- Dirty-Markierung über `OperationExecutor` → `DirtyChunkSystem`

## Phase 7 — Undo/Redo

- `VolumeDelta`
- PersistentOperationStack
- Undo/Redo
- Max Steps
- Merge Consecutive Operations

## Phase 8 — Brushes und Transactions

- Brush Stroke Transaction
- Merge von Brush Samples
- CompositeOperation
- Editor Workflow

## Phase 9 — Clipboard und Features

- VolumeClipboard
- Copy/Paste
- FeatureLibrary
- FeatureDefinition
- FeatureInstance
- FeaturePlaceOperation

## Phase 10 — Jobs/Burst

- NativeArray Layout
- `ChunkJobView`
- `VolumeJobView`
- NativeParallelHashMap für Chunk-Lookups
- Jobified Meshing
- Jobified Operations
- Thread-safe Dirty Queue

## Phase 11 — GPU Buffer View

- GraphicsBuffer für Density/Material/Flags
- Chunk Uploads
- CPU authoritative
- GPU Mirror

## Phase 12 — GPU Operations

- Compute Shader Operations
- Dirty Buffer
- optional Readback
- GPU/CPU Sync Policies

## Phase 13 — GPU Meshing / Procedural Draw

- GPU Mesher Prototyp
- ProceduralDrawOutput
- DrawIndirect
- Mesh Upload umgehen

## Phase 14 — Sparse Storage

- Sparse Voxel Octree Import/Export
- Streaming von Chunks
- Speicherkompression

## Phase 15 — HashDAG

- HashDAG als Persistenz-/Kompressionsformat
- Import in Runtime VolumeBuffer
- Export aus statischen Bereichen
- keine direkte Runtime-Edit-Struktur in Phase 15

---

# 25. Architekturentscheidungen

## 22.1 Warum VolumeBuffer als Single Source of Truth

Weil dadurch alle Systeme austauschbar bleiben.

```text
Ein Buffer
Viele Operationen
Viele Mesher
Viele Outputs
```

## 22.2 Warum OperationSystem vor Meshing

Weil Änderungen zuerst den Weltzustand ändern müssen.  
Meshing ist nur eine Darstellung dieses Zustands.

## 22.3 Warum DirtyChunkSystem separat

Weil Dirty Tracking von mehreren Systemen gebraucht wird:

- Operationen
- Mesher
- Output
- GPU Upload
- Debug
- Streaming

## 22.4 Warum HashDAG später

Weil HashDAG für Kompression gut ist, aber Runtime Editing stark erschwert.

## 22.5 Warum CPU-first

Weil CPU-first leichter zu debuggen ist und die Architektur validiert, bevor GPU-Komplexität hinzukommt.

---

# 26. Kurzfazit

Die beste Zielarchitektur ist:

```text
VolumeModel
    │
    ▼
VolumeSource
    │
    ▼
InitialBufferBuilder
    │
    ▼
ChunkManager
    │
    ▼
VolumeBuffer
    │
    ▼
OperationSystem
    │
    ▼
DirtyChunkSystem
    │
    ▼
Scheduler
    │
    ▼
MeshingSystem
    │
    ▼
OutputSystem
```

Der `VolumeBuffer` bleibt der zentrale Runtime-Zustand.  
Operationen sind der einzige saubere Weg, diesen Zustand zu ändern.  
Mesher sind austauschbare Leser des Buffers.  
Outputs sind austauschbare Darstellungen.  
SVO und HashDAG bleiben spätere Speicher-/Streaming-/Kompressionssysteme, nicht die erste Runtime-Arbeitsstruktur.
