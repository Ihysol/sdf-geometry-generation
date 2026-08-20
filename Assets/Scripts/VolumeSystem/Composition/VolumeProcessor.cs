using System.Collections.Generic;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

[DisallowMultipleComponent]
[RequireComponent(typeof(VolumeObjectRegistry))]
public class VolumeProcessor : MonoBehaviour
{
    [Header("Pipeline")]
     [SerializeField] public bool enablePipeline = true;
     [SerializeField] public PipelineMesherType pipelineMesherType = PipelineMesherType.DualContouring;
     [SerializeField] public ComputeBackend computeBackend = ComputeBackend.CPU;

     [Header("Data Structure")]
     [Tooltip("VoxelGrid = uniform resolution; Octree = adaptive (higher res near surfaces)")]
      [SerializeField] public VolumeDataStructure dataStructure = VolumeDataStructure.VoxelGrid;

     [Header("Layout")]
     [SerializeField] public Vector3Int resolution = new Vector3Int(128, 128, 128);
     [SerializeField] public int chunkSize = 16;
     [SerializeField] public float boundsExtent = 4f;

     [Header("Adaptive Octree")]
     [Tooltip("Maximum subdivision depth (3-8). Higher = finer detail near surfaces.")]
     [Range(3, 8)]
     [SerializeField] public int octreeMaxDepth = 6;
     [Tooltip("Minimum subdivision depth before surface checks begin.")]
     [Range(1, 5)]
     [SerializeField] public int octreeMinDepth = 3;
     [Tooltip("Corner value difference threshold for subdividing a node.")]
     [SerializeField] public float octreeSurfaceThreshold = 0.01f;

     [Header("Auto Expand")]
     [Tooltip("Automatically resize grid when objects fall outside current bounds")]
     [SerializeField] public bool autoExpand = true;
     [Tooltip("Padding factor around object bounds (1.0 = tight, 1.25 = 25% margin)")]
     [SerializeField] public float expandPaddingFactor = 1.25f;
     [Tooltip("Hard cap on resolution per axis (prevents unbounded growth). 0 = unlimited.")]
     [SerializeField] public int maxResolutionCap = 512;

    [Header("Core")]
    [SerializeField] public float isoLevel = 0f;
    [SerializeField] public Material surfaceMaterial;

    [Header("Add Object")]
    [SerializeField] public VolumeShapeType shapeToAdd = VolumeShapeType.Sphere;
    [SerializeField] public VolumeOperationRole roleToAdd = VolumeOperationRole.Add;

    [Header("Interaction")]
    [SerializeField] public bool rebuildOnMoveRelease = false;
    [SerializeField] public float moveReleaseDelaySeconds = 0.2f;

    // ---- Pipeline State ----
     private VolumePipeline _pipeline;
     private UnityMeshOutput _meshOutput;
     private MeshRenderer _meshRenderer;
     private ChunkRenderManager _chunkRenderers;
     private Transform _chunksParent;

     // ---- Octree (ARD) State ----
     private OctreeVolume _octreeVolume;
     private DualContouringOctreeMesher _octreeMesher;
        private MeshFilter _octreeMeshFilter;

     private bool _initialized;
       private int _buildVersion;
       private Bounds _dirtyBoundsWorld;
       private bool _hasDirtyBounds;
       private bool _lastRebuildWasPartial;
       private int _lastRemeshedChunkCount;
       private Vector3 _lastPosition;
       /// <summary>Cell size from initial layout — preserved across resizes (Anchored Cell Size).</summary>
       private float _anchoredCellSize;
    [SerializeField] private Transform _visualOutput; // ADR-001: User-facing rotation/scale wrapper (serialized for persistence)

    /// <summary>VisualOutput — user rotates/scales here, not the VolumeProcessor. See ADR-001.</summary>
    public Transform VisualOutput => EnsureVisualOutput();

    private Transform EnsureVisualOutput()
    {
        if (_visualOutput == null)
        {
            GameObject voObj = new GameObject("VisualOutput");
            voObj.transform.SetParent(transform, false);
            _visualOutput = voObj.transform;
        }
        return _visualOutput;
    }

    // ---- Undo/Redo ----
    private CommandStack _commandStack;

    public CommandStack CommandStack
    {
        get
        {
            if (_commandStack == null)
            {
                _commandStack = new CommandStack(64);
                _commandStack.OnStateChanged += OnUndoRedoStateChanged;
            }
            return _commandStack;
        }
    }

    private void OnUndoRedoStateChanged(Bounds affectedBounds)
    {
        // Trigger partial rebuild for the affected region
        if (affectedBounds.extents != Vector3.zero)
            MarkDirtyBounds(affectedBounds);
        else
            RebuildModel(); // Full rebuild when bounds unknown
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        // ADR-001: Prevent accidental rotation/scale on the VolumeProcessor itself.
        // User should rotate/scale _visualOutput, not this GameObject.
        if (transform.rotation != Quaternion.identity || transform.localScale != Vector3.one)
        {
            Debug.LogWarning($"[VolumeProcessor] Rotation/Scale detected on '{name}'! " +
                $"Per ADR-001, apply transforms to 'VisualOutput' child instead. Resetting now.");
            transform.rotation = Quaternion.identity;
            transform.localScale = Vector3.one;
        }
    }

    
#endif

#if UNITY_EDITOR
    private bool _editorUpdateRegistered;
#endif

