using System.Runtime.CompilerServices;
using Unity.VectorGraphics;
using UnityEngine;
using UnityEngine.SocialPlatforms;

public class SDFSampler : MonoBehaviour
{
    public enum ShapeMode
    {
        Sphere,
        Box,
        Torus
    }

    public enum GridMode
    {
        None,
        GlobalGrid,
        SphereGrid,
        TorusGrid
    }

    [Header("Shape")]
    public ShapeMode shapeMode = ShapeMode.Sphere;
    public float sphereRadius = 1.5f;
    public Vector3 boxHalfExtents = new Vector3(1f, 1f, 1f);

    [Header("Torus")]
    public float torusMajorRadius = 1.5f;
    public float torusMinorRadius = 0.4f;

    [Header("Surface Grid")]
    public bool useGrid = false;
    public bool invertGrid = false;
    public GridMode gridMode = GridMode.SphereGrid;

    public float gridGrooveWidth = 0.03f;
    public float gridGrooveDepth = 0.05f;
    public int gridLongitudeCount = 24;
    public int gridLatitudeCount = 12;
    public float gridSpacing = 0.25f;

    [Header("Grid")]
    public bool useAutomaticBounds = false;
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

    [Header("Performance")]
    public int targetFPS = 60;

    public void MarkDirty()
    {
        IsDirty = true;
    }

    public Vector3Int CellCount
    {
        get
        {
            return new Vector3Int(
                GridSize.x - 1,
                GridSize.y - 1,
                GridSize.z - 1
            );
        }
    }

    public Vector3 GetLocalGridPosition(int x, int y, int z)
    {
        Vector3 effectiveGridExtent = GetEffectiveGridExtent();
        Vector3 localOrigin = -effectiveGridExtent * 0.5f;

        return localOrigin + new Vector3(
            x * CellSize.x,
            y * CellSize.y,
            z * CellSize.z
        );
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
            effectiveGridExtent.x / (GridSize.x - 1),
            effectiveGridExtent.y / (GridSize.y - 1),
            effectiveGridExtent.z / (GridSize.z - 1)
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
        ISDF body;

        // ===== 1. Base Shape =====
        switch (shapeMode)
        {
            case ShapeMode.Box:
                body = new BoxSDF(Vector3.zero, boxHalfExtents);
                break;

            case ShapeMode.Torus:
                body = new TorusSDF(Vector3.zero, torusMajorRadius, torusMinorRadius);
                break;

            case ShapeMode.Sphere:
            default:
                body = new SphereSDF(Vector3.zero, sphereRadius);
                break;
        }

        // ===== 2. No grid =====
        if (!useGrid || gridMode == GridMode.None)
        {
            _sdf = body;
            return;
        }

        // ===== 3. Build grid cutter =====
        ISDF gridCutter;

        switch (gridMode)
        {
            case GridMode.SphereGrid:
            default:
                gridCutter = new SphereGridCutterSDF(
                    sphereRadius,
                    gridGrooveWidth,
                    gridGrooveDepth,
                    gridLongitudeCount,
                    gridLatitudeCount
                );
                break;

            case GridMode.GlobalGrid:
                gridCutter = new GlobalGridCutterSDF(
                    body,
                    gridGrooveWidth,
                    gridGrooveDepth,
                    gridSpacing
                );
                break;

            case GridMode.TorusGrid:
                gridCutter = new TorusGridCutterSDF(
                    body,
                    torusMajorRadius,
                    gridGrooveWidth,
                    gridGrooveDepth,
                    gridLongitudeCount,
                    gridLatitudeCount
                );
                break;
        }

        // ===== 4. APPLY GRID (FIXED) =====
        if (invertGrid)
        {
            _sdf = new UnionSDF(body, gridCutter);      // 👈 nach außen
        }
        else
        {
            _sdf = new DifferenceSDF(body, gridCutter); // 👈 nach innen
        }
    }

    public class DifferenceSDF : ISDF
    {
        private readonly ISDF a;
        private readonly ISDF b;

        public DifferenceSDF(ISDF a, ISDF b)
        {
            this.a = a;
            this.b = b;
        }

        public float Evaluate(Vector3 p)
        {
            return Mathf.Max(a.Evaluate(p), -b.Evaluate(p));
        }
    }

    public class UnionSDF : ISDF
    {
        private readonly ISDF a;
        private readonly ISDF b;

        public UnionSDF(ISDF a, ISDF b)
        {
            this.a = a;
            this.b = b;
        }

        public float Evaluate(Vector3 p)
        {
            return Mathf.Min(a.Evaluate(p), b.Evaluate(p));
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

        ApplyFPSLimit();

    }

    public Vector3 GetEffectiveGridExtent()
    {
        if (!useAutomaticBounds)
            return gridExtent;

        switch (shapeMode)
        {
            case ShapeMode.Box:
                return (boxHalfExtents + Vector3.one * boundsPadding) * 2f;

            case ShapeMode.Torus:
                {
                    float torus_radius = torusMajorRadius + torusMinorRadius + boundsPadding;

                    if (useGrid)
                        torus_radius += gridGrooveDepth + gridGrooveWidth;

                    return new Vector3(torus_radius * 2f, torus_radius * 2f, torus_radius * 2f);
                }

            case ShapeMode.Sphere:
            default:
                float r = sphereRadius + boundsPadding + gridGrooveDepth;
                return new Vector3(r * 2f, r * 2f, r * 2f);
        }
    }

    private void Update()
    {

    }

    private void Awake()
    {
        ApplyFPSLimit();
    }

    private void ApplyFPSLimit()
    {
        QualitySettings.vSyncCount = 0;
        Application.targetFrameRate = targetFPS;
    }
}

