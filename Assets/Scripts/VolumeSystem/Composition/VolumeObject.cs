#if UNITY_EDITOR
using UnityEditor;
#endif

using UnityEngine;

public enum VolumeShapeType
{
    Sphere,
    Box,
    Torus,
    Hyperboloid,
    CustomAsset
}

public enum VolumeOperationRole
{
    Add,
    Subtract,
    Intersect
}

public enum VolumeGridType
{
    None,
    Global,
    Sphere,
    Torus,
    Hyperboloid
}

[ExecuteAlways]
public class VolumeObject : MonoBehaviour
{
#if UNITY_EDITOR
    private bool _rebuildQueued;

    private Vector3 _lastLocalPosition;
    private Quaternion _lastLocalRotation;
    private Vector3 _lastLocalScale;
#endif

    [Header("Object")]
    public VolumeShapeType shapeType = VolumeShapeType.Sphere;
    public VolumeOperationRole role = VolumeOperationRole.Add;

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
    public VolumeGridType gridType = VolumeGridType.None;

    public float gridWidth = 0.02f;
    public float gridDepth = 0.04f;

    public Vector3 gridSpacing = new Vector3(0.4f, 0.4f, 0.4f);
    public Vector3 gridOffset = Vector3.zero;
    public bool globalGridInWorldSpace = false;

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


#if UNITY_EDITOR
    /// <summary>Stores the initial transform state for editor change detection.</summary>
    private void OnEnable()
    {
        CacheLocalTransform();
    }
#endif

    /// <summary>Updates editor metadata after inspector changes.</summary>
    private void OnValidate()
    {
#if UNITY_EDITOR
        UpdateGameObjectName();
        CacheLocalTransform();

        // Kein QueueComposerRebuild hier:
        // OnValidate feuert auch bei Parent-/Inspector-Änderungen.
        // Rebuild läuft über lokalen Transform-Check oder VolumeModelEditor.
#endif
    }

#if UNITY_EDITOR
    /// <summary>Watches local transform changes in edit mode and queues rebuilds.</summary>
    private void Update()
    {
        if (Application.isPlaying)
            return;

        if (!LocalTransformChanged())
            return;

        CacheLocalTransform();
        QueueComposerRebuild();
    }

    /// <summary>Caches the current local transform values.</summary>
    private void CacheLocalTransform()
    {
        _lastLocalPosition = transform.localPosition;
        _lastLocalRotation = transform.localRotation;
        _lastLocalScale = transform.localScale;
    }

    /// <summary>Checks whether the local transform changed since the last cache.</summary>
    private bool LocalTransformChanged()
    {
        return _lastLocalPosition != transform.localPosition ||
               _lastLocalRotation != transform.localRotation ||
               _lastLocalScale != transform.localScale;
    }

    /// <summary>Queues a delayed composition rebuild in the editor.</summary>
    private void QueueComposerRebuild()
    {
        if (_rebuildQueued)
            return;

        _rebuildQueued = true;
        EditorApplication.delayCall += DelayedComposerRebuild;
    }

    /// <summary>Runs the queued editor rebuild if this object still exists.</summary>
    private void DelayedComposerRebuild()
    {
        _rebuildQueued = false;

        if (this == null)
            return;

        VolumeSceneComposer composer = GetComponentInParent<VolumeSceneComposer>();

        if (composer != null)
            composer.MarkDirtyAndRebuild();
    }

    /// <summary>Renames the GameObject from its shape, role, and grid mode.</summary>
    private void UpdateGameObjectName()
    {
        string shapeName = shapeType.ToString();
        string roleName = role.ToString();

        string gridName = gridType != VolumeGridType.None
            ? $"_{gridType}Grid"
            : "";

        string newName = $"VolumeObject_{shapeName}_{roleName}{gridName}";

        if (gameObject.name != newName)
            gameObject.name = newName;
    }
#endif

    /// <summary>Samples this object's local SDF including optional grid cutters.</summary>
    public float EvaluateLocal(Vector3 p)
    {
        float d = EvaluateShape(p);

        if (gridType != VolumeGridType.None)
        {
            float cutter = EvaluateGridCutter(p, d);
            d = Mathf.Max(d, -cutter);
        }

        return d;
    }

