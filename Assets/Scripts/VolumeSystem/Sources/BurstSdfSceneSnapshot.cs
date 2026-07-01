using System;
using System.Collections.Generic;
using System.Diagnostics;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

public readonly struct BurstSdfShapeData
{
    public readonly int ShapeType;
    public readonly float4x4 WorldToLocal;
    public readonly float4x4 LocalToWorld;
    public readonly float SphereRadius;
    public readonly float3 BoxHalfExtents;
    public readonly float TorusMajorRadius;
    public readonly float TorusMinorRadius;
    public readonly float HyperboloidA;
    public readonly float HyperboloidB;
    public readonly float HyperboloidC;
    public readonly int GridType;
    public readonly float GridWidth;
    public readonly float GridDepth;
    public readonly float3 GridSpacing;
    public readonly float3 GridOffset;
    public readonly byte GlobalGridInWorldSpace;
    public readonly int LongitudeCount;
    public readonly int LatitudeCount;
    public readonly int TorusMajorSegments;
    public readonly int TorusMinorSegments;
    public readonly int HyperboloidRadialSegments;
    public readonly int HyperboloidHeightSegments;
    public readonly float HyperboloidHeightMin;
    public readonly float HyperboloidHeightMax;
    public readonly byte UseXLines;
    public readonly byte UseYLines;
    public readonly byte UseZLines;

    internal BurstSdfShapeData(SdfSceneSnapshot.ShapeData source)
    {
        ShapeType = (int)source.ShapeType;
        WorldToLocal = BurstSdfSceneSnapshot.ToFloat4x4(source.WorldToLocal);
        LocalToWorld = BurstSdfSceneSnapshot.ToFloat4x4(source.LocalToWorld);
        SphereRadius = source.SphereRadius;
        BoxHalfExtents = ToFloat3(source.BoxHalfExtents);
        TorusMajorRadius = source.TorusMajorRadius;
        TorusMinorRadius = source.TorusMinorRadius;
        HyperboloidA = source.HyperboloidA;
        HyperboloidB = source.HyperboloidB;
        HyperboloidC = source.HyperboloidC;
        GridType = (int)source.GridType;
        GridWidth = source.GridWidth;
        GridDepth = source.GridDepth;
        GridSpacing = ToFloat3(source.GridSpacing);
        GridOffset = ToFloat3(source.GridOffset);
        GlobalGridInWorldSpace = ToByte(source.GlobalGridInWorldSpace);
        LongitudeCount = source.LongitudeCount;
        LatitudeCount = source.LatitudeCount;
        TorusMajorSegments = source.TorusMajorSegments;
        TorusMinorSegments = source.TorusMinorSegments;
        HyperboloidRadialSegments = source.HyperboloidRadialSegments;
        HyperboloidHeightSegments = source.HyperboloidHeightSegments;
        HyperboloidHeightMin = source.HyperboloidHeightMin;
        HyperboloidHeightMax = source.HyperboloidHeightMax;
        UseXLines = ToByte(source.UseXLines);
        UseYLines = ToByte(source.UseYLines);
        UseZLines = ToByte(source.UseZLines);
    }

    private static float3 ToFloat3(Vector3 value)
    {
        return new float3(value.x, value.y, value.z);
    }

    private static byte ToByte(bool value)
    {
        return value ? (byte)1 : (byte)0;
    }
}

public readonly struct BurstSdfBatchResult
{
    public readonly bool UsedJob;
    public readonly int SampleCount;
    public readonly long ElapsedTicks;

    internal BurstSdfBatchResult(bool usedJob, int sampleCount, long elapsedTicks)
    {
        UsedJob = usedJob;
        SampleCount = sampleCount;
        ElapsedTicks = elapsedTicks;
    }
}

