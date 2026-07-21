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
    private BoundsInt _dirtyBounds = new BoundsInt(Vector3Int.zero, Vector3Int.one);
    private bool _hasDirtyBounds;

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

    // ---- Init ----

    public void Initialize()
    {
        if (_initialized) return;
        _initialized = true;
        if (enablePipeline) InitializePipeline();
    }

    private void InitializePipeline()
    {
        var oldRenderer = GetComponent<VolumeMeshRenderer>();
        if (oldRenderer != null)
            oldRenderer.enabled = false;

        if (surfaceMaterial == null)
            surfaceMaterial = new Material(Shader.Find("Standard"));

        // Volume grid centered on THIS transform's world position — ensures child objects at localPosition=0
        // live inside the volume bounds.
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

        // Create per-chunk renderers
        _chunksParent = new GameObject("Chunks").transform;
        _chunksParent.SetParent(transform, false);

        Vector3Int gridSize = _pipeline.Buffer.ChunkGridSize;
        _chunkRenderers = new ChunkRenderManager();
        _chunkRenderers.Initialize(_pipeline.Buffer.TotalChunks, gridSize, _chunksParent, layout);
        _chunkRenderers.SetMaterial(surfaceMaterial);
        _pipeline.SetChunkRenderers(_chunkRenderers);

        // Destroy single-mesh output object — chunk renderers take over
        GameObject.DestroyImmediate(meshObj);
        _meshOutput = null;

        Debug.Log($"[VolumeModel] Pipeline initialized: grid at {bounds.min:F1} to {bounds.max:F1}, center={transform.position:F1}");
    }

    private void Update()
    {
        if (_pipeline != null && enablePipeline)
        {
            _pipeline.Scheduler.MaxChunksPerFrame = 8;

            if (_pipeline.Scheduler.HasPendingWork) _pipeline.TickScheduler();

            if (_pipeline.IsDirty && !_pipeline.Scheduler.HasPendingWork && !_pipeline.DirtyChunks.HasPendingWork)
                RebuildPipeline();
        }

        // Sync material on chunk renderers
        if (_chunkRenderers != null && surfaceMaterial != null)
            _chunkRenderers.SetMaterial(surfaceMaterial);
    }

    private void RebuildPipeline()
     {
         VolumeSceneComposer composer = GetComponent<VolumeSceneComposer>();
         if (composer == null || _pipeline == null) return;

         composer.RebuildComposition();

         if (composer.objects.Count == 0)
         {
             Debug.LogWarning("[VolumeModel] RebuildPipeline: no objects in scene — nothing to mesh. Add a shape first.");
             return;
         }

         if (_hasDirtyBounds)
         {
             _pipeline.Rebuild(composer, isoLevel, new Bounds((Vector3)_dirtyBounds.center, _dirtyBounds.size));
         }
         else
         {
             _pipeline.Rebuild(composer, isoLevel);
         }
         // Always clear dirty bounds after rebuild — prevents stale partial-rebuild path
         // from firing on a subsequent RebuildPipeline() call with outdated bounds.
         _hasDirtyBounds = false;

         // Per v10 architecture §3.1 "Initialer Aufbau": initial build is synchronous.
         _pipeline.Scheduler.MaxChunksPerFrame = int.MaxValue;
         _pipeline.Scheduler.UseTimeBudget = false;
         int drained = _pipeline.TickScheduler();
         _pipeline.Scheduler.UseTimeBudget = true;

         Debug.Log($"[VolumeModel] RebuildPipeline done: drained {drained} chunks, IsDirty={_pipeline.IsDirty}, Scheduler pending={_pipeline.Scheduler.PendingCount}");
     }

    // ---- Public API ----

    public void Rebuild()
    {
        if (!_initialized) Initialize();
        _buildVersion++;
        RebuildPipeline();
    }

    public void Dispose()
    {
        if (_chunkRenderers != null)
        {
            _chunkRenderers.Dispose();
            _chunkRenderers = null;
        }
        if (_pipeline != null)
        {
            _pipeline.Dispose();
            _pipeline = null;
        }
        if (_meshOutput != null)
            _meshOutput.Clear();
        _initialized = false;
    }

    public void TickScheduler()
    {
        if (_pipeline != null)
            _pipeline.TickScheduler();
    }

    public void ExecuteOperation(IVolumeOperation operation)
    {
        if (!_initialized) Initialize();
        if (_pipeline != null)
            _pipeline.ApplyOperation(operation);
    }

    // ---- Object Management ----

    public void AddSelectedObject() => AddObject(shapeToAdd, roleToAdd);

    public void AddObject(VolumeShapeType shape, VolumeOperationRole role)
    {
        GameObject child = new GameObject($"VolumeObject_{shape}_{role}");
        child.transform.SetParent(ObjectsRoot, false);
        child.transform.localPosition = Vector3.zero;

        VolumeObject volumeObject = child.AddComponent<VolumeObject>();
        volumeObject.shapeType = shape;
        volumeObject.role = role;

        VolumeSceneComposer composer = GetComponent<VolumeSceneComposer>();
        if (composer != null && !composer.objects.Contains(volumeObject))
            composer.objects.Add(volumeObject);

        Debug.Log($"[VolumeModel] Added {shape} ({role}) at center, total objects={composer.objects.Count}");

        RebuildModel();
    }

    public void RemoveLastObject()
    {
        Transform root = ObjectsRoot;
        if (root == null || root.childCount == 0) return;

        int last = root.childCount - 1;
        GameObject lastChild = root.GetChild(last).gameObject;

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
        {
            for (int i = root.childCount - 1; i >= 0; i--)
                Object.DestroyImmediate(root.GetChild(i).gameObject);
        }

        VolumeSceneComposer composer = GetComponent<VolumeSceneComposer>();
        if (composer != null) composer.objects.Clear();

        RebuildModel();
    }

    public void RebuildModel()
     {
         double rebuildStart = Time.realtimeSinceStartup * 1000.0;

         if (!_initialized) Initialize();
         _buildVersion++;
         // Clear dirty bounds before full rebuild — prevents RebuildPipeline() from
         // taking the partial path with stale (and possibly out-of-bounds) dirty region.
         _hasDirtyBounds = false;

         Debug.Log($"[VolumeModel] RebuildModel called, initialized={_initialized}, enablePipeline={enablePipeline}, pipeline={(_pipeline != null)}");

         if (enablePipeline && _pipeline != null)
         {
             VolumeSceneComposer composer = GetComponent<VolumeSceneComposer>();
             if (composer != null)
             {
                 composer.RebuildComposition();
                 if (composer.objects.Count == 0)
                 {
                     Debug.LogWarning("[VolumeModel] RebuildModel: no objects in scene — nothing to mesh. Add a shape first.");
                     return;
                 }
                 _pipeline.Rebuild(composer, isoLevel);

                 // Per v10 architecture §3.1 "Initialer Aufbau": initial build is synchronous.
                 _pipeline.Scheduler.MaxChunksPerFrame = int.MaxValue;
                 _pipeline.Scheduler.UseTimeBudget = false;
                 int drained = _pipeline.TickScheduler();
                 _pipeline.Scheduler.UseTimeBudget = true;

                 double elapsed = (Time.realtimeSinceStartup * 1000.0) - rebuildStart;
                 Debug.Log($"[VolumeModel] RebuildModel done: drained {drained} chunks in {elapsed:F0}ms");
             }
             else
             {
                 Debug.LogError("[VolumeModel] RebuildModel: VolumeSceneComposer is null!");
             }
         }
     }

    public void MarkDirtyBounds(Bounds bounds)
    {
        _hasDirtyBounds = true;
        _dirtyBounds.position = new Vector3Int(
            Mathf.FloorToInt(bounds.min.x),
            Mathf.FloorToInt(bounds.min.y),
            Mathf.FloorToInt(bounds.min.z));
        _dirtyBounds.size = new Vector3Int(
            Mathf.CeilToInt(bounds.size.x),
            Mathf.CeilToInt(bounds.size.y),
            Mathf.CeilToInt(bounds.size.z));

        if (enablePipeline && _pipeline != null)
            _pipeline.MarkDirty();
    }

    // ---- Accessors ----

    public VolumePipeline Pipeline => _pipeline;
    public bool Initialized => _initialized;
    public int BuildVersion => _buildVersion;

    // ---- Legacy stubs (for external code that won't execute with pipeline) ----
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
    public bool drawChildGizmos => false;

    private VoxelGridSampler _voxelStub;
    private VoxelGridSampler CreateVoxelGridSampler()
    {
        if (_voxelStub == null)
        {
            _voxelStub = new VoxelGridSampler();
            // Match pipeline resolution so EstimateMinSamplingCellSize returns correct value
            int res = Mathf.Max(1, resolution.x);
            _voxelStub.builder.gridSize = new Vector3Int(res, res, res);
            _voxelStub.builder.gridExtent = new Vector3(boundsExtent, boundsExtent, boundsExtent);
        }
        return _voxelStub;
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0.5f, 0.8f, 1f, 0.2f);
        Bounds gizmoBounds = new Bounds(transform.position, Vector3.one * boundsExtent);
        Gizmos.DrawCube(gizmoBounds.center, gizmoBounds.size);

        Gizmos.color = new Color(0.5f, 0.8f, 1f, 0.6f);
        Gizmos.DrawWireCube(gizmoBounds.center, gizmoBounds.size);

        Transform objectsRoot = transform.Find("Objects");
        if (objectsRoot != null && objectsRoot.childCount > 0)
        {
            Bounds childBounds = new Bounds(objectsRoot.GetChild(0).position, Vector3.zero);
            for (int i = 0; i < objectsRoot.childCount; i++)
                childBounds.Encapsulate(objectsRoot.GetChild(i).position);

            Gizmos.color = new Color(1f, 0.6f, 0.2f, 0.4f);
            Gizmos.DrawWireCube(childBounds.center, childBounds.size);
        }

        if (_meshRenderer != null)
        {
            Gizmos.color = Color.green;
            Gizmos.DrawWireSphere(_meshRenderer.transform.position, 0.05f);
        }
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

        // Edit mode: drain all remaining chunks per tick (no framerate concern)
        _pipeline.Scheduler.MaxChunksPerFrame = int.MaxValue;
        _pipeline.Scheduler.UseTimeBudget = false;

        if (_pipeline.Scheduler.HasPendingWork) _pipeline.TickScheduler();
        if (_pipeline.IsDirty && !_pipeline.Scheduler.HasPendingWork && !_pipeline.DirtyChunks.HasPendingWork)
            RebuildPipeline();

        SceneView.RepaintAll();
    }
#endif

    private void OnDestroy() => Dispose();
}
