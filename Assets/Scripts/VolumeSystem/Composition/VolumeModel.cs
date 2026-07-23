using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

[DisallowMultipleComponent]
[RequireComponent(typeof(VolumeSceneComposer))]
public class VolumeModel : MonoBehaviour
{
    [Header("Pipeline")]
    [SerializeField] public bool enablePipeline = true;
    [SerializeField] public PipelineMesherType pipelineMesherType = PipelineMesherType.DualContouring;
    [SerializeField] public ComputeBackend computeBackend = ComputeBackend.CPU;

    [Header("Layout")]
    [SerializeField] public Vector3Int resolution = new Vector3Int(128, 128, 128);
    [SerializeField] public int chunkSize = 16;
    [SerializeField] public float boundsExtent = 4f;

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
    private Vector3 _lastPosition;

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

        _chunksParent = new GameObject("Chunks").transform;
        _chunksParent.SetParent(transform, false);

        Vector3Int gridSize = _pipeline.Buffer.ChunkGridSize;
        _chunkRenderers = new ChunkRenderManager();
        _chunkRenderers.Initialize(_pipeline.Buffer.TotalChunks, gridSize, _chunksParent, layout);
        _chunkRenderers.SetMaterial(surfaceMaterial);
        _pipeline.SetChunkRenderers(_chunkRenderers);

        GameObject.DestroyImmediate(meshObj);
        _meshOutput = null;

        Debug.Log($"[VolumeModel] Pipeline init: grid {bounds.min:F1}..{bounds.max:F1}, center={transform.position:F1}");
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
                _pipeline.TickScheduler();

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

