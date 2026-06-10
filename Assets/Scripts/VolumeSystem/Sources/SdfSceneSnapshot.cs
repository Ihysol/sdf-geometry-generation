using System.Collections.Generic;
using UnityEngine;

public sealed class SdfSceneSnapshot : IScalarFieldSource
{
    public readonly struct ShapeData
    {
        public readonly VolumeShapeType ShapeType;
        public readonly Matrix4x4 WorldToLocal;
        public readonly Matrix4x4 LocalToWorld;
        public readonly float SphereRadius;
        public readonly Vector3 BoxHalfExtents;
        public readonly float TorusMajorRadius;
        public readonly float TorusMinorRadius;
        public readonly float HyperboloidA;
        public readonly float HyperboloidB;
        public readonly float HyperboloidC;
        public readonly VolumeGridType GridType;
        public readonly float GridWidth;
        public readonly float GridDepth;
        public readonly Vector3 GridSpacing;
        public readonly Vector3 GridOffset;
        public readonly bool GlobalGridInWorldSpace;
        public readonly int LongitudeCount;
        public readonly int LatitudeCount;
        public readonly int TorusMajorSegments;
        public readonly int TorusMinorSegments;
        public readonly int HyperboloidRadialSegments;
        public readonly int HyperboloidHeightSegments;
        public readonly float HyperboloidHeightMin;
        public readonly float HyperboloidHeightMax;
        public readonly bool UseXLines;
        public readonly bool UseYLines;
        public readonly bool UseZLines;
        public readonly bool IsSupported;

        public ShapeData(VolumeObject obj, float minSamplingCellSize)
        {
            ShapeType = obj.shapeType;
            WorldToLocal = obj.transform.worldToLocalMatrix;
            LocalToWorld = obj.transform.localToWorldMatrix;
            SphereRadius = obj.sphereRadius;
            BoxHalfExtents = obj.boxHalfExtents;
            TorusMajorRadius = obj.torusMajorRadius;
            TorusMinorRadius = obj.torusMinorRadius;
            HyperboloidA = obj.hyperboloidA;
            HyperboloidB = obj.hyperboloidB;
            HyperboloidC = obj.hyperboloidC;
            GridType = obj.gridType;

            float gridWidth = Mathf.Max(0.0001f, obj.gridWidth);
            float gridDepth = Mathf.Max(0.0001f, obj.gridDepth);
            if (obj.autoClampGridToSampling && minSamplingCellSize > 0f)
            {
                gridWidth = Mathf.Max(gridWidth, minSamplingCellSize * 0.55f);
                gridDepth = Mathf.Max(gridDepth, minSamplingCellSize * 0.75f);
            }
            GridWidth = gridWidth;
            GridDepth = gridDepth;

            GridSpacing = obj.gridSpacing;
            GridOffset = obj.gridOffset;
            GlobalGridInWorldSpace = obj.globalGridInWorldSpace;
            LongitudeCount = obj.longitudeCount;
            LatitudeCount = obj.latitudeCount;
            TorusMajorSegments = obj.torusMajorSegments;
            TorusMinorSegments = obj.torusMinorSegments;
            HyperboloidRadialSegments = obj.hyperboloidRadialSegments;
            HyperboloidHeightSegments = obj.hyperboloidHeightSegments;
            HyperboloidHeightMin = obj.hyperboloidHeightMin;
            HyperboloidHeightMax = obj.hyperboloidHeightMax;
            UseXLines = obj.useXLines;
            UseYLines = obj.useYLines;
            UseZLines = obj.useZLines;
            IsSupported = obj.shapeType != VolumeShapeType.CustomAsset;
        }

        public float EvaluateWorld(Vector3 worldPoint)
        {
            if (!IsSupported)
                return 1f;

            Vector3 p = WorldToLocal.MultiplyPoint3x4(worldPoint);
            float d = EvaluateShape(p);
            if (GridType == VolumeGridType.None)
                return d;

            float cutter = EvaluateGridCutter(p, d);
            return Mathf.Max(d, -cutter);
        }

        private float EvaluateShape(Vector3 p)
        {
            switch (ShapeType)
            {
                case VolumeShapeType.Box:
                    return Box(p, BoxHalfExtents);
                case VolumeShapeType.Torus:
                    Vector2 q = new Vector2(new Vector2(p.x, p.z).magnitude - TorusMajorRadius, p.y);
                    return q.magnitude - TorusMinorRadius;
                case VolumeShapeType.Hyperboloid:
                    float a = Mathf.Max(0.0001f, HyperboloidA);
                    float b = Mathf.Max(0.0001f, HyperboloidB);
                    float c = Mathf.Max(0.0001f, HyperboloidC);
                    return (p.x * p.x) / (a * a) + (p.z * p.z) / (b * b) - (p.y * p.y) / (c * c) - 1f;
                case VolumeShapeType.Sphere:
                default:
                    return p.magnitude - SphereRadius;
            }
        }

        private float EvaluateGridCutter(Vector3 p, float baseDistance)
        {
            float shell = Mathf.Max(baseDistance, -baseDistance - GridDepth);
            float gridD = GridType switch
            {
                VolumeGridType.Global => EvaluateGlobalGrid(p, GridWidth),
                VolumeGridType.Sphere => EvaluateSphereGrid(p, GridWidth),
                VolumeGridType.Torus => EvaluateTorusGrid(p, GridWidth),
                VolumeGridType.Hyperboloid => EvaluateHyperboloidGrid(p, GridWidth),
                _ => 1f
            };

            return Mathf.Max(gridD, shell);
        }

        private float EvaluateGlobalGrid(Vector3 p, float width)
        {
            Vector3 samplePoint = GlobalGridInWorldSpace ? LocalToWorld.MultiplyPoint3x4(p) : p;
            Vector3 q = samplePoint + GridOffset;
            float d = float.PositiveInfinity;

            if (UseXLines)
                d = Mathf.Min(d, Mathf.Abs(RepeatCentered(q.x, GridSpacing.x)) - width);
            if (UseYLines)
                d = Mathf.Min(d, Mathf.Abs(RepeatCentered(q.y, GridSpacing.y)) - width);
            if (UseZLines)
                d = Mathf.Min(d, Mathf.Abs(RepeatCentered(q.z, GridSpacing.z)) - width);

            return d;
        }

        private float EvaluateSphereGrid(Vector3 p, float width)
        {
            float r = p.magnitude;
            if (r < 1e-6f)
                return 1f;

            Vector3 n = p / r;
            float theta = Mathf.Atan2(n.z, n.x) + GridOffset.x;
            float phi = Mathf.Acos(Mathf.Clamp(n.y, -1f, 1f)) + GridOffset.y;
            int lon = Mathf.Max(1, LongitudeCount);
            int lat = Mathf.Max(1, LatitudeCount);
            float lonSpacing = Mathf.PI * 2f / lon;
            float latSpacing = Mathf.PI / lat;
            float lonDist = Mathf.Abs(RepeatCentered(theta, lonSpacing)) * r * Mathf.Sin(phi);
            float latDist = Mathf.Abs(RepeatCentered(phi, latSpacing)) * r;

            return Mathf.Min(lonDist, latDist) - width;
        }

        private float EvaluateTorusGrid(Vector3 p, float width)
        {
            float theta = Mathf.Atan2(p.z, p.x) + GridOffset.x;
            float radial = new Vector2(p.x, p.z).magnitude;
            float phi = Mathf.Atan2(p.y, radial - TorusMajorRadius) + GridOffset.y;
            int major = Mathf.Max(1, TorusMajorSegments);
            int minor = Mathf.Max(1, TorusMinorSegments);
            float majorSpacing = Mathf.PI * 2f / major;
            float minorSpacing = Mathf.PI * 2f / minor;
            float majorDist = Mathf.Abs(RepeatCentered(theta, majorSpacing)) * Mathf.Max(0.0001f, TorusMajorRadius);
            float minorDist = Mathf.Abs(RepeatCentered(phi, minorSpacing)) * Mathf.Max(0.0001f, TorusMinorRadius);

            return Mathf.Min(majorDist, minorDist) - width;
        }

        private float EvaluateHyperboloidGrid(Vector3 p, float width)
        {
            float safeA = Mathf.Max(0.0001f, HyperboloidA);
            float safeB = Mathf.Max(0.0001f, HyperboloidB);
            float theta = Mathf.Atan2(p.z / safeB, p.x / safeA) + GridOffset.x;
            int radial = Mathf.Max(1, HyperboloidRadialSegments);
            int height = Mathf.Max(1, HyperboloidHeightSegments);
            float radialSpacing = Mathf.PI * 2f / radial;
            float heightSpacing = Mathf.Max(0.0001f, (HyperboloidHeightMax - HyperboloidHeightMin) / height);
            float rx = p.x / safeA;
            float rz = p.z / safeB;
            float localRadius = Mathf.Sqrt(rx * rx + rz * rz);
            float angularScale = Mathf.Max(0.0001f, localRadius * Mathf.Min(safeA, safeB));
            float radialDist = Mathf.Abs(RepeatCentered(theta, radialSpacing)) * angularScale;
            float heightDist = Mathf.Abs(RepeatCentered(p.y - HyperboloidHeightMin + GridOffset.y, heightSpacing));

            return Mathf.Min(radialDist, heightDist) - width;
        }

        private static float Box(Vector3 p, Vector3 halfExtents)
        {
            Vector3 q = new Vector3(Mathf.Abs(p.x), Mathf.Abs(p.y), Mathf.Abs(p.z)) - halfExtents;
            Vector3 outside = Vector3.Max(q, Vector3.zero);
            return outside.magnitude + Mathf.Min(Mathf.Max(q.x, Mathf.Max(q.y, q.z)), 0f);
        }

        private static float RepeatCentered(float v, float spacing)
        {
            spacing = Mathf.Max(0.0001f, spacing);
            return v - spacing * Mathf.Floor(v / spacing + 0.5f);
        }
    }

