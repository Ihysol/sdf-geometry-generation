using UnityEngine;
using System.Collections.Generic;

[System.Serializable]
public class OctreeVolumeBuilder : VolumeBuilderBase<OctreeVolume>
{
    [Header("Bounds")]
    public Vector3 center = Vector3.zero;
    public Vector3 size = new Vector3(4f, 4f, 4f);

    [Header("Padding")]
    public float boundsPadding = 0.25f;

    [Header("Octree")]
    public int maxDepth = 6;
    public int minDepth = 3;
    [HideInInspector]
    public bool useQefVertices = true;
    [HideInInspector]
    public QefVertexMode qefVertexMode = QefVertexMode.QefAxisSnap;
    [HideInInspector]
    public float qefBlendFactor = 0.5f;
    [HideInInspector]
    public float qefSnapEpsilon = 0.015f;
    [HideInInspector]
    public float qefMaxOffsetCells = 0.75f;
    [HideInInspector]
    public float qefAxisSnapStrength = 2.5f;
    [HideInInspector]
    public bool qefEnableMultiHermite = false;
    [HideInInspector]
    public int qefHermiteSamplesPerEdge = 3;

    private int _totalNodes;
    private int _surfaceLeaves;

    public override Bounds Bounds
    {
        get
        {
            Vector3 paddedSize = size + Vector3.one * boundsPadding * 2f;
            return new Bounds(center, paddedSize);
        }
    }

    private readonly struct Edge
    {
        public readonly int A;
        public readonly int B;

        public Edge(int a, int b)
        {
            A = a;
            B = b;
        }
    }

    private static readonly Edge[] Edges =
    {
        new Edge(0, 1),
        new Edge(1, 2),
        new Edge(2, 3),
        new Edge(3, 0),

        new Edge(4, 5),
        new Edge(5, 6),
        new Edge(6, 7),
        new Edge(7, 4),

        new Edge(0, 4),
        new Edge(1, 5),
        new Edge(2, 6),
        new Edge(3, 7)
    };

    private readonly List<Vector3> _qefPoints = new(12);
    private readonly List<Vector3> _qefNormals = new(12);
    private readonly List<float> _qefWeights = new(12);
    private readonly Dictionary<Vector3Int, float> _cornerSampleCache = new();
    private readonly Dictionary<HermiteEdgeKey, HermiteSample> _hermiteSampleCache = new();

    private readonly struct HermiteSample
    {
        public readonly Vector3 Point;
        public readonly Vector3 Normal;
        public readonly float Weight;

        public HermiteSample(Vector3 point, Vector3 normal, float weight)
        {
            Point = point;
            Normal = normal;
            Weight = weight;
        }
    }

    private readonly struct HermiteEdgeKey
    {
        public readonly Vector3Int A;
        public readonly Vector3Int B;

        public HermiteEdgeKey(Vector3Int a, Vector3Int b)
        {
            if (LexicographicLessOrEqual(a, b))
            {
                A = a;
                B = b;
            }
            else
            {
                A = b;
                B = a;
            }
        }

        public override int GetHashCode()
        {
            return (A.GetHashCode() * 397) ^ B.GetHashCode();
        }

        public override bool Equals(object obj)
        {
            if (obj is not HermiteEdgeKey other)
                return false;

            return A == other.A && B == other.B;
        }

        private static bool LexicographicLessOrEqual(Vector3Int x, Vector3Int y)
        {
            if (x.x != y.x) return x.x < y.x;
            if (x.y != y.y) return x.y < y.y;
            return x.z <= y.z;
        }
    }

