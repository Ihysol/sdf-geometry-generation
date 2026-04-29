using UnityEngine;

public class SDFSampler : MonoBehaviour
{
    public enum ShapeMode
    {
        Sphere,
        Box,
        Torus,
        Hyperboloid
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

    [Header("Hyperboloid")]
    public float hyperboloidA = 1f;
    public float hyperboloidB = 1f;
    public float hyperboloidC = 1f;

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

    private Vector3Int _lastResolution;
    private Vector3Int _lastAppliedResolution;
    private bool _lastUniform;
    private int _lastSettingsHash;

    [Header("Performance")]
    public int targetFPS = 60;
    public float[] Distances { get; private set; }
    public Vector3Int GridSize { get; private set; }
    public Vector3 GridOrigin { get; private set; }
    public Vector3 CellSize { get; private set; }
    public bool IsDirty { get; private set; } = true;

    public Vector3Int CellCount => new Vector3Int(
                GridSize.x - 1,
                GridSize.y - 1,
                GridSize.z - 1
    );

    private void Awake()
    {
        ApplyFPSLimit();
    }

    private void OnValidate()
    {
        // uniform sync
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

        int settingsHash = ComputeSettingsHash();
        if (settingsHash != _lastSettingsHash)
        {
            MarkDirty();
            _lastSettingsHash = settingsHash;
        }


        ApplyFPSLimit();
    }

    public void MarkDirty()
    {
        IsDirty = true;
    }

    public void RebuildSamples()
    {
        Vector3 effectiveGridExtent = GetEffectiveGridExtent();

        GridOrigin = -effectiveGridExtent * 0.5f;
        GridSize = resolution;

        CellSize = new Vector3(
            effectiveGridExtent.x / (GridSize.x - 1),
            effectiveGridExtent.y / (GridSize.y - 1),
            effectiveGridExtent.z / (GridSize.z - 1)
        );

        int totalCount = GridSize.x * GridSize.y * GridSize.z;

        if (Distances == null || Distances.Length != totalCount)
            Distances = new float[totalCount];

        Vector3 localOrigin = GridOrigin;

        int index = 0;
        float originX = localOrigin.x;
        float originY = localOrigin.y;
        float originZ = localOrigin.z;

        float stepX = CellSize.x;
        float stepY = CellSize.y;
        float stepZ = CellSize.z;

        for (int z = 0; z < GridSize.z; z++)
        {
            float pz = originZ + z * stepZ;
            for (int y = 0; y < GridSize.y; y++)
            {
                float py = originY + y * stepY;
                for (int x = 0; x < GridSize.x; x++)
                {
                    float px = originX + x * stepX;
                    Vector3 localPos = new Vector3(px, py, pz);
                    Distances[index++] = EvaluateDirect(localPos);
                }
            }
        }
        IsDirty = false;
    }

    public int GetIndex(int x, int y, int z)
    {
        return x + GridSize.x * (y + GridSize.y * z);
    }

    public float GetDistance(int x, int y, int z)
    {
        return Distances[GetIndex(x, y, z)];
    }

    public Vector3 GetLocalGridPosition(int x, int y, int z)
    {
        return GridOrigin + new Vector3(
            x * CellSize.x,
            y * CellSize.y,
            z * CellSize.z
        );
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

            case ShapeMode.Hyperboloid:
                {
                    float x = hyperboloidA + boundsPadding;
                    float y = hyperboloidB + boundsPadding;
                    float z = hyperboloidC * 2f + boundsPadding;

                    return new Vector3(x * 4f, y * 4f, z * 2f);
                }

            case ShapeMode.Sphere:
            default:
                float r = sphereRadius + boundsPadding;

                if (useGrid)
                    r += gridGrooveDepth + gridGrooveWidth;
                return Vector3.one * (r * 2f);
        }
    }

    private void ApplyFPSLimit()
    {
        QualitySettings.vSyncCount = 0;
        Application.targetFrameRate = targetFPS;
    }

