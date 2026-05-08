using Unity.VisualScripting;
using UnityEngine;

public enum VolumeMesherType
{
    DualContouring
}

[ExecuteAlways]
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
    private IVolumeMesher<VoxelGrid> _mesher;

    private float _lastIsoLevel;
    private VolumeMesherType _lastMesherType;

    private void Awake()
    {
        EnsureReferences();
        EnsureMesher();
    }

    private void OnSamplerChanged()
    {
        if (autoRebuildOnChange)
            QueueRebuild();
    }

    private void OnEnable()
    {
        EnsureReferences();
        EnsureMesher();

        if (_sampler != null)
            _sampler.Changed += OnSamplerChanged;
    }

    private void OnDisable()
    {
        if (_sampler != null)
            _sampler.Changed -= OnSamplerChanged;
    }

    private void OnValidate()
    {
        if (!autoRebuildOnChange)
            return;

        QueueRebuild();
    }

    private void Update()
    {
        if (rebuildEveryFrame)
        {
            RebuildMesh();
            return;
        }

        if (autoRebuildOnChange && IsDirty())
            RebuildMesh();
    }

    private void QueueRebuild()
    {
#if UNITY_EDITOR
        if (!Application.isPlaying)
        {
            UnityEditor.EditorApplication.delayCall -= DelayedRebuild;
            UnityEditor.EditorApplication.delayCall += DelayedRebuild;
            return;
        }
#endif

        RebuildMesh();
    }

#if UNITY_EDITOR
    private void DelayedRebuild()
    {
        if (this == null)
            return;

        RebuildMesh();
    }
#endif


    private void EnsureReferences()
    {
        if (_sampler == null)
            _sampler = GetComponent<VolumeSampler>();

        if (_meshFilter == null)
            _meshFilter = GetComponent<MeshFilter>();
    }

    private void EnsureMesher()
    {
        if (_mesher != null && _lastMesherType == mesherType)
            return;

        switch (mesherType)
        {
            case VolumeMesherType.DualContouring:
            default:
                _mesher = new DualContouringVoxelMesher();
                break;
        }

        _lastMesherType = mesherType;
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
        EnsureMesher();


        if (_sampler == null || _meshFilter == null || _mesher == null)
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

        MeshData meshData = _mesher.BuildMeshData(_sampler.Volume, isoLevel);
        Mesh mesh = meshData.ToMesh(recalculateNormals);

        _meshFilter.sharedMesh = mesh;

        CacheState();
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