    /// <summary>Samples the base primitive or custom SDF in local space.</summary>
    private float EvaluateShape(Vector3 p)
    {
        switch (shapeType)
        {
            case VolumeShapeType.Box:
                return Box(p, boxHalfExtents);

            case VolumeShapeType.Torus:
                {
                    Vector2 q = new Vector2(
                        new Vector2(p.x, p.z).magnitude - torusMajorRadius,
                        p.y
                    );

                    return q.magnitude - torusMinorRadius;
                }

            case VolumeShapeType.Hyperboloid:
                {
                    float a = Mathf.Max(0.0001f, hyperboloidA);
                    float b = Mathf.Max(0.0001f, hyperboloidB);
                    float c = Mathf.Max(0.0001f, hyperboloidC);

                    return
                        (p.x * p.x) / (a * a) +
                        (p.z * p.z) / (b * b) -
                        (p.y * p.y) / (c * c) -
                        1f;
                }

            case VolumeShapeType.CustomAsset:
                return customAsset != null ? customAsset.Evaluate(p) : 1f;

            case VolumeShapeType.Sphere:
            default:
                return p.magnitude - sphereRadius;
        }
    }

    /// <summary>Evaluates the active grid cutter inside the surface shell.</summary>
    private float EvaluateGridCutter(Vector3 p, float baseDistance)
    {
        float shell = Mathf.Max(baseDistance, -baseDistance - gridDepth);

        float gridD = gridType switch
        {
            VolumeGridType.Global => EvaluateGlobalGrid(p),
            VolumeGridType.Sphere => EvaluateSphereGrid(p),
            VolumeGridType.Torus => EvaluateTorusGrid(p),
            VolumeGridType.Hyperboloid => EvaluateHyperboloidGrid(p),
            _ => 1f
        };

        return Mathf.Max(gridD, shell);
    }

    /// <summary>Evaluates axis-aligned global grid grooves.</summary>
    private float EvaluateGlobalGrid(Vector3 p)
    {
        Vector3 samplePoint = globalGridInWorldSpace
            ? transform.TransformPoint(p)
            : p;

        Vector3 q = samplePoint + gridOffset;

        float d = float.PositiveInfinity;

        if (useXLines)
            d = Mathf.Min(d, Mathf.Abs(RepeatCentered(q.x, gridSpacing.x)) - gridWidth);

        if (useYLines)
            d = Mathf.Min(d, Mathf.Abs(RepeatCentered(q.y, gridSpacing.y)) - gridWidth);

        if (useZLines)
            d = Mathf.Min(d, Mathf.Abs(RepeatCentered(q.z, gridSpacing.z)) - gridWidth);

        return d;
    }

    /// <summary>Evaluates longitude and latitude grooves on a sphere.</summary>
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

    /// <summary>Evaluates major and minor grooves on a torus.</summary>
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

    /// <summary>Evaluates radial and height grooves on a hyperboloid.</summary>
    private float EvaluateHyperboloidGrid(Vector3 p)
    {
        float safeA = Mathf.Max(0.0001f, hyperboloidA);
        float safeB = Mathf.Max(0.0001f, hyperboloidB);

        float theta = Mathf.Atan2(p.z / safeB, p.x / safeA) + gridOffset.x;

        int radial = Mathf.Max(1, hyperboloidRadialSegments);
        int height = Mathf.Max(1, hyperboloidHeightSegments);

        float radialSpacing = Mathf.PI * 2f / radial;
        float heightSpacing = Mathf.Max(
            0.0001f,
            (hyperboloidHeightMax - hyperboloidHeightMin) / height
        );

        float rx = p.x / safeA;
        float rz = p.z / safeB;
        float localRadius = Mathf.Sqrt(rx * rx + rz * rz);

        float angularScale = Mathf.Max(
            0.0001f,
            localRadius * Mathf.Min(safeA, safeB)
        );

        float radialDist = Mathf.Abs(RepeatCentered(theta, radialSpacing)) * angularScale;
        float heightDist = Mathf.Abs(
            RepeatCentered(p.y - hyperboloidHeightMin + gridOffset.y, heightSpacing)
        );

        return Mathf.Min(radialDist, heightDist) - gridWidth;
    }

