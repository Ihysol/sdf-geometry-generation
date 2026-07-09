using UnityEngine;

/// <summary>Defines a reusable volume feature (e.g., column, arch, window).</summary>
[CreateAssetMenu(menuName = "VolumeSystem/Feature Definition")]
public class FeatureDefinition : ScriptableObject
{
    public string displayName;
    public VolumeShapeType shapeType;

    /// <summary>Base SDF parameters matching VolumeObject conventions.</summary>
    public float sphereRadius = 1f;
    public Vector3 boxHalfExtents = Vector3.one * 0.5f;
    public float torusMajorRadius = 1f;
    public float torusMinorRadius = 0.25f;

    /// <summary>Operation role when applied to the volume buffer.</summary>
    public VolumeOperationRole operationRole = VolumeOperationRole.Add;

    /// <summary>Default material for this feature.</summary>
    public int defaultMaterialId = 0;

    /// <summary>Evaluates the feature's SDF in local space.</summary>
    public float EvaluateLocal(Vector3 p)
    {
        switch (shapeType)
        {
            case VolumeShapeType.Sphere:
                return p.magnitude - sphereRadius;

            case VolumeShapeType.Box:
                return BoxSdf(p, boxHalfExtents);

            case VolumeShapeType.Torus:
                {
                    Vector2 q = new Vector2(
                        new Vector2(p.x, p.z).magnitude - torusMajorRadius,
                        p.y
                    );
                    return q.magnitude - torusMinorRadius;
                }

            default:
                return 1f;
        }
    }

    private static float BoxSdf(Vector3 p, Vector3 halfExtents)
    {
        Vector3 q = new Vector3(
            Mathf.Abs(p.x) - halfExtents.x,
            Mathf.Abs(p.y) - halfExtents.y,
            Mathf.Abs(p.z) - halfExtents.z
        );
        return Vector3.Max(q, Vector3.zero).magnitude +
               Mathf.Min(Mathf.Max(q.x, Mathf.Max(q.y, q.z)), 0f);
    }

    /// <summary>Returns approximate half-extents for the feature shape.</summary>
    public Vector3 GetApproximateHalfExtents()
    {
        switch (shapeType)
        {
            case VolumeShapeType.Sphere:
                float r = Mathf.Abs(sphereRadius);
                return new Vector3(r, r, r);

            case VolumeShapeType.Box:
                return new Vector3(
                    Mathf.Abs(boxHalfExtents.x),
                    Mathf.Abs(boxHalfExtents.y),
                    Mathf.Abs(boxHalfExtents.z)
                );

            case VolumeShapeType.Torus:
                float torusR = Mathf.Abs(torusMajorRadius) + Mathf.Abs(torusMinorRadius);
                return new Vector3(torusR, Mathf.Abs(torusMinorRadius), torusR);

            default:
                return Vector3.one;
        }
    }
}
