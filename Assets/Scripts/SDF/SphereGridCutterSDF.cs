using UnityEngine;

public class SphereGridCutterSDF : ISDF
{
    private readonly float radius;
    private readonly float halfWidth;
    private readonly float depth;
    private readonly int lonCount;
    private readonly int latCount;

    public SphereGridCutterSDF(float radius, float width, float depth, int lon, int lat)
    {
        this.radius = radius;
        this.halfWidth = width * 0.5f;
        this.depth = depth;
        this.lonCount = Mathf.Max(1, lon);
        this.latCount = Mathf.Max(1, lat);
    }

    public float Evaluate(Vector3 p)
    {
        float r = p.magnitude;

        if (r < 1e-6f)
            return 1f;

        float sphere = r - radius;

        Vector3 n = p / r;

        float theta = Mathf.Atan2(n.z, n.x);              // -pi .. pi
        float phi = Mathf.Acos(Mathf.Clamp(n.y, -1f, 1f)); // 0 .. pi

        float lonSpacing = Mathf.PI * 2f / lonCount;
        float latSpacing = Mathf.PI / latCount;

        float sinPhi = Mathf.Sin(phi);

        // Winkelabstand zur nächsten Längengradlinie
        float lonAngleDist = RepeatCentered(theta, lonSpacing);

        // Winkelabstand zur nächsten Breitengradlinie
        float latAngleDist = RepeatCentered(phi, latSpacing);

        // Umrechnung Winkel -> Oberflächenlänge
        float lonDist = lonAngleDist * radius * sinPhi;
        float latDist = latAngleDist * radius;

        // einzelne Streifen
        float lonStripe = lonDist - halfWidth;
        float latStripe = latDist - halfWidth;

        // Union der Streifen
        float grid = Mathf.Min(lonStripe, latStripe);

        // nur nahe der Kugeloberfläche aktiv
        float surfaceBand = Mathf.Abs(sphere) - depth;

        // Intersection: Grid UND Oberflächenband
        return Mathf.Max(grid, surfaceBand);
    }

    private float RepeatCentered(float v, float spacing)
    {
        float x = v / spacing;
        float nearest = Mathf.Round(x) * spacing;
        return Mathf.Abs(v - nearest);
    }
}