    /// <summary>Builds an adaptive octree by recursively sampling the scalar field.</summary>
    public override OctreeVolume Build(IScalarFieldSource source)
    {
        _totalNodes = 0;
        _surfaceLeaves = 0;
        _cornerSampleCache.Clear();
        _hermiteSampleCache.Clear();

        Bounds buildBounds = Bounds;

        Vector3 origin = buildBounds.min;
        Vector3 cellSize = buildBounds.size / (1 << maxDepth);

        OctreeNode root = BuildNode(
            source,
            buildBounds,
            0,
            origin,
            cellSize
        );

#if UNITY_EDITOR
        // Keep this behind editor-only logging to avoid runtime spam.
        if (UnityEngine.Debug.isDebugBuild)
        {
            Debug.Log(
                $"Octree Build: nodes={_totalNodes}, surfaceLeaves={_surfaceLeaves}, bounds={buildBounds}"
            );
        }
#endif

        return new OctreeVolume(
            root,
            buildBounds,
            maxDepth,
            _totalNodes,
            _surfaceLeaves,
            source,
            origin,
            cellSize
        );
    }

    public bool RebuildRegion(OctreeVolume existing, IScalarFieldSource source, Bounds dirtyBounds, out OctreeVolume rebuilt)
    {
        rebuilt = null;
        _cornerSampleCache.Clear();
        _hermiteSampleCache.Clear();

        if (source == null || existing == null || existing.Root == null)
            return false;

        Bounds buildBounds = Bounds;
        Vector3 origin = buildBounds.min;
        Vector3 cellSize = buildBounds.size / (1 << maxDepth);

        if (existing.Bounds.center != buildBounds.center ||
            existing.Bounds.size != buildBounds.size ||
            existing.MaxDepth != maxDepth ||
            existing.GridOrigin != origin ||
            existing.CellSize != cellSize)
        {
            return false;
        }

        Vector3 eps = cellSize;
        Bounds expandedDirty = dirtyBounds;
        expandedDirty.Expand(eps * 2f);

        OctreeNode root = RebuildNodeRegion(
            existing.Root,
            source,
            expandedDirty,
            0,
            origin,
            cellSize
        );

        int totalNodes = 0;
        int surfaceLeaves = 0;
        CountStats(root, ref totalNodes, ref surfaceLeaves);

        rebuilt = new OctreeVolume(
            root,
            buildBounds,
            maxDepth,
            totalNodes,
            surfaceLeaves,
            source,
            origin,
            cellSize
        );

        return true;
    }

    private OctreeNode RebuildNodeRegion(
        OctreeNode existingNode,
        IScalarFieldSource source,
        Bounds dirtyBounds,
        int depth,
        Vector3 origin,
        Vector3 cellSize)
    {
        if (existingNode == null)
            return null;

        if (!existingNode.Bounds.Intersects(dirtyBounds))
            return existingNode;

        if (existingNode.IsLeaf || existingNode.Children == null || existingNode.Children.Length == 0)
            return BuildNode(source, existingNode.Bounds, depth, origin, cellSize);

        OctreeNode node = new OctreeNode(existingNode.Bounds)
        {
            IsLeaf = false,
            Depth = depth,
            Children = new OctreeNode[8],
            Coord = existingNode.Coord,
            SizeInCells = existingNode.SizeInCells
        };

        for (int i = 0; i < 8; i++)
        {
            OctreeNode child = i < existingNode.Children.Length ? existingNode.Children[i] : null;
            node.Children[i] = RebuildNodeRegion(
                child,
                source,
                dirtyBounds,
                depth + 1,
                origin,
                cellSize
            );
        }

        node.ContainsSurface = AnyChildContainsSurface(node.Children);
        return node;
    }

    private static bool AnyChildContainsSurface(OctreeNode[] children)
    {
        if (children == null)
            return false;

        for (int i = 0; i < children.Length; i++)
        {
            OctreeNode c = children[i];

            if (c == null)
                continue;

            if (c.IsLeaf)
            {
                if (c.ContainsSurface)
                    return true;
            }
            else if (AnyChildContainsSurface(c.Children))
            {
                return true;
            }
        }

        return false;
    }

    private static void CountStats(OctreeNode node, ref int totalNodes, ref int surfaceLeaves)
    {
        if (node == null)
            return;

        totalNodes++;

        if (node.IsLeaf)
        {
            if (node.ContainsSurface)
                surfaceLeaves++;

            return;
        }

        if (node.Children == null)
            return;

        for (int i = 0; i < node.Children.Length; i++)
            CountStats(node.Children[i], ref totalNodes, ref surfaceLeaves);
    }

