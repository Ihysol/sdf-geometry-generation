// BoxSDF.cs
using UnityEngine;

public class BoxSDF : ISDF
{
    public Vector3 center;
    public Vector3 size; // half extents!

    public BoxSDF(Vector3 center, Vector3 size)
    {
        this.center = center;
        this.size = size;
    }

    public float Evaluate(Vector3 p)
    {
        Vector3 d = Abs(p - center) - size;
        Vector3 outside = Max(d, Vector3.zero);
        float outsideDist = outside.magnitude;

        float insideDist = Mathf.Min(Mathf.Max(d.x, Mathf.Max(d.y, d.z)), 0.0f);

        return outsideDist + insideDist;
    }

    private Vector3 Abs(Vector3 v)
    {
        return new Vector3(Mathf.Abs(v.x), Mathf.Abs(v.y), Mathf.Abs(v.z));
    }

    private Vector3 Max(Vector3 a, Vector3 b)
    {
        return new Vector3(Mathf.Max(a.x, b.x), Mathf.Max(a.y, b.y), Mathf.Max(a.z, b.z));
    }
}