public readonly struct BurstSdfSceneView
{
    public readonly float4x4 RootLocalToWorld;
    public readonly NativeArray<BurstSdfShapeData>.ReadOnly AddShapes;
    public readonly NativeArray<BurstSdfShapeData>.ReadOnly SubtractShapes;
    public readonly NativeArray<BurstSdfShapeData>.ReadOnly IntersectShapes;

    internal BurstSdfSceneView(
        float4x4 rootLocalToWorld,
        NativeArray<BurstSdfShapeData>.ReadOnly addShapes,
        NativeArray<BurstSdfShapeData>.ReadOnly subtractShapes,
        NativeArray<BurstSdfShapeData>.ReadOnly intersectShapes)
    {
        RootLocalToWorld = rootLocalToWorld;
        AddShapes = addShapes;
        SubtractShapes = subtractShapes;
        IntersectShapes = intersectShapes;
    }

    public float Evaluate(float3 rootLocalPoint)
    {
        float3 worldPoint = math.mul(RootLocalToWorld, new float4(rootLocalPoint, 1f)).xyz;
        float result = float.PositiveInfinity;

        for (int i = 0; i < AddShapes.Length; i++)
            result = math.min(result, EvaluateWorld(AddShapes[i], worldPoint));
        for (int i = 0; i < SubtractShapes.Length; i++)
            result = math.max(result, -EvaluateWorld(SubtractShapes[i], worldPoint));
        for (int i = 0; i < IntersectShapes.Length; i++)
            result = math.max(result, EvaluateWorld(IntersectShapes[i], worldPoint));

        return result;
    }

    /// <summary>Batch-evaluate positions into values array.</summary>
    public void Evaluate(NativeArray<float3> positions, NativeArray<float> values)
    {
        for (int i = 0; i < positions.Length; i++)
            values[i] = Evaluate(positions[i]);
    }

    private static float EvaluatePrimitive(BurstSdfShapeData shape, float3 p)
    {
        switch (shape.ShapeType)
        {
            case (int)VolumeShapeType.Box:
                float3 q = math.abs(p) - shape.BoxHalfExtents;
                return math.length(math.max(q, 0f)) + math.min(math.cmax(q), 0f);
            case (int)VolumeShapeType.Torus:
                float2 torusPoint = new float2(math.length(p.xz) - shape.TorusMajorRadius, p.y);
                return math.length(torusPoint) - shape.TorusMinorRadius;
            case (int)VolumeShapeType.Hyperboloid:
                float a = math.max(0.0001f, shape.HyperboloidA);
                float b = math.max(0.0001f, shape.HyperboloidB);
                float c = math.max(0.0001f, shape.HyperboloidC);
                return p.x * p.x / (a * a) + p.z * p.z / (b * b) - p.y * p.y / (c * c) - 1f;
            default:
                return math.length(p) - shape.SphereRadius;
        }
    }

    private static float EvaluateWorld(BurstSdfShapeData shape, float3 worldPoint)
    {
        float3 p = math.mul(shape.WorldToLocal, new float4(worldPoint, 1f)).xyz;
        float d = EvaluatePrimitive(shape, p);

        if (shape.GridType == (int)VolumeGridType.None)
            return d;

        float gridD = EvaluateGridCutter(shape, p, d);
        return math.max(d, -gridD);
    }

    private static float EvaluateShell(float baseDistance, float depth)
    {
        return math.max(baseDistance, -baseDistance - depth);
    }

    private static float RepeatCentered(float v, float spacing)
    {
        spacing = math.max(0.0001f, spacing);
        return v - spacing * math.floor(v / spacing + 0.5f);
    }

    private static float EvaluateGridCutter(BurstSdfShapeData shape, float3 p, float baseDistance)
    {
        float shell = EvaluateShell(baseDistance, shape.GridDepth);
        float width = math.max(0.0001f, shape.GridWidth);
        float depth = math.max(0.0001f, shape.GridDepth);

        float gridD = shape.GridType switch
        {
            (int)VolumeGridType.Global => EvaluateGlobalGrid(shape, p, width),
            (int)VolumeGridType.Sphere => EvaluateSphereGrid(shape, p, width),
            (int)VolumeGridType.Torus => EvaluateTorusGrid(shape, p, width),
            (int)VolumeGridType.Hyperboloid => EvaluateHyperboloidGrid(shape, p, width),
            _ => 1f
        };

        return math.max(gridD, shell);
    }

    private static float EvaluateGlobalGrid(BurstSdfShapeData shape, float3 p, float width)
    {
        float3 samplePoint = shape.GlobalGridInWorldSpace == 1
            ? math.mul(shape.LocalToWorld, new float4(p, 1f)).xyz
            : p;
        float3 q = samplePoint + shape.GridOffset;
        float d = float.PositiveInfinity;

        if (shape.UseXLines == 1)
            d = math.min(d, math.abs(RepeatCentered(q.x, shape.GridSpacing.x)) - width);
        if (shape.UseYLines == 1)
            d = math.min(d, math.abs(RepeatCentered(q.y, shape.GridSpacing.y)) - width);
        if (shape.UseZLines == 1)
            d = math.min(d, math.abs(RepeatCentered(q.z, shape.GridSpacing.z)) - width);

        return d;
    }

    private static float EvaluateSphereGrid(BurstSdfShapeData shape, float3 p, float width)
    {
        float r = math.length(p);
        if (r < 1e-6f)
            return 1f;

        float3 n = p / r;
        float theta = math.atan2(n.z, n.x) + shape.GridOffset.x;
        float phi = math.acos(math.clamp(n.y, -1f, 1f)) + shape.GridOffset.y;
        int lon = math.max(1, shape.LongitudeCount);
        int lat = math.max(1, shape.LatitudeCount);
        float lonSpacing = math.PI * 2f / lon;
        float latSpacing = math.PI / lat;
        float lonDist = math.abs(RepeatCentered(theta, lonSpacing)) * r * math.sin(phi);
        float latDist = math.abs(RepeatCentered(phi, latSpacing)) * r;

        return math.min(lonDist, latDist) - width;
    }

    private static float EvaluateTorusGrid(BurstSdfShapeData shape, float3 p, float width)
    {
        float theta = math.atan2(p.z, p.x) + shape.GridOffset.x;
        float radial = math.length(p.xz);
        float phi = math.atan2(p.y, radial - shape.TorusMajorRadius) + shape.GridOffset.y;
        int major = math.max(1, shape.TorusMajorSegments);
        int minor = math.max(1, shape.TorusMinorSegments);
        float majorSpacing = math.PI * 2f / major;
        float minorSpacing = math.PI * 2f / minor;
        float majorDist = math.abs(RepeatCentered(theta, majorSpacing)) * math.max(0.0001f, shape.TorusMajorRadius);
        float minorDist = math.abs(RepeatCentered(phi, minorSpacing)) * math.max(0.0001f, shape.TorusMinorRadius);

        return math.min(majorDist, minorDist) - width;
    }

    private static float EvaluateHyperboloidGrid(BurstSdfShapeData shape, float3 p, float width)
    {
        float safeA = math.max(0.0001f, shape.HyperboloidA);
        float safeB = math.max(0.0001f, shape.HyperboloidB);
        float theta = math.atan2(p.z / safeB, p.x / safeA) + shape.GridOffset.x;
        int radial = math.max(1, shape.HyperboloidRadialSegments);
        int height = math.max(1, shape.HyperboloidHeightSegments);
        float radialSpacing = math.PI * 2f / radial;
        float heightSpacing = math.max(0.0001f, (shape.HyperboloidHeightMax - shape.HyperboloidHeightMin) / height);
        float rx = p.x / safeA;
        float rz = p.z / safeB;
        float localRadius = math.sqrt(rx * rx + rz * rz);
        float angularScale = math.max(0.0001f, localRadius * math.min(safeA, safeB));
        float radialDist = math.abs(RepeatCentered(theta, radialSpacing)) * angularScale;
        float heightDist = math.abs(RepeatCentered(p.y - shape.HyperboloidHeightMin + shape.GridOffset.y, heightSpacing));

        return math.min(radialDist, heightDist) - width;
    }
}

