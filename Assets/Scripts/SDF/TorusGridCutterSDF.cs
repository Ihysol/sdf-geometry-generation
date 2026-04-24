using UnityEngine;

public class TorusGridCutterSDF : ISDF
{
    private readonly ISDF body;
    private readonly float majorRadius;
    private readonly float width;
    private readonly float depth;
    private readonly int majorCount;
    private readonly int minorCount;

    public TorusGridCutterSDF(
        ISDF body,
        float majorRadius,
        float width,
        float depth,
        int majorCount,
        int minorCount)
    {
        this.body = body;
        this.majorRadius = majorRadius;
        this.width = width;
        this.depth = depth;
        this.majorCount = Mathf.Max(1, majorCount);
        this.minorCount = Mathf.Max(1, minorCount);
    }

    public float Evaluate(Vector3 p)
    {
        float bodyDistance = body.Evaluate(p);

        // Torus main angle around Y axis
        float u = Mathf.Atan2(p.z, p.x);

        float radial = new Vector2(p.x, p.z).magnitude;

        // Tube-local coordinate:
        // x = distance from tube center circle
        // y = height
        float tubeX = radial - majorRadius;
        float tubeY = p.y;

        // Angle around tube cross-section
        float v = Mathf.Atan2(tubeY, tubeX);

        float uSpacing = Mathf.PI * 2f / majorCount;
        float vSpacing = Mathf.PI * 2f / minorCount;

        // Convert angular distance to approximate surface distance
        float uDist = RepeatDistance(u, uSpacing) * majorRadius;
        float vDist = RepeatDistance(v, vSpacing) * Mathf.Max(0.0001f, Mathf.Sqrt(tubeX * tubeX + tubeY * tubeY));

        float grid = Mathf.Min(uDist, vDist) - width;

        // Only active close to actual torus surface
        float surfaceBand = Mathf.Abs(bodyDistance) - depth;

        return Mathf.Max(grid, surfaceBand);
    }

    private float RepeatDistance(float value, float spacing)
    {
        float x = value / spacing;
        float nearest = Mathf.Round(x) * spacing;
        return Mathf.Abs(value - nearest);
    }
}