using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;

/// <summary>Burst-compatible scalar representation of a volume shape for parallel SDF sampling.</summary>
public readonly struct BurstShapeData
{
    public readonly int ShapeType;       // VolumeShapeType enum
    public readonly int Role;            // VolumeOperationRole enum
    // worldToLocalMatrix as 12 scalars (3x4, row-major)
    public readonly float M00, M01, M02, M03;
    public readonly float M10, M11, M12, M13;
    public readonly float M20, M21, M22, M23;
    // localToWorldMatrix for global grid transform (same 12 scalars)
    public readonly float Lt00, Lt01, Lt02, Lt03;
    public readonly float Lt10, Lt11, Lt12, Lt13;
    public readonly float Lt20, Lt21, Lt22, Lt23;
    // Shape params
    public readonly float SphereRadius;
    public readonly float BoxHx, BoxHy, BoxHz;
    public readonly float TorusMajorR, TorusMinorR;
    public readonly float HyperA, HyperB, HyperC;
    // Grid params
    public readonly int GridType;
    public readonly float GridWidth, GridDepth;
    public readonly float GridSx, GridSy, GridSz;
    public readonly float GridOx, GridOy, GridOz;
    public readonly bool GlobalGridInWorldSpace;
    public readonly int LongitudeCount, LatitudeCount;
    public readonly int TorusMajorSeg, TorusMinorSeg;
    public readonly int HyperRadialSeg, HyperHeightSeg;
    public readonly float HyperHMin, HyperHMax;
    public readonly bool UseXLines, UseYLines, UseZLines;

    public BurstShapeData(SdfSceneSnapshot.ShapeData sd, int role)
    {
        ShapeType = (int)sd.ShapeType;
        Role = role;

        M00 = sd.WorldToLocal.m00; M01 = sd.WorldToLocal.m01; M02 = sd.WorldToLocal.m02; M03 = sd.WorldToLocal.m03;
        M10 = sd.WorldToLocal.m10; M11 = sd.WorldToLocal.m11; M12 = sd.WorldToLocal.m12; M13 = sd.WorldToLocal.m13;
        M20 = sd.WorldToLocal.m20; M21 = sd.WorldToLocal.m21; M22 = sd.WorldToLocal.m22; M23 = sd.WorldToLocal.m23;

        Lt00 = sd.LocalToWorld.m00; Lt01 = sd.LocalToWorld.m01; Lt02 = sd.LocalToWorld.m02; Lt03 = sd.LocalToWorld.m03;
        Lt10 = sd.LocalToWorld.m10; Lt11 = sd.LocalToWorld.m11; Lt12 = sd.LocalToWorld.m12; Lt13 = sd.LocalToWorld.m13;
        Lt20 = sd.LocalToWorld.m20; Lt21 = sd.LocalToWorld.m21; Lt22 = sd.LocalToWorld.m22; Lt23 = sd.LocalToWorld.m23;

        SphereRadius = sd.SphereRadius;
        BoxHx = sd.BoxHalfExtents.x; BoxHy = sd.BoxHalfExtents.y; BoxHz = sd.BoxHalfExtents.z;
        TorusMajorR = sd.TorusMajorRadius; TorusMinorR = sd.TorusMinorRadius;
        HyperA = sd.HyperboloidA; HyperB = sd.HyperboloidB; HyperC = sd.HyperboloidC;

        GridType = (int)sd.GridType;
        GridWidth = sd.GridWidth; GridDepth = sd.GridDepth;

        GridSx = sd.GridSpacing.x; GridSy = sd.GridSpacing.y; GridSz = sd.GridSpacing.z;
        GridOx = sd.GridOffset.x; GridOy = sd.GridOffset.y; GridOz = sd.GridOffset.z;
        GlobalGridInWorldSpace = sd.GlobalGridInWorldSpace;
        LongitudeCount = sd.LongitudeCount; LatitudeCount = sd.LatitudeCount;
        TorusMajorSeg = sd.TorusMajorSegments; TorusMinorSeg = sd.TorusMinorSegments;
        HyperRadialSeg = sd.HyperboloidRadialSegments; HyperHeightSeg = sd.HyperboloidHeightSegments;
        HyperHMin = sd.HyperboloidHeightMin; HyperHMax = sd.HyperboloidHeightMax;
        UseXLines = sd.UseXLines; UseYLines = sd.UseYLines; UseZLines = sd.UseZLines;
    }

    /// <summary>Evaluate SDF at world-space point (scalar, zero-alloc).</summary>
    public float Evaluate(float wx, float wy, float wz)
    {
        // Transform to local space — inlined MultiplyPoint3x4
        float lx = M00 * wx + M01 * wy + M02 * wz + M03;
        float ly = M10 * wx + M11 * wy + M12 * wz + M13;
        float lz = M20 * wx + M21 * wy + M22 * wz + M23;

        float d = EvaluateShapeScalar(lx, ly, lz);

        if (GridType != 0) // VolumeGridType.None == 0
        {
            float cutter = EvaluateGridCutterScalar(lx, ly, lz, d);
            d = Mathf.Max(d, -cutter);
        }

        return d;
    }

    private float EvaluateShapeScalar(float x, float y, float z)
    {
        switch (ShapeType)
        {
            case 0: // Sphere
                return Mathf.Sqrt(x * x + y * y + z * z) - SphereRadius;
            case 1: // Box
            {
                float dx = Mathf.Abs(x) - BoxHx;
                float dy = Mathf.Abs(y) - BoxHy;
                float dz = Mathf.Abs(z) - BoxHz;
                float ox = Mathf.Max(dx, 0f), oy = Mathf.Max(dy, 0f), oz = Mathf.Max(dz, 0f);
                return Mathf.Sqrt(ox * ox + oy * oy + oz * oz) + Mathf.Min(Mathf.Max(dx, Mathf.Max(dy, dz)), 0f);
            }
            case 2: // Torus
            {
                float radial = Mathf.Sqrt(x * x + z * z) - TorusMajorR;
                return Mathf.Sqrt(radial * radial + y * y) - TorusMinorR;
            }
            case 3: // Hyperboloid
            {
                float a = Mathf.Max(0.0001f, HyperA);
                float b = Mathf.Max(0.0001f, HyperB);
                float c = Mathf.Max(0.0001f, HyperC);
                return (x * x) / (a * a) + (z * z) / (b * b) - (y * y) / (c * c) - 1f;
            }
            default:
                return 1f; // CustomAsset fallback
        }
    }

    private float EvaluateGridCutterScalar(float x, float y, float z, float baseDistance)
    {
        float shell = Mathf.Max(baseDistance, -baseDistance - GridDepth);
        float gridD;

        switch (GridType)
        {
            case 1: // Global
            {
                float gx = x, gy = y, gz = z;
                if (GlobalGridInWorldSpace)
                {
                    gx = Lt00 * x + Lt01 * y + Lt02 * z + Lt03;
                    gy = Lt10 * x + Lt11 * y + Lt12 * z + Lt13;
                    gz = Lt20 * x + Lt21 * y + Lt22 * z + Lt23;
                }
                float qx = gx + GridOx, qy = gy + GridOy, qz = gz + GridOz;
                gridD = float.PositiveInfinity;
                if (UseXLines) gridD = Mathf.Min(gridD, Mathf.Abs(RepeatCentered(qx, GridSx)) - GridWidth);
                if (UseYLines) gridD = Mathf.Min(gridD, Mathf.Abs(RepeatCentered(qy, GridSy)) - GridWidth);
                if (UseZLines) gridD = Mathf.Min(gridD, Mathf.Abs(RepeatCentered(qz, GridSz)) - GridWidth);
                break;
            }
            case 2: // Sphere
            {
                float r = Mathf.Sqrt(x * x + y * y + z * z);
                if (r < 1e-6f) return shell;
                float nx = x / r, ny = y / r, nz = z / r;
                float theta = Mathf.Atan2(nz, nx) + GridOx;
                float phi = Mathf.Acos(Mathf.Clamp(ny, -1f, 1f)) + GridOy;
                int lon = Mathf.Max(1, LongitudeCount);
                int lat = Mathf.Max(1, LatitudeCount);
                float lonD = Mathf.Abs(RepeatCentered(theta, Mathf.PI * 2f / lon)) * r * Mathf.Sin(phi);
                float latD = Mathf.Abs(RepeatCentered(phi, Mathf.PI / lat)) * r;
                gridD = Mathf.Min(lonD, latD) - GridWidth;
                break;
            }
            case 3: // Torus
            {
                float theta = Mathf.Atan2(z, x) + GridOx;
                float radial = Mathf.Sqrt(x * x + z * z);
                float phi = Mathf.Atan2(y, radial - TorusMajorR) + GridOy;
                int major = Mathf.Max(1, TorusMajorSeg);
                int minor = Mathf.Max(1, TorusMinorSeg);
                float majD = Mathf.Abs(RepeatCentered(theta, Mathf.PI * 2f / major)) * Mathf.Max(0.0001f, TorusMajorR);
                float minD = Mathf.Abs(RepeatCentered(phi, Mathf.PI * 2f / minor)) * Mathf.Max(0.0001f, TorusMinorR);
                gridD = Mathf.Min(majD, minD) - GridWidth;
                break;
            }
            case 4: // Hyperboloid
            {
                float safeA = Mathf.Max(0.0001f, HyperA);
                float safeB = Mathf.Max(0.0001f, HyperB);
                float theta = Mathf.Atan2(z / safeB, x / safeA) + GridOx;
                int radial = Mathf.Max(1, HyperRadialSeg);
                int height = Mathf.Max(1, HyperHeightSeg);
                float rSpacing = Mathf.PI * 2f / radial;
                float hSpacing = Mathf.Max(0.0001f, (HyperHMax - HyperHMin) / height);
                float rx = x / safeA, rz = z / safeB;
                float lrad = Mathf.Sqrt(rx * rx + rz * rz);
                float ascale = Mathf.Max(0.0001f, lrad * Mathf.Min(safeA, safeB));
                float rDist = Mathf.Abs(RepeatCentered(theta, rSpacing)) * ascale;
                float hDist = Mathf.Abs(RepeatCentered(y - HyperHMin + GridOy, hSpacing));
                gridD = Mathf.Min(rDist, hDist) - GridWidth;
                break;
            }
            default:
                return shell;
        }

        return Mathf.Max(gridD, shell);
    }

    private static float RepeatCentered(float v, float spacing)
    {
        spacing = Mathf.Max(0.0001f, spacing);
        return v - spacing * Mathf.Floor(v / spacing + 0.5f);
    }
}