    private Transform ObjectsRoot
    {
        get
        {
            Transform existing = transform.Find("Objects");
            if (existing != null) return existing;
            GameObject go = new GameObject("Objects");
            go.transform.SetParent(transform, false);
            return go.transform;
        }
    }

    /// <summary>Expose for Undo commands — creates if missing.</summary>
     internal Transform GetObjectsRoot() => ObjectsRoot;

     /// <summary>ADR-004: Access to the persistent edit layer. Null until pipeline initialized (flat mode only).</summary>
      public PersistentEditLayer EditLayer => _pipeline?.EditLayer;

      // ---- Standalone Edit Grid (mode-agnostic selection + SDF edit writes) ----

      /// <summary>
      /// Mode-agnostic edit grid used by the FPS edit controller.
      /// Flat: the live pipeline layout (source of truth — tracks resizes).
      /// Octree: an independent ADR-018 layout derived from transform + anchored cell size.
      /// </summary>
      public VolumeLayout EditLayout
      {
          get
          {
              if (_pipeline != null)
                  return _pipeline.Buffer.Layout;

              if (dataStructure == VolumeDataStructure.Octree)
              {
                  float cs = EnsureAnchoredCellSize();
                  Vector3Int res = new Vector3Int(
                      Mathf.Max(1, Mathf.CeilToInt(boundsExtent / cs)),
                      Mathf.Max(1, Mathf.CeilToInt(boundsExtent / cs)),
                      Mathf.Max(1, Mathf.CeilToInt(boundsExtent / cs)));
                  float extent = res.x * cs;
                   return new VolumeLayout
                   {
                       Resolution = res,
                       CellSize = cs,
                       Origin = transform.position - Vector3.one * (extent * 0.5f),
                      ChunkSize = chunkSize,
                      IsoLevel = isoLevel
                  };
              }

              return default;
          }
      }

      private VolumeObjectRegistry _registry;
       /// <summary>The SDF composition (registry of VolumeObjects). Truth in both modes; buffer density is truth only in flat. Cached — evaluated per ray step.</summary>
       public IScalarFieldSource SdfSource => _registry ??= GetComponent<VolumeObjectRegistry>();

      /// <summary>
      /// Samples edit-truth density at a world position.
      /// Flat: buffer (SDF sampling + replayed edit layer).
      /// Octree: SDF composition (edit primitives are registered as objects).
      /// </summary>
      public float SampleDensity(Vector3 worldPoint)
      {
          if (_pipeline != null)
          {
              var buf = _pipeline.Buffer;
              var layout = buf.Layout;
              Vector3Int idx = layout.WorldToIndex(worldPoint);
              if (!layout.IsInside(idx))
                  return float.MaxValue;
              return buf.DensityCpu[layout.IndexToOffset(idx)];
          }
          var source = SdfSource;
          return source != null ? source.Evaluate(worldPoint) : float.MaxValue;
      }

      /// <summary>Ensures an anchored cell size exists (ADR-018), capturing it on first use.</summary>
      private float EnsureAnchoredCellSize()
      {
          if (_anchoredCellSize <= 0f)
              _anchoredCellSize = boundsExtent / Mathf.Max(1, resolution.x);
          return _anchoredCellSize;
      }

      /// <summary>
      /// Fills or clears a single edit-grid cell. Flat: CellOperation into the edit layer.
      /// Octree: small box primitive (Add/Subtract) into the object registry — picked up by RebuildOctree.
      /// </summary>
      public void EditCell(Vector3Int index, bool fill)
      {
          if (_pipeline != null)
          {
              var cells = new System.Collections.Generic.List<Vector3Int> { index };
              _pipeline.EditLayer.Add(new CellOperation(cells, new EditAnchor { type = EditAnchorType.World }, fill));
              _pipeline.MarkDirty();
              return;
          }
          if (dataStructure != VolumeDataStructure.Octree) return;

          VolumeLayout layout = EditLayout;
          if (!layout.IsInside(index)) return;

          Vector3 center = layout.IndexToWorld(index);
          Bounds bounds = new Bounds(center, Vector3.one * layout.CellSize);
          AddEditPrimitive(VolumeShapeType.Box, fill ? VolumeOperationRole.Add : VolumeOperationRole.Subtract,
               center, new Vector3(layout.CellSize * 0.5f, layout.CellSize * 0.5f, layout.CellSize * 0.5f), 0f,
               bounds, $"EditCell_{(fill ? "Fill" : "Clear")}_{index.x}_{index.y}_{index.z}");
      }