public sealed class BurstSdfSceneSnapshot : IDisposable
{
    private float4x4 _rootLocalToWorld;
    private NativeArray<BurstSdfShapeData> _addShapes;
    private NativeArray<BurstSdfShapeData> _subtractShapes;
    private NativeArray<BurstSdfShapeData> _intersectShapes;

    private BurstSdfSceneSnapshot()
    {
    }

    public bool IsCreated =>
        _addShapes.IsCreated &&
        _subtractShapes.IsCreated &&
        _intersectShapes.IsCreated;

    public NativeArray<BurstSdfShapeData>.ReadOnly AddShapes => _addShapes.AsReadOnly();
    public NativeArray<BurstSdfShapeData>.ReadOnly SubtractShapes => _subtractShapes.AsReadOnly();
    public NativeArray<BurstSdfShapeData>.ReadOnly IntersectShapes => _intersectShapes.AsReadOnly();

    public BurstSdfSceneView View => new BurstSdfSceneView(
        _rootLocalToWorld,
        _addShapes.AsReadOnly(),
        _subtractShapes.AsReadOnly(),
        _intersectShapes.AsReadOnly());

    public static bool TryCreate(
        SdfSceneSnapshot source,
        Allocator allocator,
        out BurstSdfSceneSnapshot snapshot)
    {
        snapshot = null;
        if (source == null || source.HasUnsupportedShapes)
            return false;

        BurstSdfSceneSnapshot created = new BurstSdfSceneSnapshot
        {
            _rootLocalToWorld = ToFloat4x4(source.RootLocalToWorld)
        };
        try
        {
            created._addShapes = CopyShapes(source.AddShapes, allocator);
            created._subtractShapes = CopyShapes(source.SubtractShapes, allocator);
            created._intersectShapes = CopyShapes(source.IntersectShapes, allocator);
            snapshot = created;
            return true;
        }
        catch
        {
            created.Dispose();
            throw;
        }
    }

