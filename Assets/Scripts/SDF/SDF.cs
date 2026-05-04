using UnityEngine;

public static class SDF
{
    public static float Sphere(Vector3 p, float radius)
    {
        return p.magnitude - radius;
    }

    public static float Box(Vector3 p, Vector3 halfExtents)
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

    public static float Torus(Vector3 p, float majorRadius, float minorRadius)
    {
        Vector2 q = new Vector2(
            new Vector2(p.x, p.z).magnitude - majorRadius,
            p.y
        );
        return q.magnitude - minorRadius;
    }

    public static float Hyperboloid(Vector3 p, float a, float b, float c)
    {
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
}