      /// <summary>
      /// Brush edit (add or carve) around a world point. Flat: CarveOperation into the edit layer.
      /// Octree: sphere primitive (Add/Subtract) into the object registry.
      /// </summary>
      public void EditBrush(Bounds bounds, float depth, bool add)
      {
          if (_pipeline != null)
          {
              _pipeline.EditLayer.Add(new CarveOperation(bounds, new EditAnchor { type = EditAnchorType.World }, depth));
              _pipeline.MarkDirty();
              return;
          }
          if (dataStructure != VolumeDataStructure.Octree) return;

          float radius = Mathf.Max(bounds.size.x, Mathf.Max(bounds.size.y, bounds.size.z)) * 0.5f;
          AddEditPrimitive(VolumeShapeType.Sphere, add ? VolumeOperationRole.Add : VolumeOperationRole.Subtract,
              bounds.center, Vector3.zero, radius, bounds,
              $"EditBrush_{(add ? "Add" : "Carve")}_{_octreeEditCounter}");
      }

      /// <summary>
      /// Vertex/face drag: fill a small sphere at the new vertex position to pull the surface toward it.
      /// Flat: negative-depth CarveOperation (existing behavior). Octree: Add sphere primitive.
      /// </summary>
      public void EditVertexDrag(Bounds bounds, float depth)
      {
          if (_pipeline != null)
          {
              _pipeline.EditLayer.Add(new CarveOperation(bounds, new EditAnchor { type = EditAnchorType.World }, depth));
              return;
          }
          if (dataStructure != VolumeDataStructure.Octree) return;

          float radius = Mathf.Max(bounds.size.x, Mathf.Max(bounds.size.y, bounds.size.z)) * 0.5f;
          AddEditPrimitive(VolumeShapeType.Sphere, VolumeOperationRole.Add,
              bounds.center, Vector3.zero, radius, bounds,
              $"EditVertex_{_octreeEditCounter}");
      }

      private int _octreeEditCounter;

      /// <summary>Creates an edit primitive as a VolumeObject under the Objects root and triggers an octree rebuild.</summary>
      private void AddEditPrimitive(VolumeShapeType shape, VolumeOperationRole role, Vector3 worldCenter,
          Vector3 boxHalfExtents, float sphereRadius, Bounds affectedBounds, string name)
      {
          _octreeEditCounter++;

          GameObject child = new GameObject(name);
          child.transform.SetParent(ObjectsRoot, false);
          child.transform.localPosition = transform.InverseTransformPoint(worldCenter);

          VolumeObject vo = child.AddComponent<VolumeObject>();
          vo.shapeType = shape;
          vo.role = role;
          if (shape == VolumeShapeType.Box)
              vo.boxHalfExtents = boxHalfExtents;
          if (shape == VolumeShapeType.Sphere)
              vo.sphereRadius = sphereRadius;

          VolumeObjectRegistry composer = GetComponent<VolumeObjectRegistry>();
          if (composer != null && !composer.objects.Contains(vo))
              composer.objects.Add(vo);

          CommandStack.Push(new AddObjectCommand(this, composer?.objects.Count - 1 ?? 0, child, affectedBounds));
          _octreeDirty = true;

          Debug.Log($"[VolumeProcessor] Edit primitive: {name} ({role})");
      }

    private void Awake()
     {
         // Play mode: all pipeline state (_pipeline, _octreeMesher, _meshRenderer,
         // _chunkRenderers) is plain private fields that reset to null when play mode
         // starts. Re-initialize before Update()/edit calls run, or the octree edit
         // path (EditCell/EditBrush) early-returns and RebuildOctree no-ops on a null
         // mesher. Idempotent — guarded by _initialized inside Initialize().
         if (Application.isPlaying)
             Initialize();
     }

     public void Initialize()
     {
         if (_initialized) return;
        _initialized = true;
        _lastPosition = transform.position;
        if (enablePipeline) InitializePipeline();
    }

    private void InitializePipeline()
     {
         var oldRenderer = GetComponent<VolumeMeshRenderer>();
         if (oldRenderer != null)
             oldRenderer.enabled = false;

         if (surfaceMaterial == null)
             surfaceMaterial = new Material(Shader.Find("Standard"));

         Transform vo = EnsureVisualOutput();

         // ADR: Branch on data structure type
         if (dataStructure == VolumeDataStructure.Octree)
             InitializeOctreePipeline(vo);
         else
             InitializeFlatPipeline(vo);
     }