    /// <summary>Builds one octree node and subdivides it when it may contain surface detail.</summary>
    private OctreeNode BuildNode(
     IScalarFieldSource source,
     Bounds bounds,
     int depth,
     Vector3 origin,
     Vector3 cellSize)
    {
        _totalNodes++;

        OctreeNode node = new OctreeNode(bounds);
        node.Depth = depth;

        float[] corners = SampleCorners(source, bounds, origin, cellSize);
        float centerValue = source.Evaluate(bounds.center);

        node.CornerValues = corners;
        node.Coord = GetCoord(bounds, origin, cellSize);
        node.SizeInCells = GetSizeInCells(bounds, cellSize);
        node.CenterValue = centerValue;

        bool cornerHasNegative = false;
        bool cornerHasPositive = false;

        for (int i = 0; i < corners.Length; i++)
        {
            if (corners[i] < 0f)
                cornerHasNegative = true;
            else
                cornerHasPositive = true;
        }

        bool cornerContainsSurface = cornerHasNegative && cornerHasPositive;

        bool centerDiffersFromCorners =
            (centerValue < 0f && cornerHasPositive) ||
            (centerValue >= 0f && cornerHasNegative);

        bool couldContainSurface =
            Mathf.Abs(centerValue - 0f) <= bounds.extents.magnitude;

        bool shouldSubdivide =
            depth < minDepth ||
            cornerContainsSurface ||
            centerDiffersFromCorners ||
            couldContainSurface;
        node.ContainsSurface = cornerContainsSurface;

        // Adaptive pruning:
        // Wenn weder Corner-Crossing noch Center-Hinweis vorhanden ist,
        // und minDepth erreicht wurde, stoppen wir früh.
        if (!shouldSubdivide)
        {
            node.IsLeaf = true;
            node.ContainsSurface = false;
            return node;
        }

        // Max depth:
        // Nur echte Corner-Crossing-Zellen werden Surface-Leaves.
        if (depth >= maxDepth)
        {
            node.IsLeaf = true;
            node.ContainsSurface = cornerContainsSurface;

            if (cornerContainsSurface)
            {
                node.SurfaceVertex = EstimateSurfaceVertex(
                    source,
                    bounds,
                    corners,
                    origin,
                    cellSize
                );

                _surfaceLeaves++;
            }

            return node;
        }

        node.IsLeaf = false;
        node.Children = new OctreeNode[8];

        Vector3 childSize = bounds.size * 0.5f;
        Vector3 min = bounds.min;

        int childIndex = 0;

        for (int x = 0; x < 2; x++)
            for (int y = 0; y < 2; y++)
                for (int z = 0; z < 2; z++)
                {
                    Vector3 childCenter = min + new Vector3(
                        (x + 0.5f) * childSize.x,
                        (y + 0.5f) * childSize.y,
                        (z + 0.5f) * childSize.z
                    );

                    Bounds childBounds = new Bounds(childCenter, childSize);

                    node.Children[childIndex++] = BuildNode(
                        source,
                        childBounds,
                        depth + 1,
                        origin,
                        cellSize
                    );
                }

        return node;
    }

    /// <summary>Maps a node bound to its integer coordinate on the global finest grid.</summary>
    private Vector3Int GetCoord(Bounds bounds, Vector3 origin, Vector3 cellSize)
    {
        Vector3 local = bounds.center - origin;

        return new Vector3Int(
            Mathf.RoundToInt(local.x / cellSize.x - 0.5f),
            Mathf.RoundToInt(local.y / cellSize.y - 0.5f),
            Mathf.RoundToInt(local.z / cellSize.z - 0.5f)
        );
    }

    /// <summary>Samples all eight corners of a node bound.</summary>
    private float[] SampleCorners(IScalarFieldSource source, Bounds bounds, Vector3 origin, Vector3 cellSize)
    {
        Vector3[] positions = GetCornerPositions(bounds);
        Vector3Int[] coords = GetCornerGridCoords(bounds, origin, cellSize);

        return new float[]
        {
            EvaluateCornerCached(source, coords[0], positions[0]),
            EvaluateCornerCached(source, coords[1], positions[1]),
            EvaluateCornerCached(source, coords[2], positions[2]),
            EvaluateCornerCached(source, coords[3], positions[3]),
            EvaluateCornerCached(source, coords[4], positions[4]),
            EvaluateCornerCached(source, coords[5], positions[5]),
            EvaluateCornerCached(source, coords[6], positions[6]),
            EvaluateCornerCached(source, coords[7], positions[7])
        };
    }

