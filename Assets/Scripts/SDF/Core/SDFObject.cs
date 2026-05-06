using UnityEngine;

public enum SDFShapeType
{
    Sphere,
    Box,
    Torus,
    Hyperboloid,
    CustomAsset
}

public enum SDFOperationRole
{
    Add,
    Subtract,
    Intersect
}

public enum SDFGridType
{
    None,
    Global,
    Sphere,
    Torus,
    Hyperboloid
}

public class SDFObject : MonoBehaviour
{
    [Header("Object")]
    public SDFShapeType shapeType = SDFShapeType.Sphere;
    public SDFOperationRole role = SDFOperationRole.Add;

    [Header("Custom")]
    public SDF customAsset;

    [Header("Sphere")]
    public float sphereRadius = 1f;

    [Header("Box")]
    public Vector3 boxHalfExtents = Vector3.one * 0.5f;

    [Header("Torus")]
    public float torusMajorRadius = 1f;
    public float torusMinorRadius = 0.25f;

    [Header("Hyperboloid")]
    public float hyperboloidA = 1f;
    public float hyperboloidB = 1f;
    public float hyperboloidC = 1f;

    [Header("Surface Grid / Cutter")]
    public SDFGridType gridType = SDFGridType.None;

    public float gridWidth = 0.02f;
    public float gridDepth = 0.04f;

    public Vector3 gridSpacing = new Vector3(0.4f, 0.4f, 0.4f);
    public Vector3 gridOffset = Vector3.zero;

    public int longitudeCount = 16;
    public int latitudeCount = 8;

    public int torusMajorSegments = 24;
    public int torusMinorSegments = 12;

    public int hyperboloidRadialSegments = 24;
    public int hyperboloidHeightSegments = 12;
    public float hyperboloidHeightMin = -2f;
    public float hyperboloidHeightMax = 2f;

    public bool useXLines = true;
    public bool useYLines = true;
    public bool useZLines = true;


    private void OnValidate()
    {
#if UNITY_EDITOR
        UpdateGameObjectName();
#endif
    }

#if UNITY_EDITOR
    private void UpdateGameObjectName()
    {
        string shapeName = shapeType.ToString();
        string roleName = role.ToString();

        string gridName = gridType != SDFGridType.None
            ? $"_{gridType}Grid"
            : "";

        string newName = $"SDFObject_{shapeName}_{roleName}{gridName}";

        if (gameObject.name != newName)
            gameObject.name = newName;
    }
#endif
    public float EvaluateLocal(Vector3 p)
    {
        float d = EvaluateShape(p);

        if (gridType != SDFGridType.None)
        {
            float cutter = EvaluateGridCutter(p, d);
            d = Mathf.Max(d, -cutter);
        }

        return d;
    }

    private float EvaluateShape(Vector3 p)
    {
        switch (shapeType)
        {

            case SDFShapeType.Box:
                return Box(p, boxHalfExtents);

            case SDFShapeType.Torus:
                Vector2 q = new Vector2(
                    new Vector2(p.x, p.z).magnitude - torusMajorRadius,
                    p.y
                );
                return q.magnitude - torusMinorRadius;

            case SDFShapeType.Hyperboloid:
                float a = Mathf.Max(0.0001f, hyperboloidA);
                float b = Mathf.Max(0.0001f, hyperboloidB);
                float c = Mathf.Max(0.0001f, hyperboloidC);

                return
                    (p.x * p.x) / (a * a) +
                    (p.z * p.z) / (b * b) -
                    (p.y * p.y) / (c * c) -
                    1f;

            case SDFShapeType.CustomAsset:
                return customAsset != null ? customAsset.Evaluate(p) : 1f;

            case SDFShapeType.Sphere:
            default:
                return p.magnitude - sphereRadius;
        }
    }

    private float EvaluateGridCutter(Vector3 p, float baseDistance)
    {
        float shell = Mathf.Max(baseDistance, -baseDistance - gridDepth);

        float gridD = gridType switch
        {
            SDFGridType.Global => EvaluateGlobalGrid(p),
            SDFGridType.Sphere => EvaluateSphereGrid(p),
            SDFGridType.Torus => EvaluateTorusGrid(p),
            SDFGridType.Hyperboloid => EvaluateHyperboloidGrid(p),
            _ => 1f
        };

        return Mathf.Max(gridD, shell);
    }