    private float EvaluateDirect(Vector3 p)
    {
        float body = EvaluateBodyDirect(p);

        if (!useGrid || gridMode == GridMode.None)
            return body;

        float cutter = EvaluateGridDirect(p, body);

        return invertGrid
            ? Mathf.Min(body, cutter)
            : Mathf.Max(body, -cutter);
    }

    private static float EvaluateBox(Vector3 p, Vector3 halfExtents)
    {
        Vector3 q = new Vector3(
            Mathf.Abs(p.x),
            Mathf.Abs(p.y),
            Mathf.Abs(p.z)
        ) - halfExtents;

        Vector3 outside = new Vector3(
            Mathf.Max(q.x, 0f),
            Mathf.Max(q.y, 0f),
            Mathf.Max(q.z, 0f)
        );
        float inside = Mathf.Min(Mathf.Max(q.x, Mathf.Max(q.y, q.z)), 0f);
        return outside.magnitude + inside;
    }

    private static float EvaluateTorus(Vector3 p, float majorRadius, float minorRadius)
    {
        Vector2 q = new Vector2(
            new Vector2(p.x, p.z).magnitude - majorRadius,
            p.y
        );
        return q.magnitude - minorRadius;
    }

    private static float EvaluateHyperboloid(Vector3 p, float a, float b, float c)
    {
        a = Mathf.Max(1e-4f, a);
        b = Mathf.Max(1e-4f, b);
        c = Mathf.Max(1e-4f, c);

        float invA2 = 1f / (a * a);
        float invB2 = 1f / (b * b);
        float invC2 = 1f / (c * c);

        float f =
            p.x * p.x * invA2 +
            p.y * p.y * invB2 -
            p.z * p.z * invC2 -
            1f;

        Vector3 grad = new Vector3(
            2f * p.x * invA2,
            2f * p.y * invB2,
           -2f * p.z * invC2
        );

        float g = grad.magnitude;

        return g > 1e-6f ? f / g : f;
    }


    private float EvaluateBodyDirect(Vector3 p)
    {
        switch (shapeMode)
        {
            case ShapeMode.Box:
                return EvaluateBox(p, boxHalfExtents);

            case ShapeMode.Torus:
                return EvaluateTorus(p, torusMajorRadius, torusMinorRadius);

            case ShapeMode.Hyperboloid:
                return EvaluateHyperboloid(p, hyperboloidA, hyperboloidB, hyperboloidC);

            case ShapeMode.Sphere:
            default:
                return p.magnitude - sphereRadius;
        }
    }
    private float EvaluateGridDirect(Vector3 p, float body)
    {
        switch (gridMode)
        {
            case GridMode.GlobalGrid:
                return EvaluateGlobalGridCutter(p, body);

            case GridMode.TorusGrid:
                return EvaluateTorusGridCutter(p, body);

            case GridMode.SphereGrid:
            default:
                return EvaluateSphereGridCutter(
                    p,
                    sphereRadius,
                    gridGrooveWidth,
                    gridGrooveDepth,
                    gridLongitudeCount,
                    gridLatitudeCount
                );
        }
    }

    private float EvaluateGlobalGridCutter(Vector3 p, float body)
    {
        float gx = RepeatCentered(p.x, gridSpacing);
        float gy = RepeatCentered(p.y, gridSpacing);
        float gz = RepeatCentered(p.z, gridSpacing);

        float halfWidth = gridGrooveWidth * 0.5f;
        float grid = Mathf.Min(gx, Mathf.Min(gy, gz)) - halfWidth;
        float surfaceBand = Mathf.Abs(body) - gridGrooveDepth;

        return Mathf.Max(grid, surfaceBand);
    }