    private static Vector3Int[] GetCornerGridCoords(Bounds bounds, Vector3 origin, Vector3 cellSize)
    {
        Vector3 min = bounds.min;
        Vector3 max = bounds.max;

        return new Vector3Int[]
        {
            WorldToGridVertex(min.x, min.y, min.z, origin, cellSize),
            WorldToGridVertex(max.x, min.y, min.z, origin, cellSize),
            WorldToGridVertex(max.x, max.y, min.z, origin, cellSize),
            WorldToGridVertex(min.x, max.y, min.z, origin, cellSize),
            WorldToGridVertex(min.x, min.y, max.z, origin, cellSize),
            WorldToGridVertex(max.x, min.y, max.z, origin, cellSize),
            WorldToGridVertex(max.x, max.y, max.z, origin, cellSize),
            WorldToGridVertex(min.x, max.y, max.z, origin, cellSize)
        };
    }

    private static Vector3Int WorldToGridVertex(float x, float y, float z, Vector3 origin, Vector3 cellSize)
    {
        return new Vector3Int(
            Mathf.RoundToInt((x - origin.x) / cellSize.x),
            Mathf.RoundToInt((y - origin.y) / cellSize.y),
            Mathf.RoundToInt((z - origin.z) / cellSize.z)
        );
    }

    private float EvaluateCornerCached(IScalarFieldSource source, Vector3Int gridCoord, Vector3 worldPos)
    {
        if (_cornerSampleCache.TryGetValue(gridCoord, out float cached))
            return cached;

        float value = source.Evaluate(worldPos);
        _cornerSampleCache[gridCoord] = value;
        return value;
    }

    /// <summary>Returns the eight corner positions for a bound.</summary>
    private Vector3[] GetCornerPositions(Bounds bounds)
    {
        Vector3 min = bounds.min;
        Vector3 max = bounds.max;

        return new Vector3[]
        {
            new Vector3(min.x, min.y, min.z),
            new Vector3(max.x, min.y, min.z),
            new Vector3(max.x, max.y, min.z),
            new Vector3(min.x, max.y, min.z),

            new Vector3(min.x, min.y, max.z),
            new Vector3(max.x, min.y, max.z),
            new Vector3(max.x, max.y, max.z),
            new Vector3(min.x, max.y, max.z)
        };
    }