    private float EvaluateGlobalGrid(Vector3 p)
    {
        Vector3 q = p + gridOffset;

        float d = float.PositiveInfinity;

        if (useXLines)
            d = Mathf.Min(d, Mathf.Abs(RepeatCentered(q.x, gridSpacing.x)) - gridWidth);

        if (useYLines)
            d = Mathf.Min(d, Mathf.Abs(RepeatCentered(q.y, gridSpacing.y)) - gridWidth);

        if (useZLines)
            d = Mathf.Min(d, Mathf.Abs(RepeatCentered(q.z, gridSpacing.z)) - gridWidth);

        return d;
    }

    private float EvaluateSphereGrid(Vector3 p)
    {
        float r = p.magnitude;

        if (r < 1e-6f)
            return 1f;

        Vector3 n = p / r;

        float theta = Mathf.Atan2(n.z, n.x) + gridOffset.x;
        float phi = Mathf.Acos(Mathf.Clamp(n.y, -1f, 1f)) + gridOffset.y;

        int lon = Mathf.Max(1, longitudeCount);
        int lat = Mathf.Max(1, latitudeCount);

        float lonSpacing = Mathf.PI * 2f / lon;
        float latSpacing = Mathf.PI / lat;

        float lonDist = Mathf.Abs(RepeatCentered(theta, lonSpacing)) * r * Mathf.Sin(phi);
        float latDist = Mathf.Abs(RepeatCentered(phi, latSpacing)) * r;

        return Mathf.Min(lonDist, latDist) - gridWidth;
    }

    private float EvaluateTorusGrid(Vector3 p)
    {
        float theta = Mathf.Atan2(p.z, p.x) + gridOffset.x;

        float radial = new Vector2(p.x, p.z).magnitude;
        float phi = Mathf.Atan2(p.y, radial - torusMajorRadius) + gridOffset.y;

        int major = Mathf.Max(1, torusMajorSegments);
        int minor = Mathf.Max(1, torusMinorSegments);

        float majorSpacing = Mathf.PI * 2f / major;
        float minorSpacing = Mathf.PI * 2f / minor;

        float majorDist = Mathf.Abs(RepeatCentered(theta, majorSpacing)) * Mathf.Max(0.0001f, torusMajorRadius);
        float minorDist = Mathf.Abs(RepeatCentered(phi, minorSpacing)) * Mathf.Max(0.0001f, torusMinorRadius);

        return Mathf.Min(majorDist, minorDist) - gridWidth;
    }

    private float EvaluateHyperboloidGrid(Vector3 p)
    {
        float safeA = Mathf.Max(0.0001f, hyperboloidA);
        float safeB = Mathf.Max(0.0001f, hyperboloidB);

        float theta = Mathf.Atan2(p.z / safeB, p.x / safeA) + gridOffset.x;

        int radial = Mathf.Max(1, hyperboloidRadialSegments);
        int height = Mathf.Max(1, hyperboloidHeightSegments);

        float radialSpacing = Mathf.PI * 2f / radial;
        float heightSpacing = Mathf.Max(0.0001f, (hyperboloidHeightMax - hyperboloidHeightMin) / height);

        float rx = p.x / safeA;
        float rz = p.z / safeB;
        float localRadius = Mathf.Sqrt(rx * rx + rz * rz);

        float angularScale = Mathf.Max(0.0001f, localRadius * Mathf.Min(safeA, safeB));

        float radialDist = Mathf.Abs(RepeatCentered(theta, radialSpacing)) * angularScale;
        float heightDist = Mathf.Abs(RepeatCentered(p.y - hyperboloidHeightMin + gridOffset.y, heightSpacing));

        return Mathf.Min(radialDist, heightDist) - gridWidth;
    }

    private static float RepeatCentered(float v, float spacing)
    {
        spacing = Mathf.Max(0.0001f, spacing);
        return v - spacing * Mathf.Floor(v / spacing + 0.5f);
    }

    private static Vector3 Abs(Vector3 v)
    {
        return new Vector3(Mathf.Abs(v.x), Mathf.Abs(v.y), Mathf.Abs(v.z));
    }

    private static float Box(Vector3 p, Vector3 halfExtents)
    {
        Vector3 q = Abs(p) - halfExtents;

        return Vector3.Max(q, Vector3.zero).magnitude +
               Mathf.Min(Mathf.Max(q.x, Mathf.Max(q.y, q.z)), 0f);
    }
}