/// <summary>Burst-compiled parallel job: samples SDF into density + material NativeArrays.</summary>
[BurstCompile]
public struct BurstSdfSamplingJob : IJobParallelFor
{
    [ReadOnly] public NativeArray<BurstShapeData> Shapes;
    [ReadOnly] public int ShapeCount;

    // Grid dimensions
    public int Rx, Ry, Rz;
    // Region to sample (for partial rebuilds)
    public int MinX, MaxX, MinY, MaxY, MinZ, MaxZ;
    // Cell size + world offset
    public float CellSize;
    public float WorldMinX, WorldMinY, WorldMinZ;

    [NativeDisableParallelForRestriction] public NativeArray<float> Density;
    [NativeDisableParallelForRestriction] public NativeArray<int> Material;

    // Strides into the target arrays (may be full grid or sub-region)
    public int OutRowStride;
    public int OutSliceStride;

    public void Execute(int i)
    {
        int sx = MaxX - MinX + 1;
        int sy = MaxY - MinY + 1;
        int dx = i % sx;
        int rem = i / sx;
        int dy = rem % sy;
        int dz = rem / sy;

        int x = MinX + dx;
        int y = MinY + dy;
        int z = MinZ + dz;

        // World position of this cell center
        float wx = WorldMinX + x * CellSize;
        float wy = WorldMinY + y * CellSize;
        float wz = WorldMinZ + z * CellSize;

        float result = float.PositiveInfinity;

        for (int s = 0; s < ShapeCount; s++)
        {
            BurstShapeData shape = Shapes[s];
            float d = shape.Evaluate(wx, wy, wz);

            switch (shape.Role)
            {
                case 1: // Subtract
                    result = Mathf.Max(result, -d);
                    break;
                case 2: // Intersect
                    result = Mathf.Max(result, d);
                    break;
                default: // Add / Union (0)
                    result = Mathf.Min(result, d);
                    break;
            }
        }

        // Clamp infinity/NaN to safe value
        if (float.IsInfinity(result) || float.IsNaN(result))
            result = 1f;

        int idx = z * OutSliceStride + y * OutRowStride + x;
        Density[idx] = result;
        Material[idx] = (result <= 0f) ? 1 : 0;
    }
}