    /// <summary>Estimates a dual-contouring vertex from edge crossing positions.</summary>
    private Vector3 EstimateSurfaceVertex(
        IScalarFieldSource source,
        Bounds bounds,
        float[] cornerValues,
        Vector3 origin,
        Vector3 cellSize)
    {
        Vector3[] cornerPositions = GetCornerPositions(bounds);
        Vector3Int[] cornerCoords = GetCornerGridCoords(bounds, origin, cellSize);

        Vector3 sum = Vector3.zero;
        int count = 0;
        _qefPoints.Clear();
        _qefNormals.Clear();
        _qefWeights.Clear();

        for (int i = 0; i < Edges.Length; i++)
        {
            Edge edge = Edges[i];

            float va = cornerValues[edge.A];
            float vb = cornerValues[edge.B];

            if (!HasCrossing(va, vb))
                continue;

            Vector3 pa = cornerPositions[edge.A];
            Vector3 pb = cornerPositions[edge.B];
            Vector3Int ca = cornerCoords[edge.A];
            Vector3Int cb = cornerCoords[edge.B];

            AddHermiteSamplesForEdge(source, pa, pb, va, vb, ca, cb, cellSize, 0f, ref sum, ref count);
        }

        Vector3 avg = count == 0 ? bounds.center : (sum / count);
        bool useQef = useQefVertices && qefVertexMode != QefVertexMode.AverageCrossings;
        bool useAdaptiveBlend = qefVertexMode == QefVertexMode.QefFeaturePreserving || qefVertexMode == QefVertexMode.QefAxisSnap;
        bool useAxisSnap = qefVertexMode == QefVertexMode.QefAxisSnap;

        if (useQef &&
            _qefPoints.Count >= 3 &&
            HasSufficientGradientDiversity(_qefNormals) &&
            QefSolver.TrySolve(_qefPoints, _qefNormals, _qefWeights, bounds, out Vector3 qef))
        {
            qef = ConstrainQefToLocalWindow(qef, avg, bounds, cellSize);
            if (IsQefSolutionAcceptable(qef, avg, bounds, cellSize))
            {
                float blend = useAdaptiveBlend ? GetAdaptiveQefBlend(_qefNormals) : Mathf.Clamp01(qefBlendFactor);
                Vector3 blended = Vector3.Lerp(avg, qef, blend);
                Vector3 result = useAxisSnap
                    ? SnapAxisAlignedFeature(blended, _qefNormals, origin, cellSize)
                    : blended;
                if (useAxisSnap)
                    result = SnapToGridNearBoundaryWithFactor(result, origin, cellSize, qefAxisSnapStrength);
                return SnapToGridNearBoundary(result, origin, cellSize);
            }
        }


        if (count == 0)
            return SnapToGridNearBoundary(bounds.center, origin, cellSize);

        Vector3 avgResult = useAxisSnap ? SnapAxisAlignedFeature(avg, _qefNormals, origin, cellSize) : avg;
        if (useAxisSnap)
            avgResult = SnapToGridNearBoundaryWithFactor(avgResult, origin, cellSize, qefAxisSnapStrength);
        return SnapToGridNearBoundary(avgResult, origin, cellSize);
    }

    private Vector3 ConstrainQefToLocalWindow(Vector3 qef, Vector3 avg, Bounds bounds, Vector3 cellSize)
    {
        float maxCell = Mathf.Max(Mathf.Abs(cellSize.x), Mathf.Max(Mathf.Abs(cellSize.y), Mathf.Abs(cellSize.z)));
        float window = Mathf.Max(maxCell * Mathf.Max(0f, qefMaxOffsetCells), 1e-4f);

        Vector3 constrained = new Vector3(
            Mathf.Clamp(qef.x, avg.x - window, avg.x + window),
            Mathf.Clamp(qef.y, avg.y - window, avg.y + window),
            Mathf.Clamp(qef.z, avg.z - window, avg.z + window)
        );

        constrained.x = Mathf.Clamp(constrained.x, bounds.min.x, bounds.max.x);
        constrained.y = Mathf.Clamp(constrained.y, bounds.min.y, bounds.max.y);
        constrained.z = Mathf.Clamp(constrained.z, bounds.min.z, bounds.max.z);
        return constrained;
    }

    /// <summary>Checks whether two scalar samples cross the zero iso surface.</summary>
    private bool HasCrossing(float a, float b)
    {
        return (a <= 0f && b > 0f)
            || (a > 0f && b <= 0f);
    }

    /// <summary>Converts node size into finest-grid cell counts.</summary>
    private Vector3Int GetSizeInCells(Bounds bounds, Vector3 cellSize)
    {
        return new Vector3Int(
            Mathf.Max(1, Mathf.RoundToInt(bounds.size.x / cellSize.x)),
            Mathf.Max(1, Mathf.RoundToInt(bounds.size.y / cellSize.y)),
            Mathf.Max(1, Mathf.RoundToInt(bounds.size.z / cellSize.z))
        );
    }