    /// <summary>Repeats a coordinate around zero with the given spacing.</summary>
    private static float RepeatCentered(float v, float spacing)
    {
        spacing = Mathf.Max(0.0001f, spacing);
        return v - spacing * Mathf.Floor(v / spacing + 0.5f);
    }

    /// <summary>Returns the component-wise absolute value.</summary>
    private static Vector3 Abs(Vector3 v)
    {
        return new Vector3(
            Mathf.Abs(v.x),
            Mathf.Abs(v.y),
            Mathf.Abs(v.z)
        );
    }

    /// <summary>Evaluates an axis-aligned box SDF.</summary>
    private static float Box(Vector3 p, Vector3 halfExtents)
    {
        Vector3 q = Abs(p) - halfExtents;

        return Vector3.Max(q, Vector3.zero).magnitude +
               Mathf.Min(Mathf.Max(q.x, Mathf.Max(q.y, q.z)), 0f);
    }

    /// <summary>Draws a scene-view outline for this volume object.</summary>
    private void DrawVolumeGizmo(bool selected)
    {
        Matrix4x4 oldMatrix = Gizmos.matrix;
        Color oldColor = Gizmos.color;

        Gizmos.matrix = transform.localToWorldMatrix;

        float alpha = selected ? 1f : 0.45f;

        Gizmos.color = role switch
        {
            VolumeOperationRole.Add => new Color(0f, 1f, 0f, alpha),
            VolumeOperationRole.Subtract => new Color(1f, 0f, 0f, alpha),
            VolumeOperationRole.Intersect => new Color(0f, 0.5f, 1f, alpha),
            _ => Color.white
        };

        switch (shapeType)
        {
            case VolumeShapeType.Sphere:
                Gizmos.DrawWireSphere(Vector3.zero, sphereRadius);
                break;

            case VolumeShapeType.Box:
                Gizmos.DrawWireCube(Vector3.zero, boxHalfExtents * 2f);
                break;

            case VolumeShapeType.Torus:
                DrawTorusGizmo(torusMajorRadius, torusMinorRadius);
                break;

            case VolumeShapeType.Hyperboloid:
                Gizmos.DrawWireCube(
                    Vector3.zero,
                    new Vector3(
                        hyperboloidA * 2f,
                        Mathf.Abs(hyperboloidHeightMax - hyperboloidHeightMin),
                        hyperboloidB * 2f
                    )
                );
                break;
        }

        Gizmos.matrix = oldMatrix;
        Gizmos.color = oldColor;
    }

    /// <summary>Draws a simple torus outline for the scene-view gizmo.</summary>
    private void DrawTorusGizmo(float majorRadius, float minorRadius)
    {
        const int segments = 64;

        Vector3 prevOuter = Vector3.zero;
        Vector3 prevInner = Vector3.zero;

        for (int i = 0; i <= segments; i++)
        {
            float t = i / (float)segments * Mathf.PI * 2f;

            Vector3 outer = new Vector3(
                Mathf.Cos(t) * (majorRadius + minorRadius),
                0f,
                Mathf.Sin(t) * (majorRadius + minorRadius)
            );

            Vector3 inner = new Vector3(
                Mathf.Cos(t) * Mathf.Max(0f, majorRadius - minorRadius),
                0f,
                Mathf.Sin(t) * Mathf.Max(0f, majorRadius - minorRadius)
            );

            if (i > 0)
            {
                Gizmos.DrawLine(prevOuter, outer);
                Gizmos.DrawLine(prevInner, inner);
            }

            prevOuter = outer;
            prevInner = inner;
        }
    }

    /// <summary>Draws the object gizmo when child gizmos are enabled.</summary>
    private void OnDrawGizmos()
    {
        if (!ShouldDrawGizmos())
            return;

        DrawVolumeGizmo(false);
    }

    /// <summary>Draws the selected object gizmo when child gizmos are enabled.</summary>
    private void OnDrawGizmosSelected()
    {
        if (!ShouldDrawGizmos())
            return;

        DrawVolumeGizmo(true);
    }

    /// <summary>Checks the parent model setting that controls child gizmos.</summary>
    private bool ShouldDrawGizmos()
    {
        VolumeModel model = GetComponentInParent<VolumeModel>();

        if (model == null)
            return true;

        return model.drawChildGizmos;
    }
}
