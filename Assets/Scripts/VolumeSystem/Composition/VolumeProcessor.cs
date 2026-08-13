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

    [Header("Layout")]
    [SerializeField] public Vector3Int resolution = new Vector3Int(128, 128, 128);
    [SerializeField] public int chunkSize = 16;
    [SerializeField] public float boundsExtent = 4f;

   [Header("Auto Expand")]
    [Tooltip("Automatically resize grid when objects fall outside current bounds")]
    [SerializeField] public bool autoExpand = true;
    [Tooltip("Padding factor around object bounds (1.0 = tight, 1.25 = 25% margin)")]
    [SerializeField] public float expandPaddingFactor = 1.25f;

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
    private bool _initialized;
    private int _buildVersion;
    private Bounds _dirtyBoundsWorld;
    private bool _hasDirtyBounds;
    private bool _lastRebuildWasPartial;
    private int _lastRemeshedChunkCount;
    private Vector3 _lastPosition;
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

     /// <summary>ADR-004: Access to the persistent edit layer. Null until pipeline initialized.</summary>
     public PersistentEditLayer EditLayer => _pipeline?.EditLayer;

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

        Bounds bounds = new Bounds(transform.position, Vector3.one * boundsExtent);
        VolumeLayout layout = new VolumeLayout
        {
            Resolution = resolution,
            CellSize = bounds.size.x / Mathf.Max(1, resolution.x),
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

        // ADR-001: Ensure visual output wrapper exists (lazy-init)
        Transform vo = EnsureVisualOutput();

        _chunksParent = new GameObject("Chunks").transform;
        _chunksParent.SetParent(vo, false);

        Vector3Int gridSize = _pipeline.Buffer.ChunkGridSize;
        _chunkRenderers = new ChunkRenderManager();
        _chunkRenderers.Initialize(_pipeline.Buffer.TotalChunks, gridSize, _chunksParent, layout);
        _chunkRenderers.SetMaterial(surfaceMaterial);
        _pipeline.SetChunkRenderers(_chunkRenderers);

        GameObject.DestroyImmediate(meshObj);
        _meshOutput = null;

        Debug.Log($"[VolumeProcessor] Pipeline init: grid {bounds.min:F1}..{bounds.max:F1}, center={transform.position:F1}");
    }

    private void Update()
    {
        CheckModelTransformChanged();

        if (_pipeline != null && enablePipeline)
        {
            // Budgeted scheduler tick in play mode — 8 chunks or 5ms budget
            _pipeline.Scheduler.MaxChunksPerFrame = 8;
            _pipeline.Scheduler.UseTimeBudget = true;

            if (_pipeline.Scheduler.HasPendingWork)
                TickScheduler();

            if (_pipeline.IsDirty && !_pipeline.Scheduler.HasPendingWork && !_pipeline.DirtyChunks.HasPendingWork)
                RebuildPipeline();
        }

        if (_chunkRenderers != null && surfaceMaterial != null)
            _chunkRenderers.SetMaterial(surfaceMaterial);
    }

    /// <summary>Model origin moved → shift grid + full rebuild.</summary>
    private void CheckModelTransformChanged()
    {
        if (!_initialized || _pipeline == null) return;

        Vector3 delta = transform.position - _lastPosition;
        // Skip float drift — only rebuild on actual movement (> 1mm threshold)
        if (delta.sqrMagnitude < 0.001f) return;

        _lastPosition = transform.position;

        // Grid must follow the model — shift origin by same delta.
        _pipeline.Buffer.UpdateOrigin(_pipeline.Buffer.Layout.Origin + delta);

        // Every cell is now at a different world coordinate → full rebuild.
        _hasDirtyBounds = false; _dirtyBoundsWorld = default;
        _pipeline.MarkDirty();
    }

    private void RebuildPipeline()
    {
        VolumeObjectRegistry composer = GetComponent<VolumeObjectRegistry>();
        if (composer == null || _pipeline == null) return;

        composer.RebuildComposition();

        if (composer.objects.Count == 0)
        {
            Debug.LogWarning("[VolumeProcessor] RebuildPipeline: no objects — add a shape first.");
            return;
        }

        // ADR-002: Check if all objects fit within the current grid.
        if (!CheckBoundsFit(composer))
            return; // Skipped — autoExpand=false, objects exceed grid bounds (warning logged)

        bool isPartial = _hasDirtyBounds;

        if (isPartial)
             _pipeline.Rebuild(composer, isoLevel, _dirtyBoundsWorld, transform);
         else
             _pipeline.Rebuild(composer, isoLevel, transform);

        _hasDirtyBounds = false; _dirtyBoundsWorld = default;

        // Both partial and full meshing are processed by budgeted scheduler ticks.
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

    /// <summary>ADR-002: Allocate a new grid large enough to contain the given bounds.</summary>
    private void ResizeGrid(Bounds requiredBounds)
    {
        VolumeLayout oldLayout = _pipeline.Buffer.Layout;

        // Reserve movement headroom only when allocating a new grid. Checking the
        // unpadded bounds above prevents tiny moves from repeatedly reallocating it.
        float padding = Mathf.Max(1f, expandPaddingFactor);
        Vector3 paddedSize = requiredBounds.size * padding;
        float extent = Mathf.Max(paddedSize.x, Mathf.Max(paddedSize.y, paddedSize.z));
        Vector3 center = requiredBounds.center;

        Bounds newBounds = new Bounds(center, new Vector3(extent, extent, extent));
        VolumeLayout newLayout = new VolumeLayout
        {
            Resolution = resolution,
            CellSize = newBounds.size.x / Mathf.Max(1, resolution.x),
            Origin = newBounds.min,
            ChunkSize = chunkSize,
            IsoLevel = isoLevel
        };

        Debug.Log($"[VolumeProcessor] Resizing grid: {oldLayout.Resolution} @ {oldLayout.CellSize:F4} → " +
            $"{newLayout.Resolution} @ {newLayout.CellSize:F4}, center={center:F1}");

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
        CommandStack.Push(new AddObjectCommand(
            this,
            composer?.objects.Count - 1 ?? 0,
            child,
            affectedBounds));

        // Rebuild is triggered by OnUndoRedoStateChanged -> MarkDirtyBounds.
        // Do NOT call RebuildDirty() here — that caused double rebuild with stale bounds.

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

        Debug.Log($"[VolumeProcessor] RebuildModel called");

        if (!enablePipeline || _pipeline == null) return;

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
    public VolumeDataStructure dataStructure => VolumeDataStructure.VoxelGrid;
    public VoxelGridSampler voxelGridSampler => CreateVoxelGridSampler();
    public OctreeVolumeSampler octreeSampler => null;
    public SparseVoxelOctreeSampler sparseVoxelOctreeSampler => null;
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
        // Handle Ctrl+Z/Ctrl+Y — only when this processor is selected (avoids stealing undo from other objects)
        if (Selection.activeGameObject == gameObject)
        {
            if (Event.current != null && Event.current.type == EventType.ValidateCommand && Event.current.commandName == "UndoRedoPerformed")
            {
                bool isRedo = Event.current.rawType == EventType.Command;
                var stack = CommandStack;
                if (isRedo && stack.CanRedo)
                    stack.Redo();
                else if (!isRedo && stack.CanUndo)
                    stack.Undo();
            }
        }

        if (_pipeline == null || !enablePipeline) return;

        // Model origin change in editor — same as Update() path.
        CheckModelTransformChanged();

        // Budgeted ticks in editor — 8 chunks or 5ms per editor update frame
        _pipeline.Scheduler.MaxChunksPerFrame = 8;
        _pipeline.Scheduler.UseTimeBudget = true;

        if (_pipeline.Scheduler.HasPendingWork)
            TickScheduler();

        if (_pipeline.IsDirty && !_pipeline.Scheduler.HasPendingWork && !_pipeline.DirtyChunks.HasPendingWork)
            RebuildPipeline();

        SceneView.RepaintAll();
    }
#endif

    private void OnDestroy() => Dispose();
}
