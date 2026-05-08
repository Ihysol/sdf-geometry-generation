using System;
using UnityEngine;

public class VolumeSampler : MonoBehaviour
{

    public enum VolumeSourceMode
    {
        SdfAsset,
        RuntimeSource
    }

    public event Action Changed;

    [Header("Source")]
    public VolumeSourceMode sourceMode = VolumeSourceMode.SdfAsset;
    public SDF sdf;
    private SDF _lastSdf;

    [Header("Volume")]
    public VoxelGridBuilder builder = new VoxelGridBuilder();

    public bool uniformExtent = true;
    public bool uniformResolution = true;

    private Vector3 _lastGridExtent;
    private Vector3Int _lastGridSize;

    public VoxelGrid Volume { get; private set; }
    public bool IsDirty { get; private set; } = true;

    private IScalarFieldSource _runtimeSource;

    public void SetRuntimeSource(IScalarFieldSource runtimeSource)
    {
        _runtimeSource = runtimeSource;
        MarkDirty();
    }

    public void ClearRuntimeSource()
    {
        _runtimeSource = null;
        MarkDirty();
    }

    private IScalarFieldSource GetActiveSource()
    {
        if (sdf != null)
            return sdf;

        return _runtimeSource;
    }

    public void MarkDirty()
    {
        IsDirty = true;
        Changed?.Invoke();
    }

    public void RebuildVolume()
    {
        IScalarFieldSource source = GetActiveSource();

        if (source == null)
        {
            Volume = null;
            IsDirty = false;
            return;
        }

        Volume = builder.Build(source);
        IsDirty = false;
    }

    public float EvaluateLocal(Vector3 p)
    {
        IScalarFieldSource source = GetActiveSource();
        return source != null ? source.Evaluate(p) : 1f;
    }

    public Vector3 EstimateNormalLocal(Vector3 p)
    {
        float minExtent = Mathf.Min(
            builder.gridExtent.x,
            builder.gridExtent.y,
            builder.gridExtent.z
        );

        float h = Mathf.Max(0.0001f, minExtent / 512f);

        float dx = EvaluateLocal(p + new Vector3(h, 0f, 0f)) - EvaluateLocal(p - new Vector3(h, 0f, 0f));
        float dy = EvaluateLocal(p + new Vector3(0f, h, 0f)) - EvaluateLocal(p - new Vector3(0f, h, 0f));
        float dz = EvaluateLocal(p + new Vector3(0f, 0f, h)) - EvaluateLocal(p - new Vector3(0f, 0f, h));

        Vector3 n = new Vector3(dx, dy, dz);
        return n.sqrMagnitude < 1e-8f ? Vector3.up : n.normalized;
    }

    private void Awake()
    {
        RebuildVolume();
    }

    private void OnEnable()
    {
        SubscribeSdf();
    }

    private void OnDisable()
    {
        UnsubscribeSdf();
    }

    private void OnValidate()
    {
        ValidateBuilder();
        SubscribeSdf();
        MarkDirty();
    }

    private void ValidateBuilder()
    {
        builder.gridExtent.x = Mathf.Max(0.0001f, builder.gridExtent.x);
        builder.gridExtent.y = Mathf.Max(0.0001f, builder.gridExtent.y);
        builder.gridExtent.z = Mathf.Max(0.0001f, builder.gridExtent.z);

        builder.gridSize.x = Mathf.Max(2, builder.gridSize.x);
        builder.gridSize.y = Mathf.Max(2, builder.gridSize.y);
        builder.gridSize.z = Mathf.Max(2, builder.gridSize.z);

        if (uniformExtent)
        {
            float value = GetChangedComponent(_lastGridExtent, builder.gridExtent);
            builder.gridExtent = new Vector3(value, value, value);
        }

        if (uniformResolution)
        {
            int value = GetChangedComponent(_lastGridSize, builder.gridSize);
            builder.gridSize = new Vector3Int(value, value, value);
        }

        _lastGridExtent = builder.gridExtent;
        _lastGridSize = builder.gridSize;
    }

    private float GetChangedComponent(Vector3 oldVal, Vector3 newVal)
    {
        if (!Mathf.Approximately(oldVal.x, newVal.x)) return newVal.x;
        if (!Mathf.Approximately(oldVal.y, newVal.y)) return newVal.y;
        if (!Mathf.Approximately(oldVal.z, newVal.z)) return newVal.z;

        return newVal.x;
    }

    private int GetChangedComponent(Vector3Int oldVal, Vector3Int newVal)
    {
        if (oldVal.x != newVal.x) return newVal.x;
        if (oldVal.y != newVal.y) return newVal.y;
        if (oldVal.z != newVal.z) return newVal.z;

        return newVal.x;
    }

    private void SubscribeSdf()
    {
        if (_lastSdf == sdf)
            return;

        if (_lastSdf != null)
            _lastSdf.Changed -= MarkDirty;

        if (sdf != null)
            sdf.Changed += MarkDirty;

        _lastSdf = sdf;
        MarkDirty();
    }

    private void UnsubscribeSdf()
    {
        if (_lastSdf != null)
            _lastSdf.Changed -= MarkDirty;

        _lastSdf = null;
    }

    private void OnDrawGizmos()
    {
        DrawBounds(false);
    }

    private void OnDrawGizmosSelected()
    {
        DrawBounds(true);
    }

    private void DrawBounds(bool selected)
    {
        Vector3 extent = builder.gridExtent;

        Gizmos.matrix = transform.localToWorldMatrix;

        Gizmos.color = selected
            ? new Color(0f, 1f, 1f, 1f)
            : new Color(0f, 1f, 1f, 0.35f);

        Gizmos.DrawWireCube(Vector3.zero, extent);

        Gizmos.matrix = Matrix4x4.identity;
    }
}