    private Vector3 SnapToGridNearBoundary(Vector3 p, Vector3 origin, Vector3 cellSize)
    {
        float ex = Mathf.Abs(cellSize.x) * Mathf.Max(0f, qefSnapEpsilon);
        float ey = Mathf.Abs(cellSize.y) * Mathf.Max(0f, qefSnapEpsilon);
        float ez = Mathf.Abs(cellSize.z) * Mathf.Max(0f, qefSnapEpsilon);

        float gx = (p.x - origin.x) / cellSize.x;
        float gy = (p.y - origin.y) / cellSize.y;
        float gz = (p.z - origin.z) / cellSize.z;

        float rx = Mathf.Round(gx);
        float ry = Mathf.Round(gy);
        float rz = Mathf.Round(gz);

        if (Mathf.Abs(gx - rx) <= ex / Mathf.Abs(cellSize.x))
            p.x = origin.x + rx * cellSize.x;

        if (Mathf.Abs(gy - ry) <= ey / Mathf.Abs(cellSize.y))
            p.y = origin.y + ry * cellSize.y;

        if (Mathf.Abs(gz - rz) <= ez / Mathf.Abs(cellSize.z))
            p.z = origin.z + rz * cellSize.z;

        return p;
    }

    private Vector3 SnapToGridNearBoundaryWithFactor(Vector3 p, Vector3 origin, Vector3 cellSize, float factor)
    {
        float scaled = Mathf.Max(0f, qefSnapEpsilon) * Mathf.Max(1f, factor);
        float ex = Mathf.Abs(cellSize.x) * scaled;
        float ey = Mathf.Abs(cellSize.y) * scaled;
        float ez = Mathf.Abs(cellSize.z) * scaled;

        float gx = (p.x - origin.x) / cellSize.x;
        float gy = (p.y - origin.y) / cellSize.y;
        float gz = (p.z - origin.z) / cellSize.z;

        float rx = Mathf.Round(gx);
        float ry = Mathf.Round(gy);
        float rz = Mathf.Round(gz);

        if (Mathf.Abs(gx - rx) <= ex / Mathf.Abs(cellSize.x))
            p.x = origin.x + rx * cellSize.x;
        if (Mathf.Abs(gy - ry) <= ey / Mathf.Abs(cellSize.y))
            p.y = origin.y + ry * cellSize.y;
        if (Mathf.Abs(gz - rz) <= ez / Mathf.Abs(cellSize.z))
            p.z = origin.z + rz * cellSize.z;

        return p;
    }

    private Vector3 RefineEdgeIntersection(IScalarFieldSource source, Vector3 pa, Vector3 pb, float va, float vb, float isoLevel)
    {
        float fa = va - isoLevel;
        float fb = vb - isoLevel;

        if (Mathf.Abs(fa) < 1e-8f)
            return pa;
        if (Mathf.Abs(fb) < 1e-8f)
            return pb;

        float t = fa / (fa - fb);
        t = Mathf.Clamp01(t);
        Vector3 best = Vector3.Lerp(pa, pb, t);

        Vector3 a = pa;
        Vector3 b = pb;
        float fA = fa;
        float fB = fb;

        // A few bisection steps produce more stable and crisper crossings.
        for (int i = 0; i < 3; i++)
        {
            Vector3 mid = (a + b) * 0.5f;
            float fM = source.Evaluate(mid) - isoLevel;
            best = mid;

            if (Mathf.Abs(fM) < 1e-6f)
                break;

            if ((fA <= 0f && fM > 0f) || (fA > 0f && fM <= 0f))
            {
                b = mid;
                fB = fM;
            }
            else
            {
                a = mid;
                fA = fM;
            }
        }

        return best;
    }

    private HermiteSample GetHermiteSample(
        IScalarFieldSource source,
        Vector3 pa,
        Vector3 pb,
        float va,
        float vb,
        Vector3Int ca,
        Vector3Int cb,
        Vector3 cellSize,
        float isoLevel)
    {
        HermiteEdgeKey key = new HermiteEdgeKey(ca, cb);
        if (_hermiteSampleCache.TryGetValue(key, out HermiteSample cached))
            return cached;

        Vector3 p = RefineEdgeIntersection(source, pa, pb, va, vb, isoLevel);
        Vector3 g = EstimateGradientVector(source, p, cellSize);
        float strength = g.magnitude;
        HermiteSample sample = new HermiteSample(
            p,
            SafeNormalize(g),
            Mathf.Max(0.05f, strength)
        );

        _hermiteSampleCache[key] = sample;
        return sample;
    }