    private readonly Matrix4x4 _rootLocalToWorld;
    private readonly ShapeData[] _addShapes;
    private readonly ShapeData[] _subtractShapes;
    private readonly ShapeData[] _intersectShapes;

    public bool HasUnsupportedShapes { get; }

    public SdfSceneSnapshot(Transform root, List<VolumeObject> objects)
    {
        _rootLocalToWorld = root.localToWorldMatrix;
        float minSamplingCellSize = EstimateMinSamplingCellSize(root.GetComponent<VolumeModel>());
        List<ShapeData> addShapes = new();
        List<ShapeData> subtractShapes = new();
        List<ShapeData> intersectShapes = new();
        bool hasUnsupportedShapes = false;

        for (int i = 0; i < objects.Count; i++)
        {
            VolumeObject obj = objects[i];
            if (obj == null)
                continue;

            ShapeData shape = new ShapeData(obj, minSamplingCellSize);
            hasUnsupportedShapes |= !shape.IsSupported;
            switch (obj.role)
            {
                case VolumeOperationRole.Subtract:
                    subtractShapes.Add(shape);
                    break;
                case VolumeOperationRole.Intersect:
                    intersectShapes.Add(shape);
                    break;
                default:
                    addShapes.Add(shape);
                    break;
            }
        }

        _addShapes = addShapes.ToArray();
        _subtractShapes = subtractShapes.ToArray();
        _intersectShapes = intersectShapes.ToArray();
        HasUnsupportedShapes = hasUnsupportedShapes;
    }

