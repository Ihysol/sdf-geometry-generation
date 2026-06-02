using UnityEngine;
using System.Collections.Generic;
using System.Diagnostics;

[System.Serializable]
public class OctreeVolumeBuilder : VolumeBuilderBase<OctreeVolume>
{
    public readonly struct BuildStats
    {
        public readonly double totalMs;
        public readonly double recursiveBuildMs;
        public readonly double surfaceVertexMs;
        public readonly int totalNodes;
        public readonly int surfaceLeaves;
        public readonly int sourceEvaluations;
        public readonly int cornerCacheHits;
        public readonly int cornerCacheMisses;
        public readonly int centerEvaluations;
        public readonly int centerCacheHits;
        public readonly int centerCacheMisses;
        public readonly int centerDirectEvaluations;
        public readonly int edgeRefinementEvaluations;
        public readonly int gradientEvaluations;
        public readonly int gradientCacheHits;
        public readonly int gradientCacheMisses;
        public readonly int hermiteCacheHits;
        public readonly int hermiteCacheMisses;
        public readonly int subdivisionMinDepth;
        public readonly int subdivisionCornerCrossing;
        public readonly int subdivisionCenterMismatch;
        public readonly int subdivisionDistanceThreshold;

        public BuildStats(
            double totalMs,
            double recursiveBuildMs,
            double surfaceVertexMs,
            int totalNodes,
            int surfaceLeaves,
            int sourceEvaluations,
            int cornerCacheHits,
            int cornerCacheMisses,
            int centerEvaluations,
            int centerCacheHits,
            int centerCacheMisses,
            int centerDirectEvaluations,
            int edgeRefinementEvaluations,
            int gradientEvaluations,
            int gradientCacheHits,
            int gradientCacheMisses,
            int hermiteCacheHits,
            int hermiteCacheMisses,
            int subdivisionMinDepth,
            int subdivisionCornerCrossing,
            int subdivisionCenterMismatch,
            int subdivisionDistanceThreshold)
        {
            this.totalMs = totalMs;
            this.recursiveBuildMs = recursiveBuildMs;
            this.surfaceVertexMs = surfaceVertexMs;
            this.totalNodes = totalNodes;
            this.surfaceLeaves = surfaceLeaves;
            this.sourceEvaluations = sourceEvaluations;
            this.cornerCacheHits = cornerCacheHits;
            this.cornerCacheMisses = cornerCacheMisses;
            this.centerEvaluations = centerEvaluations;
            this.centerCacheHits = centerCacheHits;
            this.centerCacheMisses = centerCacheMisses;
            this.centerDirectEvaluations = centerDirectEvaluations;
            this.edgeRefinementEvaluations = edgeRefinementEvaluations;
            this.gradientEvaluations = gradientEvaluations;
            this.gradientCacheHits = gradientCacheHits;
            this.gradientCacheMisses = gradientCacheMisses;
            this.hermiteCacheHits = hermiteCacheHits;
            this.hermiteCacheMisses = hermiteCacheMisses;
            this.subdivisionMinDepth = subdivisionMinDepth;
            this.subdivisionCornerCrossing = subdivisionCornerCrossing;
            this.subdivisionCenterMismatch = subdivisionCenterMismatch;
            this.subdivisionDistanceThreshold = subdivisionDistanceThreshold;
        }
    }

    [Header("Bounds")]
    public Vector3 center = Vector3.zero;
    public Vector3 size = new Vector3(4f, 4f, 4f);

    [Header("Padding")]
    public float boundsPadding = 0.25f;

    [Header("Octree")]
    public int maxDepth = 6;
    public int minDepth = 3;
    [HideInInspector]
    public bool suppressBuildLog = false;
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
    [HideInInspector]
    public int edgeRefinementSteps = 3;
    [HideInInspector]
    public QefSolver.RobustKernel qefRobustKernel = QefSolver.RobustKernel.Cauchy;
    [HideInInspector]
    public float qefRobustScale = 2.5f;
    [HideInInspector]
    public int qefIrlsIterations = 3;
    [HideInInspector]
    public bool qefUseAnisotropicRegularization = false;
    [HideInInspector]
    public float qefAnisotropicStrength = 0.2f;
    [HideInInspector]
    public QefFeatureClassWeightMode qefFeatureWeightMode = QefFeatureClassWeightMode.Off;
    [HideInInspector]
    public float qefSurfaceWeight = 1f;
    [HideInInspector]
    public float qefEdgeWeight = 1.2f;
    [HideInInspector]
    public float qefCornerWeight = 1.4f;

