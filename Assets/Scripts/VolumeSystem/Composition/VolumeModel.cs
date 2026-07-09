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
    [SerializeField] public float isoLevel = 0.5f;
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

        Bounds bounds = new Bounds(Vector3.zero, Vector3.one * boundsExtent);
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
    }

    private void Update()
    {
        if (_pipeline != null && enablePipeline)
        {
            if (_pipeline.IsDirty && !_pipeline.Scheduler.HasPendingWork) RebuildPipeline();
            if (_pipeline.Scheduler.HasPendingWork) _pipeline.TickScheduler();
        }

        if (_meshRenderer != null && surfaceMaterial != null)
        {
            if (_meshRenderer.sharedMaterial != surfaceMaterial)
                _meshRenderer.sharedMaterial = surfaceMaterial;
        }
    }

    private void RebuildPipeline()
    {
        VolumeSceneComposer composer = GetComponent<VolumeSceneComposer>();
        if (composer == null || _pipeline == null) return;

        composer.RebuildComposition();
        _pipeline.Rebuild(composer, isoLevel);

        if (_meshOutput != null && surfaceMaterial != null)
            _meshOutput.SetMaterial(surfaceMaterial);
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
        VolumeObject volumeObject = child.AddComponent<VolumeObject>();
        volumeObject.shapeType = shape;
        volumeObject.role = role;

        VolumeSceneComposer composer = GetComponent<VolumeSceneComposer>();
        if (composer != null && !composer.objects.Contains(volumeObject))
            composer.objects.Add(volumeObject);

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
        if (!_initialized) Initialize();
        _buildVersion++;

        if (enablePipeline && _pipeline != null)
        {
            VolumeSceneComposer composer = GetComponent<VolumeSceneComposer>();
            if (composer != null)
            {
                composer.RebuildComposition();
                _pipeline.Rebuild(composer, isoLevel);
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
        Bounds gizmoBounds = new Bounds(Vector3.zero, Vector3.one * boundsExtent);
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

        if (_pipeline.IsDirty && !_pipeline.Scheduler.HasPendingWork) RebuildPipeline();
        if (_pipeline.Scheduler.HasPendingWork) _pipeline.TickScheduler();

        SceneView.RepaintAll();
    }
#endif

    private void OnDestroy() => Dispose();
}
