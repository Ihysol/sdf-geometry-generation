using System.Runtime.CompilerServices;
using Unity.VectorGraphics;
using UnityEngine;
using UnityEngine.SocialPlatforms;

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
    public Vector3Int resolution = new Vector3Int(32, 32, 32);
    public bool uniformResolution = true;

    public SDFSample[] Samples { get; private set; }
    public Vector3Int GridSize { get; private set; }
    public Vector3 CellSize { get; private set; }

    private ISDF _sdf;
    private Vector3Int _lastResolution;
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
        Vector3 h = GetNormalStep();

        float dx =
            EvaluateLocal(localPos + new Vector3(h.x, 0f, 0f)) -
            EvaluateLocal(localPos - new Vector3(h.x, 0f, 0f));

        float dy =
            EvaluateLocal(localPos + new Vector3(0f, h.y, 0f)) -
            EvaluateLocal(localPos - new Vector3(0f, h.y, 0f));

        float dz =
            EvaluateLocal(localPos + new Vector3(0f, 0f, h.z)) -
            EvaluateLocal(localPos - new Vector3(0f, 0f, h.z));

        Vector3 n = new Vector3(
            dx / (2f * h.x),
            dy / (2f * h.y),
            dz / (2f * h.z)
        );

        if (n.sqrMagnitude < 1e-12f || float.IsNaN(n.x) || float.IsNaN(n.y) || float.IsNaN(n.z))
        {
            // fallback: direction away from object center
            if (localPos.sqrMagnitude > 1e-12f)
                return localPos.normalized;

            return Vector3.up;
        }

        return n.normalized;
    }

    private Vector3 GetNormalStep()
    {
        Vector3 h = CellSize * 0.5f;

        const float minStep = 1e-4f;
        const float maxStep = 0.05f;

        h.x = Mathf.Clamp(h.x, minStep, maxStep);
        h.y = Mathf.Clamp(h.y, minStep, maxStep);
        h.z = Mathf.Clamp(h.z, minStep, maxStep);

        return h;
    }

    public void RebuildSamples()
    {
        BuildSDF();

        Vector3 effectiveGridExtent = GetEffectiveGridExtent();

        GridSize = resolution;

        CellSize = new Vector3(
            effectiveGridExtent.x / GridSize.x,
            effectiveGridExtent.y / GridSize.y,
            effectiveGridExtent.z / GridSize.z
        );

        int totalCount = GridSize.x * GridSize.y * GridSize.z;
        Samples = new SDFSample[totalCount];

        Vector3 localOrigin = -effectiveGridExtent * 0.5f;

        for (int x = 0; x < GridSize.x; x++)
        {
            for (int y = 0; y < GridSize.y; y++)
            {
                for (int z = 0; z < GridSize.z; z++)
                {
                    int index = GetIndex(x, y, z);

                    Vector3 localPos = localOrigin + new Vector3(
                        x * CellSize.x,
                        y * CellSize.y,
                        z * CellSize.z
                    );
                    float distance = _sdf.Evaluate(localPos);

                    Samples[index] = new SDFSample
                    {
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
        if (uniformResolution)
        {
            if (resolution.x != _lastResolution.x)
            {
                resolution.y = resolution.x;
                resolution.z = resolution.x;
            }
            else if (resolution.y != _lastResolution.y)
            {
                resolution.x = resolution.y;
                resolution.z = resolution.y;
            }
            else if (resolution.z != _lastResolution.z)
            {
                resolution.x = resolution.z;
                resolution.y = resolution.z;
            }
        }

        resolution.x = Mathf.Max(2, resolution.x);
        resolution.y = Mathf.Max(2, resolution.y);
        resolution.z = Mathf.Max(2, resolution.z);

        _lastResolution = resolution;
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

    }
}