    private void AddHermiteSamplesForEdge(
        IScalarFieldSource source,
        Vector3 pa,
        Vector3 pb,
        float va,
        float vb,
        Vector3Int ca,
        Vector3Int cb,
        Vector3 cellSize,
        float isoLevel,
        ref Vector3 sum,
        ref int count)
    {
        HermiteSample center = GetHermiteSample(source, pa, pb, va, vb, ca, cb, cellSize, isoLevel);
        sum += center.Point;
        count++;
        _qefPoints.Add(center.Point);
        _qefNormals.Add(center.Normal);
        _qefWeights.Add(center.Weight);

        if (!qefEnableMultiHermite)
            return;

        int samples = Mathf.Max(1, qefHermiteSamplesPerEdge);
        if (samples <= 1)
            return;

        float denom = vb - va;
        if (Mathf.Abs(denom) < 1e-8f)
            return;
        float baseT = Mathf.Clamp01((0f - va) / denom);
        float span = 0.2f;
        float step = (samples == 2) ? 0f : (2f * span / (samples - 1));

        for (int i = 0; i < samples; i++)
        {
            float offset = -span + step * i;
            if (Mathf.Abs(offset) < 1e-6f)
                continue;

            float t = Mathf.Clamp01(baseT + offset);
            Vector3 p = Vector3.Lerp(pa, pb, t);
            Vector3 g = EstimateGradientVector(source, p, cellSize);
            float w = Mathf.Max(0.02f, g.magnitude * 0.35f);
            Vector3 n = SafeNormalize(g);

            sum += p;
            count++;
            _qefPoints.Add(p);
            _qefNormals.Add(n);
            _qefWeights.Add(w);
        }
    }

    private Vector3 SnapAxisAlignedFeature(Vector3 p, List<Vector3> normals, Vector3 origin, Vector3 cellSize)
    {
        if (normals == null || normals.Count == 0)
            return p;

        Vector3 sum = Vector3.zero;
        for (int i = 0; i < normals.Count; i++)
            sum += normals[i];

        Vector3 mean = sum.normalized;
        float ax = Mathf.Abs(mean.x);
        float ay = Mathf.Abs(mean.y);
        float az = Mathf.Abs(mean.z);
        float dominant = Mathf.Max(ax, Mathf.Max(ay, az));

        if (dominant < 0.75f)
            return p;

        float strengthen = Mathf.Lerp(1.15f, 1.8f, Mathf.InverseLerp(0.75f, 1f, dominant));
        float eps = Mathf.Max(0f, qefSnapEpsilon) * strengthen;

        float gx = (p.x - origin.x) / cellSize.x;
        float gy = (p.y - origin.y) / cellSize.y;
        float gz = (p.z - origin.z) / cellSize.z;

        if (ax >= ay && ax >= az)
        {
            float r = Mathf.Round(gx);
            if (Mathf.Abs(gx - r) <= eps)
                p.x = origin.x + r * cellSize.x;
        }
        else if (ay >= ax && ay >= az)
        {
            float r = Mathf.Round(gy);
            if (Mathf.Abs(gy - r) <= eps)
                p.y = origin.y + r * cellSize.y;
        }
        else
        {
            float r = Mathf.Round(gz);
            if (Mathf.Abs(gz - r) <= eps)
                p.z = origin.z + r * cellSize.z;
        }

        return p;
    }

    private Vector3 EstimateGradientVector(IScalarFieldSource source, Vector3 p, Vector3 cellSize)
    {
        float hx = Mathf.Max(Mathf.Abs(cellSize.x), 1e-4f) * 0.5f;
        float hy = Mathf.Max(Mathf.Abs(cellSize.y), 1e-4f) * 0.5f;
        float hz = Mathf.Max(Mathf.Abs(cellSize.z), 1e-4f) * 0.5f;

        float dx = source.Evaluate(p + new Vector3(hx, 0f, 0f)) - source.Evaluate(p - new Vector3(hx, 0f, 0f));
        float dy = source.Evaluate(p + new Vector3(0f, hy, 0f)) - source.Evaluate(p - new Vector3(0f, hy, 0f));
        float dz = source.Evaluate(p + new Vector3(0f, 0f, hz)) - source.Evaluate(p - new Vector3(0f, 0f, hz));

        return new Vector3(dx, dy, dz);
    }

