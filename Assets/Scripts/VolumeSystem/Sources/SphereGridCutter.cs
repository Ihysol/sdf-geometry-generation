using UnityEngine;

[CreateAssetMenu(menuName = "SDF/Cutters/Surface Sphere Grid Cutter")]
public class SphereGridCutter : SDFCutter
{
    [Header("Grid")]
    public int longitudeCount = 16;
    public int latitudeCount = 8;
    public float angularOffset = 0f;
    public float latitudeOffset = 0f;

    [Header("Groove")]
    public float width = 0.02f;
    public float depth = 0.04f;

    /// <summary>Evaluates longitude and latitude grooves inside a sphere surface shell.</summary>
    public override float Evaluate(Vector3 p, SDF baseShape)
    {
        float baseD = baseShape.Evaluate(p);

        // nur in Oberflächenschale aktiv
        float shell = Mathf.Max(baseD, -baseD - depth);

        float r = p.magnitude;
        if (r < 1e-6f)
            return 1f;

        Vector3 n = p / r;

        float theta = Mathf.Atan2(n.z, n.x) + angularOffset;
        float phi = Mathf.Acos(Mathf.Clamp(n.y, -1f, 1f)) + latitudeOffset;

        int lon = Mathf.Max(1, longitudeCount);
        int lat = Mathf.Max(1, latitudeCount);

        float lonSpacing = Mathf.PI * 2f / lon;
        float latSpacing = Mathf.PI / lat;

        float lonDist = Mathf.Abs(RepeatCentered(theta, lonSpacing)) * r * Mathf.Sin(phi);
        float latDist = Mathf.Abs(RepeatCentered(phi, latSpacing)) * r;

        float gridD = Mathf.Min(lonDist, latDist) - width;

        return Mathf.Max(gridD, shell);
    }

    /// <summary>Repeats an angular coordinate around zero with the given spacing.</summary>
    private float RepeatCentered(float v, float spacing)
    {
        spacing = Mathf.Max(0.0001f, spacing);
        return v - spacing * Mathf.Floor(v / spacing + 0.5f);
    }
}
