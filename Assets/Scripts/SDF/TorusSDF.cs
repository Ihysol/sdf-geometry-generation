using UnityEngine;

public class TorusSDF : ISDF
{
    private readonly Vector3 center;
    private readonly float majorRadius; // R
    private readonly float minorRadius; // r

    public TorusSDF(Vector3 center, float majorRadius, float minorRadius)
    {
        this.center = center;
        this.majorRadius = majorRadius;
        this.minorRadius = minorRadius;
    }

    public float Evaluate(Vector3 p)
    {
        // move into local space
        Vector3 d = p - center;

        // distance in XZ plane
        float lenXZ = new Vector2(d.x, d.z).magnitude;

        // torus distance
        Vector2 q = new Vector2(lenXZ - majorRadius, d.y);

        return q.magnitude - minorRadius;
    }
}