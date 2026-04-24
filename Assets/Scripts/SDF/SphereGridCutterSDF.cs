using UnityEngine;


public class SphereGridCutterSDF : ISDF
{
    private float radius;
    private float width;
    private float depth;
    private int lonCount;
    private int latCount;

    public SphereGridCutterSDF(float radius, float width, float depth, int lon, int lat)
    {
        this.radius = radius;
        this.width = width;
        this.depth = depth;
        this.lonCount = lon;
        this.latCount = lat;
    }

    public float Evaluate(Vector3 p)
    {
        float sphere = p.magnitude - radius;

        if (p.sqrMagnitude < 1e-8f)
            return 1f;

        Vector3 n = p.normalized;

        float theta = Mathf.Atan2(n.z, n.x);
        float phi = Mathf.Acos(Mathf.Clamp(n.y, -1f, 1f));

        float lonSpacing = Mathf.PI * 2f / lonCount;
        float latSpacing = Mathf.PI / latCount;

        float lonDist = Repeat(theta, lonSpacing) * (radius * Mathf.Sin(phi));
        float latDist = Repeat(phi, latSpacing) * radius;

        float grid = Mathf.Min(lonDist, latDist) - width;

        float surfaceBand = Mathf.Abs(sphere) - depth;

        return Mathf.Max(grid, surfaceBand);
    }

    private float Repeat(float v, float spacing)
    {
        float x = v / spacing;
        float nearest = Mathf.Round(x) * spacing;
        return Mathf.Abs(v - nearest);
    }
}