     private void InitializeFlatPipeline(Transform visualOutput)
      {
          Bounds bounds = new Bounds(transform.position, Vector3.one * boundsExtent);
          float cellSize = bounds.size.x / Mathf.Max(1, resolution.x);
          _anchoredCellSize = cellSize; // Capture for future resizes

          VolumeLayout layout = new VolumeLayout
          {
              Resolution = resolution,
              CellSize = cellSize,
              Origin = bounds.min,
              ChunkSize = chunkSize,
              IsoLevel = isoLevel
          };

         IVolumeMesher mesher = MesherFactory.Create(pipelineMesherType);

         GameObject meshObj = new GameObject("PipelineMeshOutput");
         meshObj.transform.SetParent(transform, false);
         var mf = meshObj.AddComponent<MeshFilter>();
         _meshRenderer = meshObj.AddComponent<MeshRenderer>();

         _meshOutput = new UnityMeshOutput(mf, _meshRenderer, surfaceMaterial);
         _pipeline = new VolumePipeline(layout, mesher);
         _pipeline.Initialize(_meshOutput);
         _pipeline.SetBackend(computeBackend);

         _chunksParent = new GameObject("Chunks").transform;
         _chunksParent.SetParent(visualOutput, false);

         Vector3Int gridSize = _pipeline.Buffer.ChunkGridSize;
         _chunkRenderers = new ChunkRenderManager();
         _chunkRenderers.Initialize(_pipeline.Buffer.TotalChunks, gridSize, _chunksParent, layout);
         _chunkRenderers.SetMaterial(surfaceMaterial);
         _pipeline.SetChunkRenderers(_chunkRenderers);

         GameObject.DestroyImmediate(meshObj);
         _meshOutput = null;

         Debug.Log($"[VolumeProcessor] Flat pipeline init: grid {bounds.min:F1}..{bounds.max:F1}, center={transform.position:F1}");
     }

     private void InitializeOctreePipeline(Transform visualOutput)
     {
         // Create single-mesh output under VisualOutput
         GameObject octObj = new GameObject("OctreeMeshOutput");
         octObj.transform.SetParent(visualOutput, false);

         _octreeMesher = new DualContouringOctreeMesher();
         _octreeMeshFilter = octObj.AddComponent<MeshFilter>();
         _meshRenderer = octObj.AddComponent<MeshRenderer>();
         _meshRenderer.sharedMaterial = surfaceMaterial;

         Debug.Log($"[VolumeProcessor] Octree pipeline init: maxDepth={octreeMaxDepth}, minDepth={octreeMinDepth}");
     }

    private bool _octreeDirty; // Octree path dirty flag (no pipeline dirty state)

     private void Update()
       {
           CheckModelTransformChanged();

           if (enablePipeline)
           {
               // Octree path — rebuild only when explicitly marked dirty
               if (dataStructure == VolumeDataStructure.Octree)
               {
                   if (_octreeDirty)
                       RebuildPipeline();
                   return;
               }

               // Flat grid path — budgeted scheduler
               if (_pipeline != null)
               {
                   _pipeline.Scheduler.MaxChunksPerFrame = 8;
                   _pipeline.Scheduler.UseTimeBudget = true;

                   if (_pipeline.Scheduler.HasPendingWork)
                       TickScheduler();

                   if (_pipeline.IsDirty && !_pipeline.Scheduler.HasPendingWork && !_pipeline.DirtyChunks.HasPendingWork)
                       RebuildPipeline();
               }
           }

           if (_chunkRenderers != null && surfaceMaterial != null)
               _chunkRenderers.SetMaterial(surfaceMaterial);
       }

    /// <summary>Model origin moved → shift grid + full rebuild.</summary>
       private void CheckModelTransformChanged()
        {
            if (!_initialized) return;

            Vector3 delta = transform.position - _lastPosition;
            if (delta.sqrMagnitude < 0.001f) return;

            _lastPosition = transform.position;

            // Octree path — mark dirty for next frame rebuild
            if (dataStructure == VolumeDataStructure.Octree)
            {
                _octreeDirty = true;
                _hasDirtyBounds = false;
                _dirtyBoundsWorld = default;
                return;
            }

            // Flat grid path
            if (_pipeline != null)
            {
                _pipeline.Buffer.UpdateOrigin(_pipeline.Buffer.Layout.Origin + delta);
                _hasDirtyBounds = false; _dirtyBoundsWorld = default;
                _pipeline.MarkDirty();
            }
        }

    /// <summary>Push the current surfaceMaterial to all active renderers (flat chunks + octree mesh).
     /// The material is only captured at pipeline init, so every rebuild must re-push it —
     /// otherwise inspector material changes are ignored (Update() does not run in edit mode).</summary>
     private void ApplySurfaceMaterial()
     {
         if (surfaceMaterial == null) return;
         if (_chunkRenderers != null)
             _chunkRenderers.SetMaterial(surfaceMaterial);
         if (_meshRenderer != null)
             _meshRenderer.sharedMaterial = surfaceMaterial;
     }