        if (transform.position != _lastPosition)
        {
            Vector3 delta = transform.position - _lastPosition;
            _lastPosition = transform.position;

            // Grid must follow the model — shift origin by same delta.
            _pipeline.Buffer.UpdateOrigin(_pipeline.Buffer.Layout.Origin + delta);

            // Every cell is now at a different world coordinate → full rebuild.
            _hasDirtyBounds = false;
            _pipeline.MarkDirty();
        }
    }

    private void RebuildPipeline()
    {
        VolumeSceneComposer composer = GetComponent<VolumeSceneComposer>();
        if (composer == null || _pipeline == null) return;

        composer.RebuildComposition();

        if (composer.objects.Count == 0)
        {
            Debug.LogWarning("[VolumeModel] RebuildPipeline: no objects — add a shape first.");
            return;
        }

        bool isPartial = _hasDirtyBounds;

        if (isPartial)
            _pipeline.Rebuild(composer, isoLevel, _dirtyBoundsWorld);
        else
            _pipeline.Rebuild(composer, isoLevel);

        _hasDirtyBounds = false;

        // Partial: drain sync for instant feedback. Full: async via scheduler budgeting.
        if (isPartial)
        {
            DrainSync();
            Debug.Log($"[VolumeModel] RebuildPipeline (partial) done");
        }
        else
        {
            Debug.Log($"[VolumeModel] RebuildPipeline (full) queued, pending={_pipeline.Scheduler.PendingCount}");
        }
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

    public void TickScheduler() => _pipeline?.TickScheduler();

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

        VolumeSceneComposer composer = GetComponent<VolumeSceneComposer>();
        if (composer != null && !composer.objects.Contains(vo))
            composer.objects.Add(vo);

        Debug.Log($"[VolumeModel] Added {shape} ({role}), total={composer.objects.Count}");
        RebuildModel();
    }

    public void RemoveLastObject()
    {
        Transform root = ObjectsRoot;
        if (root == null || root.childCount == 0) return;

        GameObject lastChild = root.GetChild(root.childCount - 1).gameObject;
        VolumeSceneComposer composer = GetComponent<VolumeSceneComposer>();
        if (composer != null)
        {
            VolumeObject vo = lastChild.GetComponent<VolumeObject>();
            if (vo != null) composer.objects.Remove(vo);
        }

        Object.DestroyImmediate(lastChild);
        RebuildModel();
    }

    public void ClearObjects()
    {
        Transform root = transform.Find("Objects");
        if (root != null)
            for (int i = root.childCount - 1; i >= 0; i--)
                Object.DestroyImmediate(root.GetChild(i).gameObject);

        VolumeSceneComposer composer = GetComponent<VolumeSceneComposer>();
        if (composer != null) composer.objects.Clear();

        RebuildModel();
    }

    public void RebuildModel()
    {
        double start = Time.realtimeSinceStartup * 1000.0;

        if (!_initialized) Initialize();
        _buildVersion++;
        _hasDirtyBounds = false;

        Debug.Log($"[VolumeModel] RebuildModel called");

        if (!enablePipeline || _pipeline == null) return;

        VolumeSceneComposer composer = GetComponent<VolumeSceneComposer>();
        if (composer == null) return;

        composer.RebuildComposition();
        if (composer.objects.Count == 0)
        {
            Debug.LogWarning("[VolumeModel] RebuildModel: no objects");
            return;
        }

        _pipeline.Rebuild(composer, isoLevel);
        DrainSync();

        double elapsed = (Time.realtimeSinceStartup * 1000.0) - start;
        Debug.Log($"[VolumeModel] RebuildModel done: {elapsed:F0}ms");
    }

    public void MarkDirtyBounds(Bounds worldBounds)
    {
        _hasDirtyBounds = true;
        _dirtyBoundsWorld = worldBounds;

        if (enablePipeline && _pipeline != null)
            _pipeline.MarkDirty();
    }

    /// <summary>Synchronous partial rebuild using current dirty bounds.</summary>
    public void RebuildDirty()
    {
        if (!_initialized) Initialize();
        if (_pipeline == null || !enablePipeline) return;

        VolumeSceneComposer composer = GetComponent<VolumeSceneComposer>();
        if (composer == null) return;

        composer.RebuildComposition();

        if (composer.objects.Count == 0)
        {
            _hasDirtyBounds = false;
            return;
        }

        if (_hasDirtyBounds)
            _pipeline.Rebuild(composer, isoLevel, _dirtyBoundsWorld);
        else
            _pipeline.Rebuild(composer, isoLevel);

        _hasDirtyBounds = false;
        DrainSync();

        Debug.Log($"[VolumeModel] RebuildDirty done");
    }

    /// <summary>Drain all pending meshing synchronously (bypasses frame budget).</summary>
    private void DrainSync()
    {
        _pipeline.Scheduler.MaxChunksPerFrame = int.MaxValue;
        _pipeline.Scheduler.UseTimeBudget = false;
        _pipeline.TickScheduler();
        _pipeline.Scheduler.UseTimeBudget = true;
    }

    public VolumePipeline Pipeline => _pipeline;
    public bool Initialized => _initialized;
    public int BuildVersion => _buildVersion;

    // ---- Legacy stubs ----
    public bool ShouldUseInteractionPreview() => false;
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
        Bounds volBounds = new Bounds(transform.position, Vector3.one * boundsExtent);
        Gizmos.color = new Color(0.5f, 0.8f, 1f, 0.12f);
        Gizmos.DrawCube(volBounds.center, volBounds.size);
        Gizmos.color = new Color(0.5f, 0.8f, 1f, 0.6f);
        Gizmos.DrawWireCube(volBounds.center, volBounds.size);

        Vector3 origin = volBounds.min;
        Gizmos.color = new Color(1f, 0.85f, 0f, 0.9f);
        Gizmos.DrawWireSphere(origin, boundsExtent * 0.04f);
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
        if (_pipeline == null || !enablePipeline) return;

        // Model origin change in editor — same as Update() path.
        CheckModelTransformChanged();

        // Budgeted ticks in editor — 8 chunks or 5ms per editor update frame
        _pipeline.Scheduler.MaxChunksPerFrame = 8;
        _pipeline.Scheduler.UseTimeBudget = true;

        if (_pipeline.Scheduler.HasPendingWork)
            _pipeline.TickScheduler();

        if (_pipeline.IsDirty && !_pipeline.Scheduler.HasPendingWork && !_pipeline.DirtyChunks.HasPendingWork)
            RebuildPipeline();

        SceneView.RepaintAll();
    }
#endif

    private void OnDestroy() => Dispose();
}
