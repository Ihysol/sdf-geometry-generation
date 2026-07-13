using UnityEngine;

[CreateAssetMenu(menuName = "SDF/Cutters/Surface Rotational Ellipsoid Grid Cutter")]
public class RotationalEllipsoidGridCutter : SDFCutter
{
    [Header("Ellipsoid Reference")]
    public float radialRadius = 2f;
    public float verticalRadius = 1f;

    [Header("Grid")]
    public int profileAngleSegments = 12;
    public int longitudeSegments = 0;
    public float profileOffset = 0f;
    public float longitudeOffset = 0f;
    public bool useProfileLines = true;
    public bool useLongitudeLines = false;

    [Header("Groove")]
    public float width = 0.02f;
    public float depth = 0.04f;

    /// <summary>Evaluates profile-angle and optional longitude grooves inside an ellipsoid surface shell.</summary>
    public override float Evaluate(Vector3 p, SDF baseShape)
    {
        float baseD = baseShape.Evaluate(p);
        float shell = Mathf.Max(baseD, -baseD - depth);

        float safeRadial = Mathf.Max(0.0001f, radialRadius);
        float safeVertical = Mathf.Max(0.0001f, verticalRadius);
        float radial = new Vector2(p.x, p.z).magnitude;

        float gridD = float.PositiveInfinity;

        if (useProfileLines)
        {
            int profileSegments = Mathf.Max(1, profileAngleSegments);
            float profileSpacing = Mathf.PI / profileSegments;
            float profileAngle = Mathf.Atan2(radial / safeRadial, p.y / safeVertical) + profileOffset;
            float profileScale = Mathf.Max(0.0001f, Mathf.Sqrt(radial * radial + p.y * p.y));
            float profileDist = Mathf.Abs(RepeatCentered(profileAngle, profileSpacing)) * profileScale;
            gridD = Mathf.Min(gridD, profileDist - width);
        }

        if (useLongitudeLines && longitudeSegments > 0)
        {
            int longitudes = Mathf.Max(1, longitudeSegments);
            float longitudeSpacing = Mathf.PI * 2f / longitudes;
            float longitudeAngle = Mathf.Atan2(p.z, p.x) + longitudeOffset;
            float longitudeDist = Mathf.Abs(RepeatCentered(longitudeAngle, longitudeSpacing)) *
                                  Mathf.Max(0.0001f, radial);
            gridD = Mathf.Min(gridD, longitudeDist - width);
        }

        if (float.IsPositiveInfinity(gridD))
            gridD = 1f;

        return Mathf.Max(gridD, shell);
    }

    /// <summary>Repeats an angular coordinate around zero with the given spacing.</summary>
    private static float RepeatCentered(float v, float spacing)
    {
        spacing = Mathf.Max(0.0001f, spacing);
        return v - spacing * Mathf.Floor(v / spacing + 0.5f);
    }
}