    private static Vector3 SafeNormalize(Vector3 v)
    {
        float len = v.magnitude;
        if (len < 1e-8f)
            return Vector3.up;
        return v / len;
    }

    private bool IsQefSolutionAcceptable(Vector3 qef, Vector3 avg, Bounds bounds, Vector3 cellSize)
    {
        // Guard against rare unstable minima that jump far away from local crossings.
        float maxCell = Mathf.Max(Mathf.Abs(cellSize.x), Mathf.Max(Mathf.Abs(cellSize.y), Mathf.Abs(cellSize.z)));
        float maxAllowedOffset = Mathf.Max(maxCell * Mathf.Max(0f, qefMaxOffsetCells), 1e-4f);

        if ((qef - avg).magnitude > maxAllowedOffset)
            return false;

        if (!bounds.Contains(qef))
            return false;

        return true;
    }

    private bool HasSufficientGradientDiversity(List<Vector3> normals)
    {
        if (normals == null || normals.Count < 3)
            return false;
        GetNormalEigenvalues(normals, out float l1, out float l2, out float l3);
        return l1 > 1e-4f && (l2 + l3) > 0.02f;
    }

    private float GetAdaptiveQefBlend(List<Vector3> normals)
    {
        float baseBlend = Mathf.Clamp01(qefBlendFactor);
        if (normals == null || normals.Count < 3)
            return baseBlend * 0.35f;
        GetNormalEigenvalues(normals, out float l1, out float l2, out float l3);
        float featureStrength = l1 > 1e-6f ? Mathf.Clamp01((l2 + l3) / l1) : 0f;
        float scale = Mathf.Lerp(0.35f, 1f, featureStrength);
        return baseBlend * scale;
    }

    private static void GetNormalEigenvalues(List<Vector3> normals, out float l1, out float l2, out float l3)
    {
        float c00 = 0f, c01 = 0f, c02 = 0f, c11 = 0f, c12 = 0f, c22 = 0f;
        int n = 0;
        for (int i = 0; i < normals.Count; i++)
        {
            Vector3 v = normals[i];
            float len = v.magnitude;
            if (len < 1e-8f)
                continue;
            v /= len;
            c00 += v.x * v.x; c01 += v.x * v.y; c02 += v.x * v.z;
            c11 += v.y * v.y; c12 += v.y * v.z; c22 += v.z * v.z;
            n++;
        }
        if (n == 0)
        {
            l1 = l2 = l3 = 0f;
            return;
        }
        float inv = 1f / n;
        c00 *= inv; c01 *= inv; c02 *= inv; c11 *= inv; c12 *= inv; c22 *= inv;

        // Jacobi sweeps for symmetric 3x3 covariance.
        for (int it = 0; it < 6; it++)
        {
            Rotate(ref c00, ref c01, ref c11);
            Rotate(ref c00, ref c02, ref c22);
            Rotate(ref c11, ref c12, ref c22);
        }

        l1 = c00; l2 = c11; l3 = c22;
        if (l1 < l2) Swap(ref l1, ref l2);
        if (l2 < l3) Swap(ref l2, ref l3);
        if (l1 < l2) Swap(ref l1, ref l2);
    }

    private static void Rotate(ref float app, ref float apq, ref float aqq)
    {
        if (Mathf.Abs(apq) < 1e-6f)
            return;
        float phi = 0.5f * Mathf.Atan2(2f * apq, aqq - app);
        float c = Mathf.Cos(phi);
        float s = Mathf.Sin(phi);
        float app2 = c * c * app - 2f * s * c * apq + s * s * aqq;
        float aqq2 = s * s * app + 2f * s * c * apq + c * c * aqq;
        app = app2;
        aqq = aqq2;
        apq = 0f;
    }

    private static void Swap(ref float a, ref float b)
    {
        float t = a; a = b; b = t;
    }
}