     private void RebuildPipeline()
      {
          VolumeObjectRegistry composer = GetComponent<VolumeObjectRegistry>();
          if (composer == null) return;

          ApplySurfaceMaterial();

          composer.RebuildComposition();

         if (composer.objects.Count == 0)
         {
             Debug.LogWarning("[VolumeProcessor] RebuildPipeline: no objects — add a shape first.");
             return;
         }

         // ARD Octree path
         if (dataStructure == VolumeDataStructure.Octree)
         {
             RebuildOctree(composer);
             return;
         }

         // Flat grid pipeline path
         if (_pipeline == null) return;

         // ADR-002: Check if all objects fit within the current grid.
         if (!CheckBoundsFit(composer))
             return;

         bool isPartial = _hasDirtyBounds;

         if (isPartial)
              _pipeline.Rebuild(composer, isoLevel, _dirtyBoundsWorld, transform);
          else
              _pipeline.Rebuild(composer, isoLevel, transform);

         _hasDirtyBounds = false; _dirtyBoundsWorld = default;

         if (isPartial)
         {
             _lastRebuildWasPartial = true;
             _lastRemeshedChunkCount = 0;
             Debug.Log($"[VolumeProcessor] RebuildPipeline (partial) queued, pending={_pipeline.Scheduler.PendingCount}");
         }
         else
         {
             _lastRebuildWasPartial = false;
             _lastRemeshedChunkCount = 0;
             Debug.Log($"[VolumeProcessor] RebuildPipeline (full) queued, pending={_pipeline.Scheduler.PendingCount}");
         }
     }

     /// <summary>Build octree from SDF composition and mesh it with dual contouring.</summary>
     private void RebuildOctree(VolumeObjectRegistry composer)
     {
         if (_octreeMesher == null || _octreeMeshFilter == null) return;

         Bounds total = composer.GetTotalBounds();
         if (total.extents == Vector3.zero)
         {
             Debug.LogWarning("[VolumeProcessor] RebuildOctree: no object bounds found.");
             return;
         }

         float padding = Mathf.Max(1f, expandPaddingFactor);
         Vector3 paddedSize = total.size * padding;
         float extent = Mathf.Max(paddedSize.x, Mathf.Max(paddedSize.y, paddedSize.z));
         Bounds buildBounds = new Bounds(total.center, new Vector3(extent, extent, extent));

         OctreeVolumeBuilder builder = new OctreeVolumeBuilder
         {
             center = buildBounds.center,
             size = buildBounds.size,
             boundsPadding = 0f,
             maxDepth = octreeMaxDepth,
             minDepth = octreeMinDepth,
             suppressBuildLog = false,
             useQefVertices = true
         };

         double start = Time.realtimeSinceStartup * 1000.0;

         _octreeVolume = builder.Build(composer);
         if (_octreeVolume == null || _octreeVolume.Root == null)
         {
             Debug.LogWarning("[VolumeProcessor] RebuildOctree: octree build returned null.");
             return;
         }

         Mesh mesh = _octreeMeshFilter.sharedMesh;
         if (mesh == null)
         {
             mesh = new Mesh();
             _octreeMeshFilter.sharedMesh = mesh;
         }

         _octreeMesher.BuildMesh(_octreeVolume, isoLevel, mesh);

         double elapsed = (Time.realtimeSinceStartup * 1000.0) - start;
         Debug.Log($"[VolumeProcessor] Octree rebuild done: {elapsed:F0}ms, nodes={_octreeVolume.TotalNodes}, surfaceLeaves={_octreeVolume.SurfaceLeaves}");

         _hasDirtyBounds = false;
         _dirtyBoundsWorld = default;
         _octreeDirty = false; // Clear dirty flag after successful rebuild
     }

    /// <summary>ADR-002: Check whether all objects fit in the current grid; resize if needed. Returns false if resized.</summary>
    private bool CheckBoundsFit(VolumeObjectRegistry composer)
    {
        Bounds total = composer.GetTotalBounds();
        if (total.extents == Vector3.zero)
            return true; // No objects to check

        // Current grid bounds in world space
        VolumeLayout layout = _pipeline.Buffer.Layout;
        Vector3 gridMin = layout.Origin;
        Vector3 gridMax = layout.Origin + new Vector3(
            layout.Resolution.x, layout.Resolution.y, layout.Resolution.z
        ) * layout.CellSize;

        // Check if total bounds fit inside grid
        bool fits = gridMin.x <= total.min.x && gridMin.y <= total.min.y && gridMin.z <= total.min.z &&
                     gridMax.x >= total.max.x && gridMax.y >= total.max.y && gridMax.z >= total.max.z;

        if (fits)
            return true;

        if (!autoExpand)
        {
            Debug.LogWarning($"[VolumeProcessor] Objects exceed grid bounds; rebuild skipped to preserve existing geometry. " +
                $"Grid: {gridMin:F1}..{gridMax:F1}, Objects: {total.min:F1}..{total.max:F1}. " +
                $"Enable autoExpand or manually increase boundsExtent.");
            return false;
        }

        ResizeGrid(total);
        return true;
    }

