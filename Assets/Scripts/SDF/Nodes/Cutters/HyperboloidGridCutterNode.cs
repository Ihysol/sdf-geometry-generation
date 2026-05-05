using UnityEngine;

[CreateAssetMenu(menuName = "SDF/Cutters/Surface Hyperboloid Grid Cutter")]
public class HyperboloidGridCutterNode : SDFCutterNode
{
    [Header("Hyperboloid Reference")]
    public float a = 1f;
    public float b = 1f;

    [Header("Height Range")]
    public float heightMin = -2f;
    public float heightMax = 2f;

    [Header("Grid")]
    public int radialSegments = 24;
    public int heightSegments = 12;
    public float radialOffset = 0f;
    public float heightOffset = 0f;

    [Header("Groove")]
    public float width = 0.05f;
    public float depth = 0.15f;

    public override float Evaluate(Vector3 p, SDFNode baseShape)
    {
        float baseD = baseShape.Evaluate(p);

        // nur in Oberflächenschale aktiv
        float shell = Mathf.Max(baseD, -baseD - depth);

        float safeA = Mathf.Max(0.0001f, a);
        float safeB = Mathf.Max(0.0001f, b);

        float theta = Mathf.Atan2(p.z / safeB, p.x / safeA) + radialOffset;

        int radial = Mathf.Max(1, radialSegments);
        int height = Mathf.Max(1, heightSegments);

        float radialSpacing = Mathf.PI * 2f / radial;
        float heightSpacing = Mathf.Max(0.0001f, (heightMax - heightMin) / height);

        float rx = p.x / safeA;
        float rz = p.z / safeB;
        float localRadius = Mathf.Sqrt(rx * rx + rz * rz);

        float angularScale = Mathf.Max(0.0001f, localRadius * Mathf.Min(safeA, safeB));

        float radialLineDist = Mathf.Abs(RepeatCentered(theta, radialSpacing)) * angularScale;
        float heightLineDist = Mathf.Abs(RepeatCentered(p.y - heightMin + heightOffset, heightSpacing));

        float gridD = Mathf.Min(radialLineDist, heightLineDist) - width;

        return Mathf.Max(gridD, shell);
    }

    private float RepeatCentered(float v, float spacing)
    {
        spacing = Mathf.Max(0.0001f, spacing);
        return v - spacing * Mathf.Floor(v / spacing + 0.5f);
    }
}