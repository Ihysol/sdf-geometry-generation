using UnityEngine;

/// <summary>Connects the existing VolumeProcessor to the new modular pipeline.</summary>
public class VolumePipelineAdapter : MonoBehaviour
{
    [Header("Pipeline")]
    public bool enablePipeline = false;
    public PipelineMesherType mesherType = PipelineMesherType.Voxel;
    public ComputeBackend computeBackend = ComputeBackend.CPU;
    public OutputMode outputMode = OutputMode.UnityMesh;

    [Header("Layout")]
    public Vector3Int resolution = new Vector3Int(64, 64, 64);
    public int chunkSize = 16;
    public float isoLevel = 0f;

    private VolumePipeline _pipeline;
    private ProceduralDrawOutput _proceduralOutput;
    private bool _needsRebuild;

    private void Start()
    {
        if (!enablePipeline) return;

        var model = GetComponent<VolumeProcessor>();
        if (model == null)
        {
            Debug.LogError("[VolumePipelineAdapter] VolumeProcessor component required.");
            enabled = false;
            return;
        }

        Bounds bounds = GetBounds(model);
        VolumeLayout layout = new VolumeLayout
        {
            Resolution = resolution,
            CellSize = bounds.size.x / resolution.x,
            Origin = bounds.min,
            ChunkSize = chunkSize,
            IsoLevel = isoLevel
        };

        IVolumeMesher mesher = MesherFactory.Create(mesherType);

        switch (outputMode)
        {
            case OutputMode.ProceduralDraw:
                _proceduralOutput = new ProceduralDrawOutput(model.surfaceMaterial, Camera.main);
                break;
            default:
                _proceduralOutput = null;
                break;
        }

        Mesh outputMesh = new Mesh();
        outputMesh.name = "PipelineMesh";
        IVolumeOutput output;
        if (_proceduralOutput != null)
            output = _proceduralOutput;
        else
            output = new UnityMeshOutput(outputMesh);

        _pipeline = new VolumePipeline(layout, mesher);
        _pipeline.Initialize(output);
        _pipeline.SetBackend(computeBackend);

        if (computeBackend == ComputeBackend.GPU)
            _pipeline.Buffer.EnableComputeBuffers();

        _needsRebuild = true;
    }

   private void Update()
    {
        if (_pipeline == null || !enablePipeline) return;

        var composer = GetComponent<VolumeObjectRegistry>();
        if (composer == null) return;

        if (_pipeline.IsDirty || _needsRebuild)
        {
            _pipeline.Rebuild(composer, isoLevel);
            _needsRebuild = false;
        }

        if (_pipeline.Scheduler.HasPendingWork)
        {
            if (Camera.main != null)
                _pipeline.Scheduler.CameraPosition = Camera.main.transform.position;
            _pipeline.TickScheduler();
        }

        if (_proceduralOutput != null)
            _proceduralOutput.Render();
    }

    private void OnDestroy()
    {
        if (_pipeline != null)
        {
            _pipeline.Dispose();
            _pipeline = null;
        }
    }

    /// <summary>Call to trigger a rebuild from external code.</summary>
    public void RequestRebuild()
    {
        _needsRebuild = true;
        if (_pipeline != null)
            _pipeline.MarkDirty();
    }

    private Bounds GetBounds(VolumeProcessor model)
    {
        return new Bounds(Vector3.zero, Vector3.one * 4f);
    }
}