    private int _totalNodes;
    private int _surfaceLeaves;
    private int _sourceEvaluations;
    private int _cornerCacheHits;
    private int _cornerCacheMisses;
    private int _centerEvaluations;
    private int _centerCacheHits;
    private int _centerCacheMisses;
    private int _centerDirectEvaluations;
    private int _edgeRefinementEvaluations;
    private int _gradientEvaluations;
    private int _gradientCacheHits;
    private int _gradientCacheMisses;
    private int _hermiteCacheHits;
    private int _hermiteCacheMisses;
    private int _subdivisionMinDepth;
    private int _subdivisionCornerCrossing;
    private int _subdivisionCenterMismatch;
    private int _subdivisionDistanceThreshold;
    private long _surfaceVertexTicks;

    public BuildStats LastBuildStats { get; private set; }

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

    private readonly struct CornerSamples
    {
        private readonly float _v0;
        private readonly float _v1;
        private readonly float _v2;
        private readonly float _v3;
        private readonly float _v4;
        private readonly float _v5;
        private readonly float _v6;
        private readonly float _v7;

        public int Length => 8;

        public CornerSamples(float v0, float v1, float v2, float v3, float v4, float v5, float v6, float v7)
        {
            _v0 = v0;
            _v1 = v1;
            _v2 = v2;
            _v3 = v3;
            _v4 = v4;
            _v5 = v5;
            _v6 = v6;
            _v7 = v7;
        }

        public float this[int index] => index switch
        {
            0 => _v0,
            1 => _v1,
            2 => _v2,
            3 => _v3,
            4 => _v4,
            5 => _v5,
            6 => _v6,
            _ => _v7
        };

        public float[] ToArray()
        {
            return new[] { _v0, _v1, _v2, _v3, _v4, _v5, _v6, _v7 };
        }
    }

    private readonly List<Vector3> _qefPoints = new(12);
    private readonly List<Vector3> _qefNormals = new(12);
    private readonly List<float> _qefWeights = new(12);
    private readonly Dictionary<Vector3Int, float> _cornerSampleCache = new();
    private readonly Dictionary<Vector3, float> _gradientSampleCache = new();
    private readonly Dictionary<OctreeHermiteEdgeKey, OctreeHermiteSample> _hermiteSampleCache = new();