    public float Evaluate(Vector3 rootLocalPoint)
    {
        Vector3 worldPoint = _rootLocalToWorld.MultiplyPoint3x4(rootLocalPoint);
        float result = float.PositiveInfinity;

        for (int i = 0; i < _addShapes.Length; i++)
            result = Mathf.Min(result, _addShapes[i].EvaluateWorld(worldPoint));
        for (int i = 0; i < _subtractShapes.Length; i++)
            result = Mathf.Max(result, -_subtractShapes[i].EvaluateWorld(worldPoint));
        for (int i = 0; i < _intersectShapes.Length; i++)
            result = Mathf.Max(result, _intersectShapes[i].EvaluateWorld(worldPoint));

        return result;
    }

    private static float EstimateMinSamplingCellSize(VolumeModel model)
    {
        if (model == null)
            return 0f;

        switch (model.dataStructure)
        {
            case VolumeDataStructure.VoxelGrid:
                Vector3Int size = model.voxelGridSampler.builder.gridSize;
                Vector3 extent = model.voxelGridSampler.builder.gridExtent;
                float cx = extent.x / Mathf.Max(1, size.x - 1);
                float cy = extent.y / Mathf.Max(1, size.y - 1);
                float cz = extent.z / Mathf.Max(1, size.z - 1);
                return Mathf.Min(cx, Mathf.Min(cy, cz));
            case VolumeDataStructure.Octree:
            case VolumeDataStructure.SparseVoxelOctree:
                OctreeVolumeBuilder builder = model.dataStructure == VolumeDataStructure.Octree
                    ? model.octreeSampler.builder
                    : model.sparseVoxelOctreeSampler.builder.backend;
                int resolution = 1 << Mathf.Max(0, builder.maxDepth);
                Vector3 cell = builder.size / Mathf.Max(1, resolution);
                return Mathf.Min(cell.x, Mathf.Min(cell.y, cell.z));
            default:
                return 0f;
        }
    }
}
