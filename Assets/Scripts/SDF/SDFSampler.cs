using System.Runtime.CompilerServices;
using Unity.VectorGraphics;
using UnityEngine;

public class SDFSampler : MonoBehaviour
{
    public enum ShapeMode
    {
        Sphere,
        Box
    }

    [Header("Shape")]
    public ShapeMode shapeMode = ShapeMode.Sphere;
    public float sphereRadius = 1.5f;
    public Vector3 boxHalfExtents = new Vector3(1f, 1f, 1f);

    [Header("Grid")]
    public bool useAutomaticBounds = true;
    public Vector3 gridExtent = new Vector3(4f, 4f, 4f);
    public float boundsPadding = 0.2f;
    public int resolution = 10;

    public SDFSample[] Samples { get; private set; }
    public Vector3Int GridSize { get; private set; }
    public float CellSize { get; private set; }

    private ISDF _sdf;
    public bool IsDirty { get; private set; } = true;

    public void MarkDirty()
    {
        IsDirty = true;
    }

    // evaluate SDF in local space
    public float EvaluateLocal(Vector3 localPos)
    {
        if (_sdf == null)
            BuildSDF();

        return _sdf.Evaluate(localPos);
    }

    // approximate normal via central differences
    public Vector3 EstimateNormalLocal(Vector3 localPos)
    {
        float e = CellSize * 0.5f;
        if (e <= 0f)
            e = 0.001f;

        float dx = EvaluateLocal(localPos + new Vector3(e, 0f, 0f)) - EvaluateLocal(localPos - new Vector3(e, 0f, 0f));
        float dy = EvaluateLocal(localPos + new Vector3(0f, e, 0f)) - EvaluateLocal(localPos - new Vector3(0f, e, 0f));
        float dz = EvaluateLocal(localPos + new Vector3(0f, 0f, e)) - EvaluateLocal(localPos - new Vector3(0f, 0f, e));

        Vector3 n = new Vector3(dx, dy, dz);
        return n.sqrMagnitude < 1e-6f ? Vector3.up : n.normalized;
    }

    public void RebuildSamples()
    {
        BuildSDF();

        Vector3 effectiveGridExtent = GetEffectiveGridExtent();

        CellSize = 1f / resolution;

        GridSize = new Vector3Int(
            Mathf.CeilToInt(effectiveGridExtent.x * resolution),
            Mathf.CeilToInt(effectiveGridExtent.y * resolution),
            Mathf.CeilToInt(effectiveGridExtent.z * resolution)
        );

        int totalCount = GridSize.x * GridSize.y * GridSize.z;
        Samples = new SDFSample[totalCount];

        Vector3 origin = transform.position - effectiveGridExtent * 0.5f;

        for (int x = 0; x < GridSize.x; x++)
        {
            for (int y = 0; y < GridSize.y; y++)
            {
                for (int z = 0; z < GridSize.z; z++)
                {
                    int index = GetIndex(x, y, z);

                    Vector3 worldPos = origin + new Vector3(x, y, z) * CellSize;
                    Vector3 localPos = transform.InverseTransformPoint(worldPos);
                    float distance = _sdf.Evaluate(localPos);

                    Samples[index] = new SDFSample
                    {
                        WorldPosition = worldPos,
                        LocalPosition = localPos,
                        Distance = distance
                    };
                }
            }
        }
        IsDirty = false;
    }

    private void BuildSDF()
    {
        switch (shapeMode)
        {
            case ShapeMode.Box:
                _sdf = new BoxSDF(Vector3.zero, boxHalfExtents);
                break;

            case ShapeMode.Sphere:
            default:
                _sdf = new SphereSDF(Vector3.zero, sphereRadius);
                break;
        }
    }

    public int GetIndex(int x, int y, int z)
    {
        return x + GridSize.x * (y + GridSize.y * z);
    }

    public bool IsValidCoordinate(int x, int y, int z)
    {
        return x >= 0 && x < GridSize.x &&
               y >= 0 && y < GridSize.y &&
               z >= 0 && z < GridSize.z;
    }

    public SDFSample GetSample(int x, int y, int z)
    {
        return Samples[GetIndex(x, y, z)];
    }
    private void OnValidate()
    {
        if (resolution < 1)
            resolution = 1;

        MarkDirty();
    }

    private Vector3 GetEffectiveGridExtent()
    {
        if (!useAutomaticBounds)
            return gridExtent;

        switch (shapeMode)
        {
            case ShapeMode.Box:
                return (boxHalfExtents + Vector3.one * boundsPadding) * 2f;

            case ShapeMode.Sphere:
            default:
                float r = sphereRadius + boundsPadding;
                return new Vector3(r * 2f, r * 2f, r * 2f);
        }
    }

    private void Update()
    {
        if (transform.hasChanged)
        {
            MarkDirty();
            transform.hasChanged = false;
        }
    }
}

