using UnityEngine;

[CreateAssetMenu(menuName = "SDF/Cutters/Global Grid Cutter")]
public class GlobalGridCutter : SDFCutter
{
    public float depth = 0.02f;
    public float width = 0.02f;
    public Vector3 spacing = new Vector3(0.5f, 0.5f, 0.5f);
    public Vector3 offset = Vector3.zero;

    public bool useXLines = true;
    public bool useYLines = true;
    public bool useZLines = true;

    /// <summary>Evaluates axis-aligned grid cuts inside the base surface shell.</summary>
    public override float Evaluate(Vector3 p, SDF baseShape)
    {
        float baseD = baseShape.Evaluate(p);

        float shell = Mathf.Max(baseD, -baseD - depth);

        Vector3 q = p + offset;

        float gridD = float.PositiveInfinity;

        if (useXLines)
            gridD = Mathf.Min(gridD, Mathf.Abs(Repeat(q.x, spacing.x)) - width);

        if (useYLines)
            gridD = Mathf.Min(gridD, Mathf.Abs(Repeat(q.y, spacing.y)) - width);

        if (useZLines)
            gridD = Mathf.Min(gridD, Mathf.Abs(Repeat(q.z, spacing.z)) - width);

        return Mathf.Max(gridD, shell);
    }

    /// <summary>Repeats a coordinate around zero with the given spacing.</summary>
    private float Repeat(float v, float s)
    {
        s = Mathf.Max(0.0001f, s);
        return v - s * Mathf.Floor(v / s + 0.5f);
    }
}