    /// <summary>ADR-002 + Anchored Cell Size: allocate new grid with original cell size; resolution scales instead.</summary>
      private void ResizeGrid(Bounds requiredBounds)
      {
          VolumeLayout oldLayout = _pipeline.Buffer.Layout;

          float padding = Mathf.Max(1f, expandPaddingFactor);
          Vector3 paddedSize = requiredBounds.size * padding;
          float extent = Mathf.Max(paddedSize.x, Mathf.Max(paddedSize.y, paddedSize.z));
          Vector3 center = requiredBounds.center;

          Bounds newBounds = new Bounds(center, new Vector3(extent, extent, extent));

          // Anchored Cell Size: resolution scales to fit new bounds at original cell size
            int rawRes = Mathf.CeilToInt(newBounds.size.x / _anchoredCellSize);
            Vector3Int newRes = new Vector3Int(rawRes, rawRes, rawRes);

          if (maxResolutionCap > 0)
          {
              bool capped = false;
              if (newRes.x > maxResolutionCap)
              {
                  newRes = new Vector3Int(maxResolutionCap, maxResolutionCap, maxResolutionCap);
                  capped = true;
              }
              if (capped)
              {
                  Debug.LogWarning($"[VolumeProcessor] Resolution capped at {maxResolutionCap}³ — objects may exceed grid. " +
                      $"Add a second VolumeProcessor or increase maxResolutionCap.");
              }
          }

          VolumeLayout newLayout = new VolumeLayout
          {
              Resolution = newRes,
              CellSize = _anchoredCellSize, // Anchored — never changes
              Origin = newBounds.min,
              ChunkSize = chunkSize,
              IsoLevel = isoLevel
          };

          Debug.Log($"[VolumeProcessor] Resizing grid: {oldLayout.Resolution} @ {oldLayout.CellSize:F4} → " +
              $"newRes @ {_anchoredCellSize:F4} (anchored), center={center:F1}, cells={newLayout.TotalCells:N0}");

          // Dispose old pipeline state
          _chunkRenderers?.Dispose();
          _pipeline?.Dispose();

          // Clear orphaned chunk children from the shared parent
          for (int i = _chunksParent.childCount - 1; i >= 0; i--)
              GameObject.DestroyImmediate(_chunksParent.GetChild(i).gameObject);

          // Rebuild pipeline with new layout
          IVolumeMesher mesher = MesherFactory.Create(pipelineMesherType);
          _pipeline = new VolumePipeline(newLayout, mesher);

          GameObject meshObj = new GameObject("PipelineMeshOutput");
          meshObj.transform.SetParent(transform, false);
          var mf = meshObj.AddComponent<MeshFilter>();
          _meshRenderer = meshObj.AddComponent<MeshRenderer>();

          _meshOutput = new UnityMeshOutput(mf, _meshRenderer, surfaceMaterial);
          _pipeline.Initialize(_meshOutput);
          _pipeline.SetBackend(computeBackend);

          Vector3Int gridSize = _pipeline.Buffer.ChunkGridSize;
          _chunkRenderers = new ChunkRenderManager();
          _chunkRenderers.Initialize(_pipeline.Buffer.TotalChunks, gridSize, _chunksParent, newLayout);
          _chunkRenderers.SetMaterial(surfaceMaterial);
          _pipeline.SetChunkRenderers(_chunkRenderers);

          GameObject.DestroyImmediate(meshObj);
          _meshOutput = null;

          // The caller continues with a synchronous full rebuild on this new pipeline.
          _hasDirtyBounds = false; _dirtyBoundsWorld = default;
      }

    public void Rebuild()
    {
        if (!_initialized) Initialize();
        _buildVersion++;
        RebuildPipeline();
    }

    public void Dispose()
     {
         _chunkRenderers?.Dispose();
         _chunkRenderers = null;
         _pipeline?.Dispose();
         _pipeline = null;
         _meshOutput?.Clear();
         _meshOutput = null;
         _octreeVolume = null;
         _octreeMesher = null;
         if (_octreeMeshFilter != null)
             GameObject.DestroyImmediate(_octreeMeshFilter.sharedMesh);
         _octreeMeshFilter = null;
         _initialized = false;
     }

    public int TickScheduler()
    {
        if (_pipeline == null) return 0;
        int processed = _pipeline.TickScheduler();
        _lastRemeshedChunkCount += processed;
        return processed;
    }

    public void ExecuteOperation(IVolumeOperation operation)
    {
        if (!_initialized) Initialize();
        _pipeline?.ApplyOperation(operation);
    }

    public void AddSelectedObject() => AddObject(shapeToAdd, roleToAdd);

    public void AddObject(VolumeShapeType shape, VolumeOperationRole role)
    {
        GameObject child = new GameObject($"VolumeObject_{shape}_{role}");
        child.transform.SetParent(ObjectsRoot, false);
        child.transform.localPosition = Vector3.zero;

        VolumeObject vo = child.AddComponent<VolumeObject>();
        vo.shapeType = shape;
        vo.role = role;

        VolumeObjectRegistry composer = GetComponent<VolumeObjectRegistry>();
        if (composer != null && !composer.objects.Contains(vo))
            composer.objects.Add(vo);

        bool canRebuildPartially =
            _initialized &&
            _pipeline != null &&
            composer != null &&
            composer.objects.Count > 1 &&
            role != VolumeOperationRole.Intersect;

        Bounds affectedBounds = canRebuildPartially ? vo.GetBounds() : default;
        #if UNITY_EDITOR
        Undo.RegisterFullObjectHierarchyUndo(child, "Add Volume Object");
        #endif

        CommandStack.Push(new AddObjectCommand(
            this,
            composer?.objects.Count - 1 ?? 0,
            child,
            affectedBounds));

        // Rebuild is triggered by OnUndoRedoStateChanged -> MarkDirtyBounds.

        Debug.Log($"[VolumeProcessor] Added {shape} ({role}), total={composer.objects.Count}");
    }

