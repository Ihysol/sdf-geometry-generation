using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;

[System.Serializable]
public class FlatOctreeVolumeBuilder : VolumeBuilderBase<OctreeVolume>
{
    public readonly struct BuildStats
    {
        public readonly double totalMs;
        public readonly double recursiveBuildMs;
        public readonly double createLayoutMs;
        public readonly double runtimeCacheMs;
        public readonly double surfaceVertexMs;
        public readonly double surfaceCrossingMs;
        public readonly double surfaceNormalMs;
        public readonly int totalNodes;
        public readonly int surfaceLeaves;
        public readonly Vector3 buildBoundsSize;
        public readonly int sourceEvaluations;
        public readonly int cornerCacheHits;
        public readonly int cornerCacheMisses;
        public readonly int centerEvaluations;
        public readonly int edgeRefinementEvaluations;
        public readonly int crossingCacheHits;
        public readonly int crossingCacheMisses;
        public readonly int persistentCrossingCacheHits;
        public readonly int persistentCrossingCacheInvalidated;
        public readonly int persistentCrossingCacheSize;
        public readonly int subdivisionMinDepth;
        public readonly int subdivisionCornerCrossing;
        public readonly int subdivisionCenterMismatch;
        public readonly int subdivisionDistanceThreshold;
        public readonly int subdivisionOnlyMinDepth;
        public readonly int subdivisionOnlyCornerCrossing;
        public readonly int subdivisionOnlyCenterMismatch;
        public readonly int subdivisionOnlyDistanceThreshold;
        public readonly int subdivisionMixedReasons;
        public readonly int gcGen0Delta;
        public readonly int gcGen1Delta;
        public readonly int gcGen2Delta;

        public BuildStats(
            double totalMs,
            double recursiveBuildMs,
            double createLayoutMs,
            double runtimeCacheMs,
            double surfaceVertexMs,
            double surfaceCrossingMs,
            double surfaceNormalMs,
            int totalNodes,
            int surfaceLeaves,
            Vector3 buildBoundsSize,
            int sourceEvaluations,
            int cornerCacheHits,
            int cornerCacheMisses,
            int centerEvaluations,
            int edgeRefinementEvaluations,
            int crossingCacheHits,
            int crossingCacheMisses,
            int persistentCrossingCacheHits,
            int persistentCrossingCacheInvalidated,
            int persistentCrossingCacheSize,
            int subdivisionMinDepth,
            int subdivisionCornerCrossing,
            int subdivisionCenterMismatch,
            int subdivisionDistanceThreshold,
            int subdivisionOnlyMinDepth,
            int subdivisionOnlyCornerCrossing,
            int subdivisionOnlyCenterMismatch,
            int subdivisionOnlyDistanceThreshold,
            int subdivisionMixedReasons,
            int gcGen0Delta,
            int gcGen1Delta,
            int gcGen2Delta)
        {
            this.totalMs = totalMs;
            this.recursiveBuildMs = recursiveBuildMs;
            this.createLayoutMs = createLayoutMs;
            this.runtimeCacheMs = runtimeCacheMs;
            this.surfaceVertexMs = surfaceVertexMs;
            this.surfaceCrossingMs = surfaceCrossingMs;
            this.surfaceNormalMs = surfaceNormalMs;
            this.totalNodes = totalNodes;
            this.surfaceLeaves = surfaceLeaves;
            this.buildBoundsSize = buildBoundsSize;
            this.sourceEvaluations = sourceEvaluations;
            this.cornerCacheHits = cornerCacheHits;
            this.cornerCacheMisses = cornerCacheMisses;
            this.centerEvaluations = centerEvaluations;
            this.edgeRefinementEvaluations = edgeRefinementEvaluations;
            this.crossingCacheHits = crossingCacheHits;
            this.crossingCacheMisses = crossingCacheMisses;
            this.persistentCrossingCacheHits = persistentCrossingCacheHits;
            this.persistentCrossingCacheInvalidated = persistentCrossingCacheInvalidated;
            this.persistentCrossingCacheSize = persistentCrossingCacheSize;
            this.subdivisionMinDepth = subdivisionMinDepth;
            this.subdivisionCornerCrossing = subdivisionCornerCrossing;
            this.subdivisionCenterMismatch = subdivisionCenterMismatch;
            this.subdivisionDistanceThreshold = subdivisionDistanceThreshold;
            this.subdivisionOnlyMinDepth = subdivisionOnlyMinDepth;
            this.subdivisionOnlyCornerCrossing = subdivisionOnlyCornerCrossing;
            this.subdivisionOnlyCenterMismatch = subdivisionOnlyCenterMismatch;
            this.subdivisionOnlyDistanceThreshold = subdivisionOnlyDistanceThreshold;
            this.subdivisionMixedReasons = subdivisionMixedReasons;
            this.gcGen0Delta = gcGen0Delta;
            this.gcGen1Delta = gcGen1Delta;
            this.gcGen2Delta = gcGen2Delta;
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
    public int edgeRefinementSteps = 3;

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
    }

    private struct NodeRecord
    {
        public Vector3 Center;
        public Vector3 Size;
        public Vector3 SurfaceVertex;
        public Vector3 SurfaceNormal;
        public Vector3Int Coord;
        public Vector3Int SizeInCells;
        public int FirstChildIndex;
        public byte ChildMask;
        public byte Flags;
        public CornerSamples Corners;
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

    private const int MaxDenseCornerCacheEntries = 8 * 1024 * 1024;
    private const int InitialNodeCapacity = 32 * 1024;
    private const int InitialFallbackCornerCapacity = 1024;
    private const int InitialAverageCrossingCapacity = 64 * 1024;

    private readonly struct CrossingCacheEntry
    {
        public readonly Vector3 Crossing;
        public readonly Bounds EdgeBounds;

        public CrossingCacheEntry(Vector3 crossing, Bounds edgeBounds)
        {
            Crossing = crossing;
            EdgeBounds = edgeBounds;
        }
    }

    private readonly List<NodeRecord> _nodes = new(InitialNodeCapacity);
    private readonly Dictionary<Vector3Int, float> _cornerSampleCacheFallback = new(InitialFallbackCornerCapacity);
    private readonly Dictionary<OctreeHermiteEdgeKey, CrossingCacheEntry> _averageCrossingCache = new(InitialAverageCrossingCapacity);
    private readonly List<OctreeHermiteEdgeKey> _crossingCacheRemovalBuffer = new(InitialFallbackCornerCapacity);
    private readonly FlatOctreeLayout _layout = new();
    private float[] _cornerSampleValues;
    private byte[] _cornerSampleStates;
    private int _cornerSampleGridSide;
    private Vector3 _crossingCacheBuildCenter;
    private Vector3 _crossingCacheBuildSize;
    private int _crossingCacheMaxDepth = -1;
    private int _crossingCacheRefinementSteps = -1;
    private bool _hasPreparedCrossingCache;
    private bool _hasCrossingCacheDirtyBounds;
    private Bounds _crossingCacheDirtyBounds;
    private int _sourceEvaluations;
    private int _cornerCacheHits;
    private int _cornerCacheMisses;
    private int _centerEvaluations;
    private int _edgeRefinementEvaluations;
    private int _crossingCacheHits;
    private int _crossingCacheMisses;
    private int _persistentCrossingCacheHits;
    private int _persistentCrossingCacheInvalidated;
    private int _surfaceLeaves;
    private int _subdivisionMinDepth;
    private int _subdivisionCornerCrossing;
    private int _subdivisionCenterMismatch;
    private int _subdivisionDistanceThreshold;
    private int _subdivisionOnlyMinDepth;
    private int _subdivisionOnlyCornerCrossing;
    private int _subdivisionOnlyCenterMismatch;
    private int _subdivisionOnlyDistanceThreshold;
    private int _subdivisionMixedReasons;
    private long _surfaceVertexTicks;
    private long _surfaceCrossingTicks;
    private long _surfaceNormalTicks;

    public BuildStats LastBuildStats { get; private set; }

    public override Bounds Bounds
    {
        get
        {
            Vector3 paddedSize = size + Vector3.one * boundsPadding * 2f;
            return new Bounds(center, paddedSize);
        }
    }

    public void PreparePersistentCrossingCache(bool hasDirtyBounds, Bounds dirtyBounds)
    {
        _hasPreparedCrossingCache = true;
        _hasCrossingCacheDirtyBounds = hasDirtyBounds;
        _crossingCacheDirtyBounds = dirtyBounds;
    }

    public override OctreeVolume Build(IScalarFieldSource source)
    {
        Stopwatch totalStopwatch = Stopwatch.StartNew();
        Stopwatch recursiveStopwatch = Stopwatch.StartNew();
#if UNITY_EDITOR
        int gc0Before = System.GC.CollectionCount(0);
        int gc1Before = System.GC.CollectionCount(1);
        int gc2Before = System.GC.CollectionCount(2);
#else
        const int gc0Before = 0;
        const int gc1Before = 0;
        const int gc2Before = 0;
#endif
        ResetState();
        PrepareCrossingCacheForBuild();
        PrepareCornerSampleCache();

        Bounds buildBounds = Bounds;
        Vector3 origin = buildBounds.min;
        Vector3 cellSize = buildBounds.size / (1 << maxDepth);

        BuildNode(source, buildBounds, 0, origin, cellSize);

        recursiveStopwatch.Stop();
        Stopwatch phaseStopwatch = Stopwatch.StartNew();
        FlatOctreeLayout layout = CreateLayout();
        phaseStopwatch.Stop();
        double createLayoutMs = phaseStopwatch.Elapsed.TotalMilliseconds;
        phaseStopwatch.Restart();
        layout.EnsureRuntimeCache();
        phaseStopwatch.Stop();
        double runtimeCacheMs = phaseStopwatch.Elapsed.TotalMilliseconds;
        totalStopwatch.Stop();
        CaptureBuildStats(
            totalStopwatch.Elapsed.TotalMilliseconds,
            recursiveStopwatch.Elapsed.TotalMilliseconds,
            createLayoutMs,
            runtimeCacheMs,
            buildBounds.size,
            System.GC.CollectionCount(0) - gc0Before,
            System.GC.CollectionCount(1) - gc1Before,
            System.GC.CollectionCount(2) - gc2Before);

#if UNITY_EDITOR
        if (!suppressBuildLog && UnityEngine.Debug.isDebugBuild)
        {
            UnityEngine.Debug.Log(
                $"Flat Octree Build: nodes={_nodes.Count}, surfaceLeaves={_surfaceLeaves}, bounds={buildBounds}, refinementSteps={edgeRefinementSteps}, " +
                $"timing(total={LastBuildStats.totalMs:F2} ms, recursive={LastBuildStats.recursiveBuildMs:F2} ms, createLayout={LastBuildStats.createLayoutMs:F2} ms, runtimeCache={LastBuildStats.runtimeCacheMs:F2} ms, surfaceVertex={LastBuildStats.surfaceVertexMs:F2} ms, surfaceCrossing={LastBuildStats.surfaceCrossingMs:F2} ms, surfaceNormal={LastBuildStats.surfaceNormalMs:F2} ms), " +
                $"samples(total={LastBuildStats.sourceEvaluations}, cornerMiss={LastBuildStats.cornerCacheMisses}, center={LastBuildStats.centerEvaluations}, edge={LastBuildStats.edgeRefinementEvaluations}), " +
                $"cornerCache(hit={LastBuildStats.cornerCacheHits}, miss={LastBuildStats.cornerCacheMisses}), " +
                $"crossingCache(hit={LastBuildStats.crossingCacheHits}, miss={LastBuildStats.crossingCacheMisses}, persistentHit={LastBuildStats.persistentCrossingCacheHits}, invalidated={LastBuildStats.persistentCrossingCacheInvalidated}, size={LastBuildStats.persistentCrossingCacheSize}), " +
                $"subdivision(minDepth={LastBuildStats.subdivisionMinDepth}, crossing={LastBuildStats.subdivisionCornerCrossing}, centerMismatch={LastBuildStats.subdivisionCenterMismatch}, distance={LastBuildStats.subdivisionDistanceThreshold}), " +
                $"exclusive(minDepth={LastBuildStats.subdivisionOnlyMinDepth}, crossing={LastBuildStats.subdivisionOnlyCornerCrossing}, centerMismatch={LastBuildStats.subdivisionOnlyCenterMismatch}, distance={LastBuildStats.subdivisionOnlyDistanceThreshold}, mixed={LastBuildStats.subdivisionMixedReasons}), " +
                $"gc(gen0={LastBuildStats.gcGen0Delta}, gen1={LastBuildStats.gcGen1Delta}, gen2={LastBuildStats.gcGen2Delta})"
            );
        }
#endif

        return new OctreeVolume(
            null,
            buildBounds,
            maxDepth,
            _nodes.Count,
            _surfaceLeaves,
            source,
            origin,
            cellSize,
            flatLayout: layout
        );
    }

    private int BuildNode(IScalarFieldSource source, Bounds bounds, int depth, Vector3 origin, Vector3 cellSize)
    {
        int nodeIndex = _nodes.Count;
        CornerSamples corners = SampleCorners(source, bounds, origin, cellSize);

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
        bool canSubdivide = depth < maxDepth;
        bool forcedSubdivide = depth < minDepth || cornerContainsSurface;
        bool needsCenterDecision = canSubdivide && !forcedSubdivide;
        bool centerDiffersFromCorners = false;
        bool couldContainSurface = false;
        if (needsCenterDecision)
        {
            float centerValue = EvaluateCenter(source, bounds.center, origin, cellSize);
            centerDiffersFromCorners =
                (centerValue < 0f && cornerHasPositive) ||
                (centerValue >= 0f && cornerHasNegative);
            couldContainSurface = Mathf.Abs(centerValue) <= bounds.extents.magnitude;
        }
        bool minDepthReason = depth < minDepth;
        bool cornerCrossingReason = cornerContainsSurface;
        bool centerMismatchReason = centerDiffersFromCorners;
        bool distanceThresholdReason = couldContainSurface;
        int subdivisionReasons = 0;
        if (minDepthReason)
        {
            _subdivisionMinDepth++;
            subdivisionReasons |= 1;
        }
        if (cornerCrossingReason)
        {
            _subdivisionCornerCrossing++;
            subdivisionReasons |= 2;
        }
        if (centerMismatchReason)
        {
            _subdivisionCenterMismatch++;
            subdivisionReasons |= 4;
        }
        if (distanceThresholdReason)
        {
            _subdivisionDistanceThreshold++;
            subdivisionReasons |= 8;
        }
        CountExclusiveSubdivisionReason(subdivisionReasons);

        bool shouldSubdivide = subdivisionReasons != 0;

        NodeRecord record = new NodeRecord
        {
            Center = bounds.center,
            Size = bounds.size,
            Coord = GetCoord(bounds, origin, cellSize),
            SizeInCells = GetSizeInCells(bounds, cellSize),
            FirstChildIndex = -1,
            ChildMask = 0,
            Corners = corners
        };
        _nodes.Add(record);

        if (!shouldSubdivide)
        {
            record.Flags = FlatOctreeLayout.FlagLeaf;
            _nodes[nodeIndex] = record;
            return nodeIndex;
        }

        if (depth >= maxDepth)
        {
            record.Flags = FlatOctreeLayout.FlagLeaf;
            if (cornerContainsSurface)
            {
                record.Flags |= FlatOctreeLayout.FlagSurface;
                long surfaceVertexStart = Stopwatch.GetTimestamp();
                EstimateSurfaceVertexAndNormal(source, bounds, corners, origin, cellSize, out record.SurfaceVertex, out record.SurfaceNormal);
                _surfaceVertexTicks += Stopwatch.GetTimestamp() - surfaceVertexStart;
                _surfaceLeaves++;
            }
            _nodes[nodeIndex] = record;
            return nodeIndex;
        }

        Vector3 childSize = bounds.size * 0.5f;
        Vector3 min = bounds.min;
        int childWriteStart = _nodes.Count;
        byte childMask = 0;

        for (int x = 0; x < 2; x++)
        for (int y = 0; y < 2; y++)
        for (int z = 0; z < 2; z++)
        {
            int childOctant = (x << 2) | (y << 1) | z;
            Vector3 childCenter = min + new Vector3(
                (x + 0.5f) * childSize.x,
                (y + 0.5f) * childSize.y,
                (z + 0.5f) * childSize.z
            );
            BuildNode(source, new Bounds(childCenter, childSize), depth + 1, origin, cellSize);
            childMask |= (byte)(1 << childOctant);
        }

        record.FirstChildIndex = childWriteStart;
        record.ChildMask = childMask;
        _nodes[nodeIndex] = record;
        return nodeIndex;
    }

    private FlatOctreeLayout CreateLayout()
    {
        int count = _nodes.Count;
        Vector3[] centers = EnsureVector3Array(_layout.Centers, count);
        Vector3[] sizes = EnsureVector3Array(_layout.Sizes, count);
        Vector3[] surfaceVertices = EnsureVector3Array(_layout.SurfaceVertices, count);
        Vector3[] surfaceNormals = EnsureVector3Array(_layout.SurfaceNormals, count);
        Vector3Int[] coords = EnsureVector3IntArray(_layout.Coords, count);
        Vector3Int[] nodeSizeInCells = EnsureVector3IntArray(_layout.NodeSizeInCells, count);
        float[] cornerValues8 = EnsureFloatArray(_layout.CornerValues8, count * 8);
        int[] firstChildIndex = EnsureIntArray(_layout.FirstChildIndex, count);
        byte[] childMask = EnsureByteArray(_layout.ChildMask, count);
        byte[] flags = EnsureByteArray(_layout.Flags, count);

        for (int i = 0; i < count; i++)
        {
            NodeRecord n = _nodes[i];
            centers[i] = n.Center;
            sizes[i] = n.Size;
            surfaceVertices[i] = n.SurfaceVertex;
            surfaceNormals[i] = n.SurfaceNormal;
            coords[i] = n.Coord;
            nodeSizeInCells[i] = n.SizeInCells;
            firstChildIndex[i] = n.FirstChildIndex;
            childMask[i] = n.ChildMask;
            flags[i] = n.Flags;

            int cornerBase = i * 8;
            for (int c = 0; c < 8; c++)
                cornerValues8[cornerBase + c] = n.Corners[c];
        }

        _layout.Centers = centers;
        _layout.Sizes = sizes;
        _layout.SurfaceVertices = surfaceVertices;
        _layout.SurfaceNormals = surfaceNormals;
        _layout.Coords = coords;
        _layout.NodeSizeInCells = nodeSizeInCells;
        _layout.CornerValues8 = cornerValues8;
        _layout.FirstChildIndex = firstChildIndex;
        _layout.ChildMask = childMask;
        _layout.Flags = flags;
        _layout.SetCount(count);
        return _layout;
    }

    private static Vector3[] EnsureVector3Array(Vector3[] array, int required)
    {
        return array == null || array.Length < required ? new Vector3[required] : array;
    }

    private static Vector3Int[] EnsureVector3IntArray(Vector3Int[] array, int required)
    {
        return array == null || array.Length < required ? new Vector3Int[required] : array;
    }

    private static float[] EnsureFloatArray(float[] array, int required)
    {
        return array == null || array.Length < required ? new float[required] : array;
    }

    private static int[] EnsureIntArray(int[] array, int required)
    {
        return array == null || array.Length < required ? new int[required] : array;
    }

    private static byte[] EnsureByteArray(byte[] array, int required)
    {
        return array == null || array.Length < required ? new byte[required] : array;
    }

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

    private void EstimateSurfaceVertexAndNormal(
        IScalarFieldSource source,
        Bounds bounds,
        CornerSamples cornerValues,
        Vector3 origin,
        Vector3 cellSize,
        out Vector3 surfaceVertex,
        out Vector3 surfaceNormal)
    {
        Vector3 min = bounds.min;
        Vector3 max = bounds.max;
        Vector3Int minCoord = WorldToGridVertex(min.x, min.y, min.z, origin, cellSize);
        Vector3Int maxCoord = WorldToGridVertex(max.x, max.y, max.z, origin, cellSize);
        Vector3 sum = Vector3.zero;
        int count = 0;
        long crossingStart = Stopwatch.GetTimestamp();

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
            AddAverageCrossingForEdge(source, pa, pb, va, vb, ca, cb, ref sum, ref count);
        }

        surfaceVertex = count == 0
            ? SnapToGridNearBoundary(bounds.center, origin, cellSize)
            : SnapToGridNearBoundary(sum / count, origin, cellSize);

        _surfaceCrossingTicks += Stopwatch.GetTimestamp() - crossingStart;
        long normalStart = Stopwatch.GetTimestamp();
        surfaceNormal = EstimateCornerNormal(cornerValues, bounds.size);
        _surfaceNormalTicks += Stopwatch.GetTimestamp() - normalStart;
    }

    private void AddAverageCrossingForEdge(
        IScalarFieldSource source,
        Vector3 pa,
        Vector3 pb,
        float va,
        float vb,
        Vector3Int ca,
        Vector3Int cb,
        ref Vector3 sum,
        ref int count)
    {
        OctreeHermiteEdgeKey key = new OctreeHermiteEdgeKey(ca, cb);
        if (_averageCrossingCache.TryGetValue(key, out CrossingCacheEntry entry))
        {
            _crossingCacheHits++;
            _persistentCrossingCacheHits++;
            sum += entry.Crossing;
            count++;
            return;
        }

        _crossingCacheMisses++;
        Vector3 crossing = RefineEdgeIntersection(source, pa, pb, va, vb, 0f);
        _averageCrossingCache[key] = new CrossingCacheEntry(crossing, CreateEdgeBounds(pa, pb));
        sum += crossing;
        count++;
    }

    private Vector3 RefineEdgeIntersection(IScalarFieldSource source, Vector3 pa, Vector3 pb, float va, float vb, float isoLevel)
    {
        float fa = va - isoLevel;
        float fb = vb - isoLevel;
        if (Mathf.Abs(fa) < 1e-8f)
            return pa;
        if (Mathf.Abs(fb) < 1e-8f)
            return pb;

        Vector3 a = pa;
        Vector3 b = pb;
        float fA = fa;
        float fB = fb;
        float t = Mathf.Clamp01(fA / (fA - fB));
        Vector3 best = Vector3.Lerp(a, b, t);

        for (int i = 0; i < Mathf.Max(0, edgeRefinementSteps); i++)
        {
            t = Mathf.Clamp01(fA / (fA - fB));
            Vector3 p = Vector3.Lerp(a, b, t);
            float fP = EvaluateEdgeRefinement(source, p) - isoLevel;
            best = p;

            if (Mathf.Abs(fP) < 1e-6f)
                break;

            if ((fA <= 0f && fP > 0f) || (fA > 0f && fP <= 0f))
            {
                b = p;
                fB = fP;
            }
            else
            {
                a = p;
                fA = fP;
            }
        }

        return best;
    }

    private static Vector3 EstimateCornerNormal(CornerSamples cornerValues, Vector3 size)
    {
        float invX = size.x > 1e-8f ? 1f / size.x : 1f;
        float invY = size.y > 1e-8f ? 1f / size.y : 1f;
        float invZ = size.z > 1e-8f ? 1f / size.z : 1f;

        Vector3 normal = new Vector3(
            ((cornerValues[1] + cornerValues[2] + cornerValues[5] + cornerValues[6]) -
             (cornerValues[0] + cornerValues[3] + cornerValues[4] + cornerValues[7])) * invX,
            ((cornerValues[2] + cornerValues[3] + cornerValues[6] + cornerValues[7]) -
             (cornerValues[0] + cornerValues[1] + cornerValues[4] + cornerValues[5])) * invY,
            ((cornerValues[4] + cornerValues[5] + cornerValues[6] + cornerValues[7]) -
             (cornerValues[0] + cornerValues[1] + cornerValues[2] + cornerValues[3])) * invZ
        );

        if (normal.sqrMagnitude > 1e-12f)
        {
            normal.Normalize();
            return normal;
        }

        return Vector3.up;
    }

    private Vector3Int GetCoord(Bounds bounds, Vector3 origin, Vector3 cellSize)
    {
        Vector3 local = bounds.center - origin;
        return new Vector3Int(
            Mathf.RoundToInt(local.x / cellSize.x - 0.5f),
            Mathf.RoundToInt(local.y / cellSize.y - 0.5f),
            Mathf.RoundToInt(local.z / cellSize.z - 0.5f)
        );
    }

    private Vector3Int GetSizeInCells(Bounds bounds, Vector3 cellSize)
    {
        return new Vector3Int(
            Mathf.Max(1, Mathf.RoundToInt(bounds.size.x / cellSize.x)),
            Mathf.Max(1, Mathf.RoundToInt(bounds.size.y / cellSize.y)),
            Mathf.Max(1, Mathf.RoundToInt(bounds.size.z / cellSize.z))
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

    private bool TryGetGridVertex(Vector3 position, Vector3 origin, Vector3 cellSize, out Vector3Int gridCoord)
    {
        float gx = (position.x - origin.x) / cellSize.x;
        float gy = (position.y - origin.y) / cellSize.y;
        float gz = (position.z - origin.z) / cellSize.z;
        int ix = Mathf.RoundToInt(gx);
        int iy = Mathf.RoundToInt(gy);
        int iz = Mathf.RoundToInt(gz);
        const float eps = 1e-4f;
        if (Mathf.Abs(gx - ix) <= eps && Mathf.Abs(gy - iy) <= eps && Mathf.Abs(gz - iz) <= eps)
        {
            gridCoord = new Vector3Int(ix, iy, iz);
            return true;
        }

        gridCoord = default;
        return false;
    }

    private float EvaluateCornerCached(IScalarFieldSource source, Vector3Int gridCoord, Vector3 worldPos)
    {
        if (TryGetCornerSample(gridCoord, out float cached))
        {
            _cornerCacheHits++;
            return cached;
        }

        _cornerCacheMisses++;
        float value = EvaluateSource(source, worldPos);
        StoreCornerSample(gridCoord, value);
        return value;
    }

    private float EvaluateCenter(IScalarFieldSource source, Vector3 position, Vector3 origin, Vector3 cellSize)
    {
        _centerEvaluations++;
        if (TryGetGridVertex(position, origin, cellSize, out Vector3Int gridCoord) &&
            TryGetCornerSample(gridCoord, out float cached))
        {
            return cached;
        }

        return EvaluateSource(source, position);
    }

    private float EvaluateEdgeRefinement(IScalarFieldSource source, Vector3 position)
    {
        _edgeRefinementEvaluations++;
        return EvaluateSource(source, position);
    }

    private float EvaluateSource(IScalarFieldSource source, Vector3 position)
    {
        _sourceEvaluations++;
        return source.Evaluate(position);
    }

    private void PrepareCornerSampleCache()
    {
        _cornerSampleCacheFallback.Clear();
        int side = (1 << maxDepth) + 1;
        long entries = (long)side * side * side;
        if (entries > 0 && entries <= MaxDenseCornerCacheEntries)
        {
            int count = (int)entries;
            if (_cornerSampleValues == null || _cornerSampleValues.Length != count)
            {
                _cornerSampleValues = new float[count];
                _cornerSampleStates = new byte[count];
            }
            else
            {
                System.Array.Clear(_cornerSampleStates, 0, _cornerSampleStates.Length);
            }
            _cornerSampleGridSide = side;
        }
        else
        {
            _cornerSampleValues = null;
            _cornerSampleStates = null;
            _cornerSampleGridSide = 0;
        }
    }

    private bool TryGetCornerSample(Vector3Int gridCoord, out float value)
    {
        if (_cornerSampleValues != null && IsDenseCornerCoord(gridCoord))
        {
            int index = DenseCornerIndex(gridCoord);
            if (_cornerSampleStates[index] != 0)
            {
                value = _cornerSampleValues[index];
                return true;
            }
            value = 0f;
            return false;
        }

        return _cornerSampleCacheFallback.TryGetValue(gridCoord, out value);
    }

    private void StoreCornerSample(Vector3Int gridCoord, float value)
    {
        if (_cornerSampleValues != null && IsDenseCornerCoord(gridCoord))
        {
            int index = DenseCornerIndex(gridCoord);
            _cornerSampleValues[index] = value;
            _cornerSampleStates[index] = 1;
            return;
        }

        _cornerSampleCacheFallback[gridCoord] = value;
    }

    private bool IsDenseCornerCoord(Vector3Int coord)
    {
        return coord.x >= 0 && coord.x < _cornerSampleGridSide &&
               coord.y >= 0 && coord.y < _cornerSampleGridSide &&
               coord.z >= 0 && coord.z < _cornerSampleGridSide;
    }

    private int DenseCornerIndex(Vector3Int coord)
    {
        return coord.x + _cornerSampleGridSide * (coord.y + _cornerSampleGridSide * coord.z);
    }

    private Vector3 SnapToGridNearBoundary(Vector3 p, Vector3 origin, Vector3 cellSize)
    {
        const float snapEpsilon = 0.015f;
        float gx = (p.x - origin.x) / cellSize.x;
        float gy = (p.y - origin.y) / cellSize.y;
        float gz = (p.z - origin.z) / cellSize.z;
        float rx = Mathf.Round(gx);
        float ry = Mathf.Round(gy);
        float rz = Mathf.Round(gz);

        if (Mathf.Abs(gx - rx) <= snapEpsilon)
            p.x = origin.x + rx * cellSize.x;
        if (Mathf.Abs(gy - ry) <= snapEpsilon)
            p.y = origin.y + ry * cellSize.y;
        if (Mathf.Abs(gz - rz) <= snapEpsilon)
            p.z = origin.z + rz * cellSize.z;

        return p;
    }

    private bool HasCrossing(float a, float b)
    {
        return (a <= 0f && b > 0f) || (a > 0f && b <= 0f);
    }

    private void PrepareCrossingCacheForBuild()
    {
        bool clearCache =
            !_hasPreparedCrossingCache ||
            !_hasCrossingCacheDirtyBounds ||
            _crossingCacheBuildCenter != center ||
            _crossingCacheBuildSize != size ||
            _crossingCacheMaxDepth != maxDepth ||
            _crossingCacheRefinementSteps != edgeRefinementSteps;

        if (clearCache)
        {
            _averageCrossingCache.Clear();
        }
        else
        {
            Bounds expandedDirtyBounds = _crossingCacheDirtyBounds;
            float epsilonPadding = Bounds.size.magnitude * 1e-5f;
            expandedDirtyBounds.Expand(epsilonPadding);
            InvalidateCrossingCache(expandedDirtyBounds);
        }

        _crossingCacheBuildCenter = center;
        _crossingCacheBuildSize = size;
        _crossingCacheMaxDepth = maxDepth;
        _crossingCacheRefinementSteps = edgeRefinementSteps;
        _hasPreparedCrossingCache = false;
    }

    private void InvalidateCrossingCache(Bounds dirtyBounds)
    {
        if (_averageCrossingCache.Count == 0)
            return;

        _crossingCacheRemovalBuffer.Clear();
        foreach (KeyValuePair<OctreeHermiteEdgeKey, CrossingCacheEntry> pair in _averageCrossingCache)
        {
            if (pair.Value.EdgeBounds.Intersects(dirtyBounds))
                _crossingCacheRemovalBuffer.Add(pair.Key);
        }

        for (int i = 0; i < _crossingCacheRemovalBuffer.Count; i++)
            _averageCrossingCache.Remove(_crossingCacheRemovalBuffer[i]);

        _persistentCrossingCacheInvalidated = _crossingCacheRemovalBuffer.Count;
        _crossingCacheRemovalBuffer.Clear();
    }

    private static Bounds CreateEdgeBounds(Vector3 a, Vector3 b)
    {
        Vector3 min = Vector3.Min(a, b);
        Vector3 max = Vector3.Max(a, b);
        Bounds bounds = new Bounds((min + max) * 0.5f, max - min);
        bounds.Expand(1e-5f);
        return bounds;
    }

    private void ResetState()
    {
        _nodes.Clear();
        _sourceEvaluations = 0;
        _cornerCacheHits = 0;
        _cornerCacheMisses = 0;
        _centerEvaluations = 0;
        _edgeRefinementEvaluations = 0;
        _crossingCacheHits = 0;
        _crossingCacheMisses = 0;
        _persistentCrossingCacheHits = 0;
        _persistentCrossingCacheInvalidated = 0;
        _surfaceLeaves = 0;
        _subdivisionMinDepth = 0;
        _subdivisionCornerCrossing = 0;
        _subdivisionCenterMismatch = 0;
        _subdivisionDistanceThreshold = 0;
        _subdivisionOnlyMinDepth = 0;
        _subdivisionOnlyCornerCrossing = 0;
        _subdivisionOnlyCenterMismatch = 0;
        _subdivisionOnlyDistanceThreshold = 0;
        _subdivisionMixedReasons = 0;
        _surfaceVertexTicks = 0;
        _surfaceCrossingTicks = 0;
        _surfaceNormalTicks = 0;
    }

    private void CountExclusiveSubdivisionReason(int subdivisionReasons)
    {
        switch (subdivisionReasons)
        {
            case 0: break;
            case 1: _subdivisionOnlyMinDepth++; break;
            case 2: _subdivisionOnlyCornerCrossing++; break;
            case 4: _subdivisionOnlyCenterMismatch++; break;
            case 8: _subdivisionOnlyDistanceThreshold++; break;
            default:
                _subdivisionMixedReasons++;
                break;
        }
    }

    private void CaptureBuildStats(
        double totalMs,
        double recursiveBuildMs,
        double createLayoutMs,
        double runtimeCacheMs,
        Vector3 buildBoundsSize,
        int gcGen0Delta,
        int gcGen1Delta,
        int gcGen2Delta)
    {
        long surfaceVertexExclusiveTicks = System.Math.Max(0, _surfaceVertexTicks - _surfaceCrossingTicks - _surfaceNormalTicks);
        LastBuildStats = new BuildStats(
            totalMs,
            recursiveBuildMs,
            createLayoutMs,
            runtimeCacheMs,
            surfaceVertexExclusiveTicks * 1000d / Stopwatch.Frequency,
            _surfaceCrossingTicks * 1000d / Stopwatch.Frequency,
            _surfaceNormalTicks * 1000d / Stopwatch.Frequency,
            _nodes.Count,
            _surfaceLeaves,
            buildBoundsSize,
            _sourceEvaluations,
            _cornerCacheHits,
            _cornerCacheMisses,
            _centerEvaluations,
            _edgeRefinementEvaluations,
            _crossingCacheHits,
            _crossingCacheMisses,
            _persistentCrossingCacheHits,
            _persistentCrossingCacheInvalidated,
            _averageCrossingCache.Count,
            _subdivisionMinDepth,
            _subdivisionCornerCrossing,
            _subdivisionCenterMismatch,
            _subdivisionDistanceThreshold,
            _subdivisionOnlyMinDepth,
            _subdivisionOnlyCornerCrossing,
            _subdivisionOnlyCenterMismatch,
            _subdivisionOnlyDistanceThreshold,
            _subdivisionMixedReasons,
            gcGen0Delta,
            gcGen1Delta,
            gcGen2Delta
        );
    }
}