    public float Evaluate(float3 rootLocalPoint)
    {
        return View.Evaluate(rootLocalPoint);
    }

    [BurstCompile]
    private struct EvaluateSdfBatchJob : IJobParallelFor
    {
        [ReadOnly] public BurstSdfSceneView Scene;
        [ReadOnly] public NativeArray<float3> Positions;
        public NativeArray<float> Values;

        public void Execute(int index)
        {
            Values[index] = Scene.Evaluate(Positions[index]);
        }
    }

    public BurstSdfBatchResult EvaluateBatch(NativeArray<float3> positions, NativeArray<float> values, int minBatchSize)
    {
        if (positions.Length != values.Length)
            throw new InvalidOperationException("positions and values must have equal length");

        long startTicks = System.Diagnostics.Stopwatch.GetTimestamp();
        int count = positions.Length;
        bool usedJob = false;

        if (count >= minBatchSize)
        {
            usedJob = true;
            EvaluateSdfBatchJob job = new EvaluateSdfBatchJob
            {
                Scene = View,
                Positions = positions,
                Values = values
            };
            job.Schedule(count, 32).Complete();
        }
        else if (count > 0)
        {
            for (int i = 0; i < count; i++)
                values[i] = View.Evaluate(positions[i]);
        }

        long elapsedTicks = System.Diagnostics.Stopwatch.GetTimestamp() - startTicks;
        return new BurstSdfBatchResult(usedJob, count, elapsedTicks);
    }

    public void Dispose()
    {
        if (_addShapes.IsCreated)
            _addShapes.Dispose();
        if (_subtractShapes.IsCreated)
            _subtractShapes.Dispose();
        if (_intersectShapes.IsCreated)
            _intersectShapes.Dispose();

        _addShapes = default;
        _subtractShapes = default;
        _intersectShapes = default;
    }

    internal static float4x4 ToFloat4x4(Matrix4x4 value)
    {
        return new float4x4(
            new float4(value.m00, value.m10, value.m20, value.m30),
            new float4(value.m01, value.m11, value.m21, value.m31),
            new float4(value.m02, value.m12, value.m22, value.m32),
            new float4(value.m03, value.m13, value.m23, value.m33));
    }

    private static NativeArray<BurstSdfShapeData> CopyShapes(
        IReadOnlyList<SdfSceneSnapshot.ShapeData> source,
        Allocator allocator)
    {
        NativeArray<BurstSdfShapeData> copy =
            new NativeArray<BurstSdfShapeData>(source.Count, allocator, NativeArrayOptions.UninitializedMemory);
        for (int i = 0; i < source.Count; i++)
            copy[i] = new BurstSdfShapeData(source[i]);
        return copy;
    }
}
