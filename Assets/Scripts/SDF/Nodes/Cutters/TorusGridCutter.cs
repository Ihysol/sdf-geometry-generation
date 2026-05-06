using UnityEngine;

[CreateAssetMenu(menuName = "SDF/Cutters/Surface Torus Grid Cutter")]
public class TorusGridCutter : SDFCutter
{
    [Header("Torus Reference")]
    public float majorRadius = 1.5f;
    public float minorRadius = 0.4f;

    [Header("Grid")]
    public int majorSegments = 24;
    public int minorSegments = 12;
    public float majorOffset = 0f;
    public float minorOffset = 0f;

    [Header("Groove")]
    public float width = 0.02f;
    public float depth = 0.04f;

    public override float Evaluate(Vector3 p, SDF baseShape)
    {
        float baseD = baseShape.Evaluate(p);

        // nur nahe Oberfläche und nach innen aktiv
        float shell = Mathf.Max(baseD, -baseD - depth);

        float theta = Mathf.Atan2(p.z, p.x) + majorOffset;

        float radial = new Vector2(p.x, p.z).magnitude;
        float phi = Mathf.Atan2(p.y, radial - majorRadius) + minorOffset;

        int major = Mathf.Max(1, majorSegments);
        int minor = Mathf.Max(1, minorSegments);

        float majorSpacing = Mathf.PI * 2f / major;
        float minorSpacing = Mathf.PI * 2f / minor;

        float majorLineDist = Mathf.Abs(RepeatCentered(theta, majorSpacing)) * Mathf.Max(0.0001f, majorRadius);
        float minorLineDist = Mathf.Abs(RepeatCentered(phi, minorSpacing)) * Mathf.Max(0.0001f, minorRadius);

        float gridD = Mathf.Min(majorLineDist, minorLineDist) - width;

        return Mathf.Max(gridD, shell);
    }

    private float RepeatCentered(float v, float spacing)
    {
        spacing = Mathf.Max(0.0001f, spacing);
        return v - spacing * Mathf.Floor(v / spacing + 0.5f);
    }
}