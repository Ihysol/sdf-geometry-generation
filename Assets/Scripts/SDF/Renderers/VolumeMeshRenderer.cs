using UnityEngine;

public enum VolumeMesherType
{
    DualContouring
}

[RequireComponent(typeof(VolumeSampler))]
[RequireComponent(typeof(MeshFilter))]
[RequireComponent(typeof(MeshRenderer))]
public class VolumeMeshRenderer : MonoBehaviour
{
    [Header("Mesher")]
    public VolumeMesherType mesherType = VolumeMesherType.DualContouring;

    [Header("Iso Surface")]
    public float isoLevel = 0f;

    [Header("Rebuild")]
    public bool rebuildEveryFrame = false;
    public bool autoRebuildOnChange = true;
    public bool recalculateNormals = true;

    private VolumeSampler _sampler;
    private MeshFilter _meshFilter;

    private readonly DualContouringVoxelMesher _dualContouringMesher = new();
    private IVolumeMesher<VoxelGrid> _mesher;

    private float _lastIsoLevel;
    private VolumeMesherType _lastMesherType;

    private void Awake()
    {
        EnsureReferences();
    }

    private void OnEnable()
    {
        EnsureReferences();
    }

    private void Update()
    {
        if (rebuildEveryFrame || (autoRebuildOnChange && IsDirty()))
            RebuildMesh();
    }

    private void EnsureReferences()
    {
        if (_sampler == null)
            _sampler = GetComponent<VolumeSampler>();

        if (_meshFilter == null)
            _meshFilter = GetComponent<MeshFilter>();
    }

    private bool IsDirty()
    {
        EnsureReferences();

        return _sampler == null
            || _sampler.IsDirty
            || _sampler.Volume == null
            || !Mathf.Approximately(_lastIsoLevel, isoLevel)
            || _lastMesherType != mesherType;
    }

    [ContextMenu("Rebuild Mesh")]
    public void RebuildMesh()
    {
        EnsureReferences();

        if (_sampler == null || _meshFilter == null)
            return;

        if (_sampler.IsDirty || _sampler.Volume == null)
            _sampler.RebuildVolume();

        if (_sampler.Volume == null)
        {
            DestroyMesh();
            CacheState();
            return;
        }

        DestroyMesh();

        Mesh mesh = BuildMesh(_sampler.Volume);
        _meshFilter.sharedMesh = mesh;

        CacheState();
    }

    private Mesh BuildMesh(VoxelGrid volume)
    {
        MeshData meshData;

        switch (mesherType)
        {
            case VolumeMesherType.DualContouring:
            default:
                meshData = _dualContouringMesher.BuildMeshData(volume, isoLevel);
                break;
        }

        return meshData.ToMesh(recalculateNormals);
    }

    private void CacheState()
    {
        _lastIsoLevel = isoLevel;
        _lastMesherType = mesherType;
    }

    public void DestroyMesh()
    {
        EnsureReferences();

        if (_meshFilter == null || _meshFilter.sharedMesh == null)
            return;

#if UNITY_EDITOR
        if (!Application.isPlaying)
            DestroyImmediate(_meshFilter.sharedMesh);
        else
#endif
            Destroy(_meshFilter.sharedMesh);

        _meshFilter.sharedMesh = null;
    }
}