    public void RemoveLastObject()
    {
        Transform root = ObjectsRoot;
        if (root == null || root.childCount == 0) return;

        GameObject lastChild = root.GetChild(root.childCount - 1).gameObject;
        VolumeObjectRegistry composer = GetComponent<VolumeObjectRegistry>();
        VolumeObject vo = lastChild.GetComponent<VolumeObject>();

        // Save state for undo before destroying
        string name = lastChild.name;
        VolumeShapeType shape = vo?.shapeType ?? VolumeShapeType.Sphere;
        VolumeOperationRole role = vo?.role ?? VolumeOperationRole.Add;
        Vector3 localPos = lastChild.transform.localPosition;

#if UNITY_EDITOR
        Undo.DestroyObjectImmediate(lastChild);
#else
        Object.DestroyImmediate(lastChild);
#endif
        if (composer != null && vo != null) composer.objects.Remove(vo);

        Bounds affectedBounds = vo != null ? vo.GetBounds() : default;
        CommandStack.Push(new RemoveObjectCommand(this, name, shape, role, localPos, affectedBounds));
        // Rebuild is triggered by OnUndoRedoStateChanged -> MarkDirtyBounds/RebuildModel.
    }

    public void ClearObjects()
    {
        Transform root = transform.Find("Objects");
        if (root == null) return;

        VolumeObjectRegistry composer = GetComponent<VolumeObjectRegistry>();

        // Save state before destroying for undo
        List<VolumeObject> saved = composer?.objects ?? new List<VolumeObject>();

        for (int i = root.childCount - 1; i >= 0; i--)
            Object.DestroyImmediate(root.GetChild(i).gameObject);

        if (composer != null) composer.objects.Clear();

        CommandStack.Push(new ClearAllCommand(this, saved));
        // Rebuild is triggered by OnUndoRedoStateChanged -> RebuildModel.
    }

    public void RebuildModel()
     {
         double start = Time.realtimeSinceStartup * 1000.0;

         if (!_initialized) Initialize();
         _buildVersion++;
         _hasDirtyBounds = false; _dirtyBoundsWorld = default;
         _octreeDirty = true; // Mark octree dirty for next Update tick

         Debug.Log($"[VolumeProcessor] RebuildModel called");

         // Octree path — synchronous rebuild
          if (dataStructure == VolumeDataStructure.Octree)
          {
              if (!_initialized) Initialize();
              RebuildPipeline();
              double octreeElapsed = (Time.realtimeSinceStartup * 1000.0) - start;
              Debug.Log($"[VolumeProcessor] RebuildModel done: {octreeElapsed:F0}ms");
              return;
          }

         // Flat grid path
          if (!enablePipeline || _pipeline == null) return;

          ApplySurfaceMaterial();

          VolumeObjectRegistry composer = GetComponent<VolumeObjectRegistry>();
         if (composer == null) return;

         composer.RebuildComposition();
         if (composer.objects.Count == 0)
         {
             Debug.LogWarning("[VolumeProcessor] RebuildModel: no objects");
             return;
         }

         // ADR-002: Resize grid if objects exceed current bounds.
         if (!CheckBoundsFit(composer))
             return;

         _pipeline.Rebuild(composer, isoLevel);
         _lastRebuildWasPartial = false;
         _lastRemeshedChunkCount = DrainSync();

         double elapsed = (Time.realtimeSinceStartup * 1000.0) - start;
         Debug.Log($"[VolumeProcessor] RebuildModel done: {elapsed:F0}ms");
     }

    public void MarkDirtyBounds(Bounds worldBounds)
      {
          // Octree path — full rebuild since partial octree updates aren't implemented yet
          if (dataStructure == VolumeDataStructure.Octree)
          {
              _octreeDirty = true;
              return;
          }

          _hasDirtyBounds = true;
          if (_dirtyBoundsWorld.extents == Vector3.zero)
              _dirtyBoundsWorld = worldBounds;
          else
              _dirtyBoundsWorld.Encapsulate(worldBounds);

          if (enablePipeline && _pipeline != null)
              _pipeline.MarkDirty();
      }

    /// <summary>Drain all pending meshing for explicit synchronous full rebuilds.</summary>
    private int DrainSync()
    {
        _pipeline.Scheduler.MaxChunksPerFrame = int.MaxValue;
        _pipeline.Scheduler.UseTimeBudget = false;
        int processed = _pipeline.TickScheduler();
        _pipeline.Scheduler.UseTimeBudget = true;
        return processed;
    }

    public VolumePipeline Pipeline => _pipeline;
    public bool Initialized => _initialized;
    public int BuildVersion => _buildVersion;
    public bool LastRebuildWasPartial => _lastRebuildWasPartial;
    public int LastRemeshedChunkCount => _lastRemeshedChunkCount;

