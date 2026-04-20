// SphereSDF.cs
using UnityEngine;

public class SphereSDF : ISDF
{
    public Vector3 center;
    public float radius;

    public SphereSDF(Vector3 center, float radius)
    {
        this.center = center;
        this.radius = radius;
    }

    public float Evaluate(Vector3 p)
    {
        return (p - center).magnitude - radius;
    }
}