    private float EvaluateTorusGridCutter(Vector3 p, float body)
    {
        float u = Mathf.Atan2(p.z, p.x);

        float radial = new Vector2(p.x, p.z).magnitude;

        float tubeX = radial - torusMajorRadius;
        float tubeY = p.y;

        float v = Mathf.Atan2(tubeY, tubeX);

        int majorCount = Mathf.Max(1, gridLongitudeCount);
        int minorCount = Mathf.Max(1, gridLatitudeCount);

        float uSpacing = Mathf.PI * 2f / majorCount;
        float vSpacing = Mathf.PI * 2f / minorCount;

        float tubeRadius = Mathf.Max(
            0.0001f,
            Mathf.Sqrt(tubeX * tubeX + tubeY * tubeY)
        );

        float uDist = RepeatCentered(u, uSpacing) * torusMajorRadius;
        float vDist = RepeatCentered(v, vSpacing) * tubeRadius;

        float halfWidth = gridGrooveWidth * 0.5f;
        float grid = Mathf.Min(uDist, vDist) - halfWidth;

        float surfaceBand = Mathf.Abs(body) - gridGrooveDepth;

        return Mathf.Max(grid, surfaceBand);
    }

    private static float EvaluateSphereGridCutter(
        Vector3 p,
        float sphereRadius,
        float gridGrooveWidth,
        float gridGrooveDepth,
        int gridLongitudeCount,
        int gridLatitudeCount)
    {
        float r = p.magnitude;

        if (r < 1e-6f)
            return 1f;

        float sphere = r - sphereRadius;

        Vector3 n = p / r;
        float theta = Mathf.Atan2(n.z, n.x);
        float phi = Mathf.Acos(Mathf.Clamp(n.y, -1f, 1f));

        gridLongitudeCount = Mathf.Max(1, gridLongitudeCount);
        gridLatitudeCount = Mathf.Max(1, gridLatitudeCount);

        float lonSpacing = Mathf.PI * 2f / gridLongitudeCount;
        float latSpacing = Mathf.PI / gridLatitudeCount;

        float sinPhi = Mathf.Sin(phi);

        float lonAngleDist = RepeatCentered(theta, lonSpacing);
        float latAngleDist = RepeatCentered(phi, latSpacing);

        float halfWidth = gridGrooveWidth * 0.5f;

        float lonDist = lonAngleDist * sphereRadius * sinPhi;
        float latDist = latAngleDist * sphereRadius;

        float lonStripe = lonDist - halfWidth;
        float latStripe = latDist - halfWidth;

        float grid = Mathf.Min(lonStripe, latStripe);
        float surfaceBand = Mathf.Abs(sphere) - gridGrooveDepth;

        return Mathf.Max(grid, surfaceBand);
    }
    private static float RepeatCentered(float v, float spacing)
    {
        spacing = Mathf.Max(1e-6f, spacing);

        float x = v / spacing;
        float nearest = Mathf.Round(x) * spacing;
        return Mathf.Abs(v - nearest);
    }

    private int ComputeSettingsHash()
    {
        unchecked
        {
            int hash = 17;

            hash = hash * 31 + shapeMode.GetHashCode();
            hash = hash * 31 + sphereRadius.GetHashCode();
            hash = hash * 31 + boxHalfExtents.GetHashCode();

            hash = hash * 31 + torusMajorRadius.GetHashCode();
            hash = hash * 31 + torusMinorRadius.GetHashCode();

            hash = hash * 31 + hyperboloidA.GetHashCode();
            hash = hash * 31 + hyperboloidB.GetHashCode();
            hash = hash * 31 + hyperboloidC.GetHashCode();

            hash = hash * 31 + useGrid.GetHashCode();
            hash = hash * 31 + invertGrid.GetHashCode();
            hash = hash * 31 + gridMode.GetHashCode();

            hash = hash * 31 + gridGrooveWidth.GetHashCode();
            hash = hash * 31 + gridGrooveDepth.GetHashCode();
            hash = hash * 31 + gridLongitudeCount;
            hash = hash * 31 + gridLatitudeCount;
            hash = hash * 31 + gridSpacing.GetHashCode();

            hash = hash * 31 + useAutomaticBounds.GetHashCode();
            hash = hash * 31 + gridExtent.GetHashCode();
            hash = hash * 31 + boundsPadding.GetHashCode();

            hash = hash * 31 + resolution.GetHashCode();
            hash = hash * 31 + uniformResolution.GetHashCode();

            return hash;
        }
    }

}