    /// <summary>Builds an adaptive octree by recursively sampling the scalar field.</summary>
    public override OctreeVolume Build(IScalarFieldSource source)
    {
        Stopwatch totalStopwatch = Stopwatch.StartNew();
        Stopwatch recursiveStopwatch = Stopwatch.StartNew();
        _totalNodes = 0;
        _surfaceLeaves = 0;
        ResetProfilingCounters();
        _cornerSampleCache.Clear();
        _gradientSampleCache.Clear();
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
        recursiveStopwatch.Stop();
        totalStopwatch.Stop();
        CaptureBuildStats(totalStopwatch.Elapsed.TotalMilliseconds, recursiveStopwatch.Elapsed.TotalMilliseconds);

#if UNITY_EDITOR
        // Keep this behind editor-only logging to avoid runtime spam.
        if (!suppressBuildLog && UnityEngine.Debug.isDebugBuild)
        {
            UnityEngine.Debug.Log(
                $"Octree Build: nodes={_totalNodes}, surfaceLeaves={_surfaceLeaves}, bounds={buildBounds}, refinementSteps={edgeRefinementSteps}, " +
                $"timing(total={LastBuildStats.totalMs:F2} ms, recursive={LastBuildStats.recursiveBuildMs:F2} ms, surfaceVertex={LastBuildStats.surfaceVertexMs:F2} ms), " +
                $"samples(total={LastBuildStats.sourceEvaluations}, cornerMiss={LastBuildStats.cornerCacheMisses}, center={LastBuildStats.centerEvaluations}, edge={LastBuildStats.edgeRefinementEvaluations}, gradient={LastBuildStats.gradientEvaluations}), " +
                $"cornerCache(hit={LastBuildStats.cornerCacheHits}, miss={LastBuildStats.cornerCacheMisses}), " +
                $"centerCache(hit={LastBuildStats.centerCacheHits}, miss={LastBuildStats.centerCacheMisses}, direct={LastBuildStats.centerDirectEvaluations}), " +
                $"gradientCache(hit={LastBuildStats.gradientCacheHits}, miss={LastBuildStats.gradientCacheMisses}), " +
                $"hermiteCache(hit={LastBuildStats.hermiteCacheHits}, miss={LastBuildStats.hermiteCacheMisses}), " +
                $"subdivision(minDepth={LastBuildStats.subdivisionMinDepth}, crossing={LastBuildStats.subdivisionCornerCrossing}, centerMismatch={LastBuildStats.subdivisionCenterMismatch}, distance={LastBuildStats.subdivisionDistanceThreshold})"
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
            cellSize,
            new Dictionary<OctreeHermiteEdgeKey, OctreeHermiteSample>(_hermiteSampleCache),
            edgeRefinementSteps,
            0f
        );
    }

    public bool RebuildRegion(OctreeVolume existing, IScalarFieldSource source, Bounds dirtyBounds, out OctreeVolume rebuilt)
    {
        rebuilt = null;
        _cornerSampleCache.Clear();
        _gradientSampleCache.Clear();
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

        CornerSamples corners = SampleCorners(source, bounds, origin, cellSize);
        float centerValue = EvaluateCenter(source, bounds.center, origin, cellSize);

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

        if (depth < minDepth)
            _subdivisionMinDepth++;
        if (cornerContainsSurface)
            _subdivisionCornerCrossing++;
        if (centerDiffersFromCorners)
            _subdivisionCenterMismatch++;
        if (couldContainSurface)
            _subdivisionDistanceThreshold++;

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
                node.CornerValues = corners.ToArray();
                long surfaceVertexStart = Stopwatch.GetTimestamp();
                node.SurfaceVertex = EstimateSurfaceVertex(
                    source,
                    bounds,
                    corners,
                    origin,
                    cellSize
                );
                _surfaceVertexTicks += Stopwatch.GetTimestamp() - surfaceVertexStart;

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
    private CornerSamples SampleCorners(IScalarFieldSource source, Bounds bounds, Vector3 origin, Vector3 cellSize)
    {
        Vector3 min = bounds.min;
        Vector3 max = bounds.max;
        Vector3Int minCoord = WorldToGridVertex(min.x, min.y, min.z, origin, cellSize);
        Vector3Int maxCoord = WorldToGridVertex(max.x, max.y, max.z, origin, cellSize);

        return new CornerSamples(
            EvaluateCornerCached(source, GetCornerGridCoord(0, minCoord, maxCoord), GetCornerPosition(0, min, max)),
            EvaluateCornerCached(source, GetCornerGridCoord(1, minCoord, maxCoord), GetCornerPosition(1, min, max)),
            EvaluateCornerCached(source, GetCornerGridCoord(2, minCoord, maxCoord), GetCornerPosition(2, min, max)),
            EvaluateCornerCached(source, GetCornerGridCoord(3, minCoord, maxCoord), GetCornerPosition(3, min, max)),
            EvaluateCornerCached(source, GetCornerGridCoord(4, minCoord, maxCoord), GetCornerPosition(4, min, max)),
            EvaluateCornerCached(source, GetCornerGridCoord(5, minCoord, maxCoord), GetCornerPosition(5, min, max)),
            EvaluateCornerCached(source, GetCornerGridCoord(6, minCoord, maxCoord), GetCornerPosition(6, min, max)),
            EvaluateCornerCached(source, GetCornerGridCoord(7, minCoord, maxCoord), GetCornerPosition(7, min, max))
        );
    }

    private static Vector3 GetCornerPosition(int index, Vector3 min, Vector3 max)
    {
        return index switch
        {
            0 => new Vector3(min.x, min.y, min.z),
            1 => new Vector3(max.x, min.y, min.z),
            2 => new Vector3(max.x, max.y, min.z),
            3 => new Vector3(min.x, max.y, min.z),
            4 => new Vector3(min.x, min.y, max.z),
            5 => new Vector3(max.x, min.y, max.z),
            6 => new Vector3(max.x, max.y, max.z),
            _ => new Vector3(min.x, max.y, max.z)
        };
    }

    private static Vector3Int GetCornerGridCoord(int index, Vector3Int min, Vector3Int max)
    {
        return index switch
        {
            0 => new Vector3Int(min.x, min.y, min.z),
            1 => new Vector3Int(max.x, min.y, min.z),
            2 => new Vector3Int(max.x, max.y, min.z),
            3 => new Vector3Int(min.x, max.y, min.z),
            4 => new Vector3Int(min.x, min.y, max.z),
            5 => new Vector3Int(max.x, min.y, max.z),
            6 => new Vector3Int(max.x, max.y, max.z),
            _ => new Vector3Int(min.x, max.y, max.z)
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
        {
            _cornerCacheHits++;
            return cached;
        }

        _cornerCacheMisses++;
        float value = EvaluateSource(source, worldPos);
        _cornerSampleCache[gridCoord] = value;
        return value;
    }

    /// <summary>Estimates a dual-contouring vertex from edge crossing positions.</summary>
    private Vector3 EstimateSurfaceVertex(
        IScalarFieldSource source,
        Bounds bounds,
        CornerSamples cornerValues,
        Vector3 origin,
        Vector3 cellSize)
    {
        Vector3 min = bounds.min;
        Vector3 max = bounds.max;
        Vector3Int minCoord = WorldToGridVertex(min.x, min.y, min.z, origin, cellSize);
        Vector3Int maxCoord = WorldToGridVertex(max.x, max.y, max.z, origin, cellSize);

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

            Vector3 pa = GetCornerPosition(edge.A, min, max);
            Vector3 pb = GetCornerPosition(edge.B, min, max);
            Vector3Int ca = GetCornerGridCoord(edge.A, minCoord, maxCoord);
            Vector3Int cb = GetCornerGridCoord(edge.B, minCoord, maxCoord);

            AddHermiteSamplesForEdge(source, pa, pb, va, vb, ca, cb, cellSize, 0f, ref sum, ref count);
        }

        Vector3 avg = count == 0 ? bounds.center : (sum / count);
        bool useQef = useQefVertices && qefVertexMode != QefVertexMode.AverageCrossings;
        bool useAdaptiveBlend = qefVertexMode == QefVertexMode.QefFeaturePreserving || qefVertexMode == QefVertexMode.QefAxisSnap;
        bool useAxisSnap = qefVertexMode == QefVertexMode.QefAxisSnap;

        if (useQef &&
            _qefPoints.Count >= 3 &&
            HasSufficientGradientDiversity(_qefNormals) &&
            QefSolver.TrySolve(
                _qefPoints,
                _qefNormals,
                BuildWeightedQefWeights(_qefWeights, _qefNormals),
                bounds,
                BuildQefSettings(),
                out Vector3 qef))
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

    private QefSolver.Settings BuildQefSettings()
    {
        return new QefSolver.Settings
        {
            irlsIterations = Mathf.Max(1, qefIrlsIterations),
            robustKernel = qefRobustKernel,
            robustScale = Mathf.Max(0.1f, qefRobustScale),
            useAnisotropicRegularization = qefUseAnisotropicRegularization,
            anisotropicStrength = Mathf.Max(0f, qefAnisotropicStrength)
        };
    }

    private List<float> BuildWeightedQefWeights(List<float> baseWeights, List<Vector3> normals)
    {
        if (baseWeights == null || qefFeatureWeightMode == QefFeatureClassWeightMode.Off)
            return baseWeights;

        float featureScale = GetFeatureClassWeightMultiplier(normals);
        if (Mathf.Abs(featureScale - 1f) < 1e-5f)
            return baseWeights;

        List<float> weighted = new List<float>(baseWeights.Count);
        for (int i = 0; i < baseWeights.Count; i++)
            weighted.Add(baseWeights[i] * featureScale);
        return weighted;
    }

    private float GetFeatureClassWeightMultiplier(List<Vector3> normals)
    {
        if (normals == null || normals.Count < 3)
            return 1f;

        GetNormalEigenvalues(normals, out float l1, out float l2, out float l3);
        float eps = 1e-4f;
        if (l1 < eps)
            return qefSurfaceWeight;
        if (l2 < 0.12f * l1)
            return qefEdgeWeight;
        return qefCornerWeight;
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

        // Optional bisection steps improve crossing precision at additional sampling cost.
        for (int i = 0; i < Mathf.Max(0, edgeRefinementSteps); i++)
        {
            Vector3 mid = (a + b) * 0.5f;
            float fM = EvaluateEdgeRefinement(source, mid) - isoLevel;
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

    private OctreeHermiteSample GetHermiteSample(
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
        OctreeHermiteEdgeKey key = new OctreeHermiteEdgeKey(ca, cb);
        if (_hermiteSampleCache.TryGetValue(key, out OctreeHermiteSample cached))
        {
            _hermiteCacheHits++;
            return cached;
        }

        _hermiteCacheMisses++;
        Vector3 p = RefineEdgeIntersection(source, pa, pb, va, vb, isoLevel);
        Vector3 g = EstimateGradientVector(source, p, cellSize);
        float strength = g.magnitude;
        OctreeHermiteSample sample = new OctreeHermiteSample(
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
        OctreeHermiteSample center = GetHermiteSample(source, pa, pb, va, vb, ca, cb, cellSize, isoLevel);
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

        float dx = EvaluateGradient(source, p + new Vector3(hx, 0f, 0f)) - EvaluateGradient(source, p - new Vector3(hx, 0f, 0f));
        float dy = EvaluateGradient(source, p + new Vector3(0f, hy, 0f)) - EvaluateGradient(source, p - new Vector3(0f, hy, 0f));
        float dz = EvaluateGradient(source, p + new Vector3(0f, 0f, hz)) - EvaluateGradient(source, p - new Vector3(0f, 0f, hz));

        return new Vector3(dx, dy, dz);
    }

    private void ResetProfilingCounters()
    {
        _sourceEvaluations = 0;
        _cornerCacheHits = 0;
        _cornerCacheMisses = 0;
        _centerEvaluations = 0;
        _centerCacheHits = 0;
        _centerCacheMisses = 0;
        _centerDirectEvaluations = 0;
        _edgeRefinementEvaluations = 0;
        _gradientEvaluations = 0;
        _gradientCacheHits = 0;
        _gradientCacheMisses = 0;
        _hermiteCacheHits = 0;
        _hermiteCacheMisses = 0;
        _subdivisionMinDepth = 0;
        _subdivisionCornerCrossing = 0;
        _subdivisionCenterMismatch = 0;
        _subdivisionDistanceThreshold = 0;
        _surfaceVertexTicks = 0;
    }

    private void CaptureBuildStats(double totalMs, double recursiveBuildMs)
    {
        LastBuildStats = new BuildStats(
            totalMs,
            recursiveBuildMs,
            _surfaceVertexTicks * 1000d / Stopwatch.Frequency,
            _totalNodes,
            _surfaceLeaves,
            _sourceEvaluations,
            _cornerCacheHits,
            _cornerCacheMisses,
            _centerEvaluations,
            _centerCacheHits,
            _centerCacheMisses,
            _centerDirectEvaluations,
            _edgeRefinementEvaluations,
            _gradientEvaluations,
            _gradientCacheHits,
            _gradientCacheMisses,
            _hermiteCacheHits,
            _hermiteCacheMisses,
            _subdivisionMinDepth,
            _subdivisionCornerCrossing,
            _subdivisionCenterMismatch,
            _subdivisionDistanceThreshold
        );
    }

    private float EvaluateSource(IScalarFieldSource source, Vector3 position)
    {
        _sourceEvaluations++;
        return source.Evaluate(position);
    }

    private float EvaluateCenter(IScalarFieldSource source, Vector3 position, Vector3 origin, Vector3 cellSize)
    {
        _centerEvaluations++;
        if (!TryGetGridVertex(position, origin, cellSize, out Vector3Int gridCoord))
        {
            _centerDirectEvaluations++;
            return EvaluateSource(source, position);
        }

        if (_cornerSampleCache.TryGetValue(gridCoord, out float cached))
        {
            _centerCacheHits++;
            return cached;
        }

        _centerCacheMisses++;
        float value = EvaluateSource(source, position);
        _cornerSampleCache[gridCoord] = value;
        return value;
    }

    private static bool TryGetGridVertex(Vector3 position, Vector3 origin, Vector3 cellSize, out Vector3Int gridCoord)
    {
        float x = (position.x - origin.x) / cellSize.x;
        float y = (position.y - origin.y) / cellSize.y;
        float z = (position.z - origin.z) / cellSize.z;
        int rx = Mathf.RoundToInt(x);
        int ry = Mathf.RoundToInt(y);
        int rz = Mathf.RoundToInt(z);
        gridCoord = new Vector3Int(rx, ry, rz);
        return Mathf.Abs(x - rx) <= 1e-5f &&
               Mathf.Abs(y - ry) <= 1e-5f &&
               Mathf.Abs(z - rz) <= 1e-5f;
    }

    private float EvaluateEdgeRefinement(IScalarFieldSource source, Vector3 position)
    {
        _edgeRefinementEvaluations++;
        return EvaluateSource(source, position);
    }

    private float EvaluateGradient(IScalarFieldSource source, Vector3 position)
    {
        if (_gradientSampleCache.TryGetValue(position, out float cached))
        {
            _gradientCacheHits++;
            return cached;
        }

        _gradientCacheMisses++;
        _gradientEvaluations++;
        float value = EvaluateSource(source, position);
        _gradientSampleCache[position] = value;
        return value;
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
