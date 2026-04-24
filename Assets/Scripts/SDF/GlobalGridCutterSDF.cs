using UnityEngine;

public class GlobalGridCutterSDF : ISDF
{
    private readonly ISDF body;
    private readonly float width;
    private readonly float depth;
    private readonly float spacing;

    public GlobalGridCutterSDF(ISDF body, float width, float depth, float spacing)
    {
        this.body = body;
        this.width = width;
        this.depth = depth;
        this.spacing = spacing;
    }

    public float Evaluate(Vector3 p)
    {
        // 3D grid planes in object/local space
        float gx = RepeatDistance(p.x, spacing);
        float gy = RepeatDistance(p.y, spacing);
        float gz = RepeatDistance(p.z, spacing);

        // any coordinate near a repeated grid plane becomes a cut
        float grid = Mathf.Min(gx, Mathf.Min(gy, gz)) - width;

        // restrict cutter to the actual body surface
        float surfaceBand = Mathf.Abs(body.Evaluate(p)) - depth;

        return Mathf.Max(grid, surfaceBand);
    }

    private float RepeatDistance(float value, float spacing)
    {
        float x = value / spacing;
        float nearest = Mathf.Round(x) * spacing;
        return Mathf.Abs(value - nearest);
    }
}