    // ---- Legacy stubs ----
    public bool ShouldUseInteractionPreview() => false;
    public bool ShouldAutoRebuildOnChange() => true;
    public bool ShouldAutoRebuildOnTransformChange() => true;
    public void NotifyInteractiveEdit() { }
    public bool SupportsPreviewDepth() => false;
    public float usePreviewDepthWhileInteracting = 1f;
    public bool SupportsPreviewResolution() => false;
    public Vector3Int usePreviewResolutionWhileInteracting = Vector3Int.zero;
    public bool IsPreviewInteractionActive => false;
    public void DrainPendingRenderChunksImmediately() { }
    public OctreeVolumeSampler octreeSampler => null;
         public VoxelGridSampler voxelGridSampler => CreateVoxelGridSampler();
         public SparseVoxelOctreeSampler sparseVoxelOctreeSampler => null;
      /// <summary>Direct access to the built octree volume (ARD mode only).</summary>
      public OctreeVolume octreeVolume => _octreeVolume;
    public bool drawChildGizmos => true;

    private VoxelGridSampler _voxelStub;
     private VoxelGridSampler CreateVoxelGridSampler()
     {
         if (_voxelStub == null)
         {
             _voxelStub = new VoxelGridSampler();
             int res = Mathf.Max(1, resolution.x);
             _voxelStub.builder.gridSize = new Vector3Int(res, res, res);
             _voxelStub.builder.gridExtent = new Vector3(boundsExtent, boundsExtent, boundsExtent);
         }
         return _voxelStub;
         }

         private void OnDrawGizmos()
         {
         // ADR-001: Draw gizmos relative to VisualOutput so they follow rotation/scale.
        Transform vo = _visualOutput;
        if (vo == null) return;

        // ADR-002: Show actual grid bounds from pipeline, not inspector default.
        Bounds volBounds;
        if (_pipeline != null)
        {
            var layout = _pipeline.Buffer.Layout;
            Vector3 size = new Vector3(
                layout.Resolution.x * layout.CellSize,
                layout.Resolution.y * layout.CellSize,
                layout.Resolution.z * layout.CellSize
            );
            volBounds = new Bounds(layout.Origin + size * 0.5f, size);
        }
        else
        {
            volBounds = new Bounds(transform.position, Vector3.one * boundsExtent);
        }

        // Apply VisualOutput transform to gizmo positions
        Gizmos.matrix = vo.localToWorldMatrix;
        if (vo.parent != null)
            Gizmos.matrix *= vo.parent.worldToLocalMatrix;

        Gizmos.color = new Color(0.5f, 0.8f, 1f, 0.12f);
        Gizmos.DrawCube(volBounds.center, volBounds.size);
        Gizmos.color = new Color(0.5f, 0.8f, 1f, 0.6f);
        Gizmos.DrawWireCube(volBounds.center, volBounds.size);

        Vector3 origin = volBounds.min;
        Gizmos.color = new Color(1f, 0.85f, 0f, 0.9f);
        Gizmos.DrawWireSphere(origin, volBounds.size.x * 0.04f);
        Gizmos.matrix = Matrix4x4.identity;
    }

#if UNITY_EDITOR
    private void OnEnable()
    {
        if (!Application.isPlaying && !Application.isBatchMode)
            RegisterEditorUpdate();
    }

    private void OnDisable()
    {
        if (!Application.isPlaying)
            UnregisterEditorUpdate();
    }

    private void RegisterEditorUpdate()
    {
        if (_editorUpdateRegistered) return;
        EditorApplication.update += EditorTickScheduler;
        _editorUpdateRegistered = true;
    }

    private void UnregisterEditorUpdate()
    {
        if (!_editorUpdateRegistered) return;
        EditorApplication.update -= EditorTickScheduler;
        _editorUpdateRegistered = false;
    }

    private void EditorTickScheduler()
      {
          // Don't steal focus during editor interactions (mouse drag, etc.)
          if (Event.current != null && Event.current.type == EventType.MouseDrag)
              return;

          if (!enablePipeline) return;

          // Model origin change in editor — same as Update() path.
          CheckModelTransformChanged();

          // Octree path — rebuild when dirty
          if (dataStructure == VolumeDataStructure.Octree)
          {
              if (_octreeDirty)
                  RebuildPipeline();
              return;
          }

          // Flat grid path — budgeted scheduler
          if (_pipeline == null) return;

          // Budgeted ticks in editor — 8 chunks or 5ms per editor update frame
          _pipeline.Scheduler.MaxChunksPerFrame = 8;
          _pipeline.Scheduler.UseTimeBudget = true;

          bool didWork = false;
          if (_pipeline.Scheduler.HasPendingWork)
              didWork |= TickScheduler() > 0;

          if (_pipeline.IsDirty && !_pipeline.Scheduler.HasPendingWork && !_pipeline.DirtyChunks.HasPendingWork)
          {
              RebuildPipeline();
              didWork = true;
          }

          // Only repaint when we actually did something — prevents stealing drag focus
          if (didWork)
              SceneView.RepaintAll();
      }
#endif
}

