using UnityEngine;

public class SDFSampler : MonoBehaviour
{
    [Header("SDF")]
    public SDF sdf;
    private SDF _lastSdf;

    [Header("Volume")]
    public bool uniformExtent = true;
    public Vector3 gridExtent = new Vector3(4f, 4f, 4f);
    private Vector3 _lastGridExtent;
    public bool uniformResolution = true;
    public Vector3Int gridSize = new Vector3Int(32, 32, 32);
    private Vector3Int _lastGridSize;

    public VoxelGrid Volume { get; private set; }
    public bool IsDirty { get; private set; } = true;

    private ISDF _runtimesdf;

    public void SetRuntimeSDF(ISDF runtimesdf)
    {
        _runtimesdf = runtimesdf;
        MarkDirty();
    }

    private float EvaluateRoot(Vector3 p)
    {
        if (_runtimesdf != null)
            return _runtimesdf.Evaluate(p);

        if (sdf != null)
            return sdf.Evaluate(p);

        return 1f;
    }

    private void Awake()
    {
        RebuildVolume();
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

    private void OnValidate()
    {
        SubscribeSdf();

        gridExtent.x = Mathf.Max(0.0001f, gridExtent.x);
        gridExtent.y = Mathf.Max(0.0001f, gridExtent.y);
        gridExtent.z = Mathf.Max(0.0001f, gridExtent.z);

        gridSize.x = Mathf.Max(2, gridSize.x);
        gridSize.y = Mathf.Max(2, gridSize.y);
        gridSize.z = Mathf.Max(2, gridSize.z);

        if (uniformExtent)
        {
            float value = GetChangedComponent(_lastGridExtent, gridExtent);
            gridExtent = new Vector3(value, value, value);
        }

        if (uniformResolution)
        {
            int value = GetChangedComponent(_lastGridSize, gridSize);
            gridSize = new Vector3Int(value, value, value);
        }

        _lastGridExtent = gridExtent;
        _lastGridSize = gridSize;

        MarkDirty();
    }

    public void MarkDirty()
    {
        IsDirty = true;
    }

    public void RebuildVolume()
    {
        if (sdf == null && _runtimesdf == null)
        {
            Volume = null;
            IsDirty = false;
            return;
        }

        Vector3 origin = -gridExtent * 0.5f;

        Vector3 cellSize = new Vector3(
            gridExtent.x / (gridSize.x - 1),
            gridExtent.y / (gridSize.y - 1),
            gridExtent.z / (gridSize.z - 1)
        );

        Volume = new VoxelGrid(origin, cellSize, gridSize);

        float[] distances = Volume.Distances;

        int sizeX = Volume.GridSize.x;
        int sizeY = Volume.GridSize.y;
        int sizeZ = Volume.GridSize.z;

        for (int z = 0; z < sizeZ; z++)
        {
            float pz = origin.z + z * cellSize.z;

            for (int y = 0; y < sizeY; y++)
            {
                float py = origin.y + y * cellSize.y;

                int rowBase = sizeX * (y + sizeY * z);

                for (int x = 0; x < sizeX; x++)
                {
                    float px = origin.x + x * cellSize.x;

                    Vector3 p = new Vector3(px, py, pz);

                    int index = x + rowBase;
                    distances[index] = EvaluateRoot(p);
                }
            }
        }

        IsDirty = false;
    }


    public float EvaluateLocal(Vector3 p)
    {
        return EvaluateRoot(p);
    }

    public Vector3 EstimateNormalLocal(Vector3 p)
    {
        float h = Mathf.Max(0.0001f, Mathf.Min(gridExtent.x, gridExtent.y, gridExtent.z) / 512f);
        float dx = EvaluateLocal(p + new Vector3(h, 0f, 0f)) - EvaluateLocal(p - new Vector3(h, 0f, 0f));
        float dy = EvaluateLocal(p + new Vector3(0f, h, 0f)) - EvaluateLocal(p - new Vector3(0f, h, 0f));
        float dz = EvaluateLocal(p + new Vector3(0f, 0f, h)) - EvaluateLocal(p - new Vector3(0f, 0f, h));

        Vector3 n = new Vector3(dx, dy, dz);

        if (n.sqrMagnitude < 1e-8f)
            return Vector3.up;
        return n.normalized;

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

    private void OnEnable()
    {
        SubscribeSdf();
    }

    private void OnDisable()
    {
        UnsubscribeSdf();
    }
}