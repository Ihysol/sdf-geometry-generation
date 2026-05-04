using UnityEngine;

public static class SDFGridCutters
{
    public static float Global(Vector3 p, float body, float spacing, float width, float depth)
    {
        float gx = RepeatCentered(p.x, spacing);
        float gy = RepeatCentered(p.y, spacing);
        float gz = RepeatCentered(p.z, spacing);

        float halfWidth = width * 0.5f;
        float grid = Mathf.Min(gx, Mathf.Min(gy, gz)) - halfWidth;
        float surfaceBand = Mathf.Abs(body) - depth;

        return Mathf.Max(grid, surfaceBand);
    }

    public static float Sphere(
        Vector3 p,
        float sphereRadius,
        float width,
        float depth,
        int longitudeCount,
        int latitudeCount)
    {
        float r = p.magnitude;

        if (r < 1e-6f)
            return 1f;

        float sphere = r - sphereRadius;

        Vector3 n = p / r;
        float theta = Mathf.Atan2(n.z, n.x);
        float phi = Mathf.Acos(Mathf.Clamp(n.y, -1f, 1f));

        longitudeCount = Mathf.Max(1, longitudeCount);
        latitudeCount = Mathf.Max(1, latitudeCount);

        float lonSpacing = Mathf.PI * 2f / longitudeCount;
        float latSpacing = Mathf.PI / latitudeCount;

        float sinPhi = Mathf.Sin(phi);

        float lonAngleDist = RepeatCentered(theta, lonSpacing);
        float latAngleDist = RepeatCentered(phi, latSpacing);

        float halfWidth = width * 0.5f;

        float lonDist = lonAngleDist * sphereRadius * sinPhi;
        float latDist = latAngleDist * sphereRadius;

        float grid = Mathf.Min(lonDist - halfWidth, latDist - halfWidth);
        float surfaceBand = Mathf.Abs(sphere) - depth;

        return Mathf.Max(grid, surfaceBand);
    }

    public static float Torus(
        Vector3 p,
        float body,
        float majorRadius,
        float width,
        float depth,
        int majorCount,
        int minorCount)
    {
        float u = Mathf.Atan2(p.z, p.x);

        float radial = new Vector2(p.x, p.z).magnitude;
        float tubeX = radial - majorRadius;
        float tubeY = p.y;

        float v = Mathf.Atan2(tubeY, tubeX);

        majorCount = Mathf.Max(1, majorCount);
        minorCount = Mathf.Max(1, minorCount);

        float uSpacing = Mathf.PI * 2f / majorCount;
        float vSpacing = Mathf.PI * 2f / minorCount;

        float tubeRadius = Mathf.Max(0.0001f, Mathf.Sqrt(tubeX * tubeX + tubeY * tubeY));

        float uDist = RepeatCentered(u, uSpacing) * majorRadius;
        float vDist = RepeatCentered(v, vSpacing) * tubeRadius;

        float halfWidth = width * 0.5f;
        float grid = Mathf.Min(uDist, vDist) - halfWidth;
        float surfaceBand = Mathf.Abs(body) - depth;

        return Mathf.Max(grid, surfaceBand);
    }

    private static float RepeatCentered(float v, float spacing)
    {
        spacing = Mathf.Max(1e-6f, spacing);

        float x = v / spacing;
        float nearest = Mathf.Round(x) * spacing;
        return Mathf.Abs(v - nearest);
    }
}