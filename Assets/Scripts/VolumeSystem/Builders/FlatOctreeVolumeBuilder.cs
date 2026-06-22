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
        public readonly double recursiveCornerSampleMs;
        public readonly double recursiveCenterDecisionMs;
        public readonly double recursiveChildCornerMs;
        public readonly double recursiveNodeRecordMs;
        public readonly double recursiveNodeReusePreparationMs;
        public readonly double recursiveCornerCachePreparationMs;
        public readonly double recursiveCenterCachePreparationMs;
        public readonly double recursiveCrossingCachePreparationMs;
        public readonly double recursiveSubtreeCopyMs;
        public readonly double recursiveOtherMs;
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
        public readonly int persistentCornerCacheInvalidated;
        public readonly int persistentCornerCacheSize;
        public readonly int centerEvaluations;
        public readonly int centerCacheHits;
        public readonly int centerCacheMisses;
        public readonly int persistentCenterCacheInvalidated;
        public readonly int persistentCenterCacheSize;
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
        public readonly int reusedNodeCount;
        public readonly int reusedSubtreeCount;
        public readonly int gcGen0Delta;
        public readonly int gcGen1Delta;
        public readonly int gcGen2Delta;

        public BuildStats(
            double totalMs,
            double recursiveBuildMs,
            double recursiveCornerSampleMs,
            double recursiveCenterDecisionMs,
            double recursiveChildCornerMs,
            double recursiveNodeRecordMs,
            double recursiveNodeReusePreparationMs,
            double recursiveCornerCachePreparationMs,
            double recursiveCenterCachePreparationMs,
            double recursiveCrossingCachePreparationMs,
            double recursiveSubtreeCopyMs,
            double recursiveOtherMs,
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
            int persistentCornerCacheInvalidated,
            int persistentCornerCacheSize,
            int centerEvaluations,
            int centerCacheHits,
            int centerCacheMisses,
            int persistentCenterCacheInvalidated,
            int persistentCenterCacheSize,
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
            int reusedNodeCount,
            int reusedSubtreeCount,
            int gcGen0Delta,
            int gcGen1Delta,
            int gcGen2Delta)
        {
            this.totalMs = totalMs;
            this.recursiveBuildMs = recursiveBuildMs;
            this.recursiveCornerSampleMs = recursiveCornerSampleMs;
            this.recursiveCenterDecisionMs = recursiveCenterDecisionMs;
            this.recursiveChildCornerMs = recursiveChildCornerMs;
            this.recursiveNodeRecordMs = recursiveNodeRecordMs;
            this.recursiveNodeReusePreparationMs = recursiveNodeReusePreparationMs;
            this.recursiveCornerCachePreparationMs = recursiveCornerCachePreparationMs;
            this.recursiveCenterCachePreparationMs = recursiveCenterCachePreparationMs;
            this.recursiveCrossingCachePreparationMs = recursiveCrossingCachePreparationMs;
            this.recursiveSubtreeCopyMs = recursiveSubtreeCopyMs;
            this.recursiveOtherMs = recursiveOtherMs;
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
            this.persistentCornerCacheInvalidated = persistentCornerCacheInvalidated;
            this.persistentCornerCacheSize = persistentCornerCacheSize;
            this.centerEvaluations = centerEvaluations;
            this.centerCacheHits = centerCacheHits;
            this.centerCacheMisses = centerCacheMisses;
            this.persistentCenterCacheInvalidated = persistentCenterCacheInvalidated;
            this.persistentCenterCacheSize = persistentCenterCacheSize;
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
            this.reusedNodeCount = reusedNodeCount;
            this.reusedSubtreeCount = reusedSubtreeCount;
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
    [HideInInspector]
    public float sampleCacheDirtyPaddingCells = 0f;
    [HideInInspector]
    public bool profileRecursiveParts = false;

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

        public CrossingCacheEntry(Vector3 crossing)
        {
            Crossing = crossing;
        }
    }

    private readonly struct CenterCacheEntry
    {
        public readonly Vector3 Position;
        public readonly float Value;

        public CenterCacheEntry(Vector3 position, float value)
        {
            Position = position;
            Value = value;
        }
    }

    private List<NodeRecord> _nodes = new(InitialNodeCapacity);
    private List<NodeRecord> _previousNodes = new(InitialNodeCapacity);
    private readonly Dictionary<Vector3Int, float> _cornerSampleCacheFallback = new(InitialFallbackCornerCapacity);
    private readonly Dictionary<QuantizedVector3Key, CenterCacheEntry> _centerSampleCache = new(InitialFallbackCornerCapacity);
    private readonly Dictionary<ulong, CrossingCacheEntry> _averageCrossingCache = new(InitialAverageCrossingCapacity);
    private readonly List<ulong> _crossingCacheRemovalBuffer = new(InitialFallbackCornerCapacity);
    private readonly List<Vector3Int> _cornerCacheRemovalBuffer = new(InitialFallbackCornerCapacity);
    private readonly List<QuantizedVector3Key> _centerCacheRemovalBuffer = new(InitialFallbackCornerCapacity);
    private readonly float[] _childCornerSampleScratch = new float[27];
    private readonly FlatOctreeLayout _layout = new();
    private float[] _cornerSampleValues;
    private byte[] _cornerSampleStates;
    private int _cornerSampleGridSide;
    private Vector3 _cornerCacheBuildCenter;
    private Vector3 _cornerCacheBuildSize;
    private float _cornerCacheBoundsPadding;
    private int _cornerCacheMaxDepth = -1;
    private Vector3 _centerCacheBuildCenter;
    private Vector3 _centerCacheBuildSize;
    private float _centerCacheBoundsPadding;
    private int _centerCacheMaxDepth = -1;
    private float _centerCacheQuantum = 1e-6f;
    private Vector3 _crossingCacheBuildCenter;
    private Vector3 _crossingCacheBuildSize;
    private float _crossingCacheBoundsPadding;
    private int _crossingCacheMaxDepth = -1;
    private int _crossingCacheRefinementSteps = -1;
    private int _crossingCacheGridVertexSide = 1;
    private bool _hasPreparedCrossingCache;
    private bool _hasCrossingCacheDirtyBounds;
    private Bounds _crossingCacheDirtyBounds;
    private int[] _previousSubtreeSizes;
    private bool _reusePreviousSubtrees;
    private Bounds _subtreeReuseDirtyBounds;
    private bool _hasPreviousNodeBuild;
    private Vector3 _previousNodeBuildCenter;
    private Vector3 _previousNodeBuildSize;
    private float _previousNodeBoundsPadding;
    private int _previousNodeMaxDepth = -1;
    private int _previousNodeMinDepth = -1;
    private int _previousNodeEdgeRefinementSteps = -1;
    private int _sourceEvaluations;
    private int _cornerCacheHits;
    private int _cornerCacheMisses;
    private int _persistentCornerCacheInvalidated;
    private int _cornerCacheSize;
    private int _centerEvaluations;
    private int _centerCacheHits;
    private int _centerCacheMisses;
    private int _persistentCenterCacheInvalidated;
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
    private int _reusedNodeCount;
    private int _reusedSubtreeCount;
    private long _recursiveCornerSampleTicks;
    private long _recursiveCenterDecisionTicks;
    private long _recursiveChildCornerTicks;
    private long _recursiveNodeRecordTicks;
    private long _recursiveNodeReusePreparationTicks;
    private long _recursiveCornerCachePreparationTicks;
    private long _recursiveCenterCachePreparationTicks;
    private long _recursiveCrossingCachePreparationTicks;
    private long _recursiveSubtreeCopyTicks;
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

    private void PrepareNodeBuffersForBuild(Vector3 cellSize)
    {
        List<NodeRecord> completedNodes = _nodes;
        _nodes = _previousNodes;
        _previousNodes = completedNodes;
        _previousSubtreeSizes = _layout.SubtreeSize;

        bool hasCompatiblePreviousBuild =
            _hasPreviousNodeBuild &&
            _previousNodes.Count > 0 &&
            _previousSubtreeSizes != null &&
            _previousSubtreeSizes.Length >= _previousNodes.Count &&
            _previousNodeBuildCenter == center &&
            _previousNodeBuildSize == size &&
            Mathf.Approximately(_previousNodeBoundsPadding, boundsPadding) &&
            _previousNodeMaxDepth == maxDepth &&
            _previousNodeMinDepth == minDepth &&
            _previousNodeEdgeRefinementSteps == edgeRefinementSteps;

        _reusePreviousSubtrees =
            _hasPreparedCrossingCache &&
            _hasCrossingCacheDirtyBounds &&
            hasCompatiblePreviousBuild;
        _subtreeReuseDirtyBounds = ExpandBoundsByCellPadding(
            _crossingCacheDirtyBounds,
            cellSize,
            sampleCacheDirtyPaddingCells);
    }

    private void RecordNodeBuildConfiguration()
    {
        _previousNodeBuildCenter = center;
        _previousNodeBuildSize = size;
        _previousNodeBoundsPadding = boundsPadding;
        _previousNodeMaxDepth = maxDepth;
        _previousNodeMinDepth = minDepth;
        _previousNodeEdgeRefinementSteps = edgeRefinementSteps;
        _hasPreviousNodeBuild = true;
    }

    private bool TryReusePreviousSubtree(
        Vector3 nodeCenter,
        Vector3 nodeSize,
        int previousNodeIndex,
        out int reusedNodeIndex)
    {
        reusedNodeIndex = -1;
        if (!_reusePreviousSubtrees ||
            !PreviousNodeMatches(previousNodeIndex, nodeCenter, nodeSize) ||
            BoundsOverlapInclusive(nodeCenter, nodeSize, _subtreeReuseDirtyBounds))
        {
            return false;
        }

        int subtreeSize = GetPreviousSubtreeSize(previousNodeIndex);
        if (subtreeSize <= 0 || previousNodeIndex + subtreeSize > _previousNodes.Count)
            return false;

        long subtreeCopyStart = profileRecursiveParts ? Stopwatch.GetTimestamp() : 0L;
        reusedNodeIndex = _nodes.Count;
        int indexOffset = reusedNodeIndex - previousNodeIndex;
        for (int i = 0; i < subtreeSize; i++)
        {
            NodeRecord record = _previousNodes[previousNodeIndex + i];
            if (record.FirstChildIndex >= 0)
                record.FirstChildIndex += indexOffset;
            if ((record.Flags & FlatOctreeLayout.FlagSurface) != 0)
                _surfaceLeaves++;
            _nodes.Add(record);
        }

        _reusedNodeCount += subtreeSize;
        _reusedSubtreeCount++;
        if (profileRecursiveParts)
            _recursiveSubtreeCopyTicks += Stopwatch.GetTimestamp() - subtreeCopyStart;
        return true;
    }

    private int GetPreviousFirstChildIndex(int previousNodeIndex, Vector3 nodeCenter, Vector3 nodeSize)
    {
        if (!_reusePreviousSubtrees || !PreviousNodeMatches(previousNodeIndex, nodeCenter, nodeSize))
            return -1;

        NodeRecord previous = _previousNodes[previousNodeIndex];
        int firstChild = previous.FirstChildIndex;
        return firstChild >= 0 && firstChild < _previousNodes.Count ? firstChild : -1;
    }

    private bool PreviousNodeMatches(int previousNodeIndex, Vector3 nodeCenter, Vector3 nodeSize)
    {
        if (previousNodeIndex < 0 || previousNodeIndex >= _previousNodes.Count)
            return false;

        NodeRecord previous = _previousNodes[previousNodeIndex];
        return previous.Center == nodeCenter && previous.Size == nodeSize;
    }

    private int GetPreviousSubtreeSize(int previousNodeIndex)
    {
        if (_previousSubtreeSizes == null ||
            previousNodeIndex < 0 ||
            previousNodeIndex >= _previousNodes.Count ||
            previousNodeIndex >= _previousSubtreeSizes.Length)
        {
            return 0;
        }

        return _previousSubtreeSizes[previousNodeIndex];
    }

    private static bool BoundsOverlapInclusive(Vector3 center, Vector3 size, Bounds dirtyBounds)
    {
        Vector3 halfSize = size * 0.5f;
        Vector3 min = center - halfSize;
        Vector3 max = center + halfSize;
        Vector3 dirtyMin = dirtyBounds.min;
        Vector3 dirtyMax = dirtyBounds.max;
        return min.x <= dirtyMax.x && max.x >= dirtyMin.x &&
               min.y <= dirtyMax.y && max.y >= dirtyMin.y &&
               min.z <= dirtyMax.z && max.z >= dirtyMin.z;
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
        Bounds buildBounds = Bounds;
        Vector3 origin = buildBounds.min;
        Vector3 cellSize = buildBounds.size / (1 << maxDepth);
        long nodeReusePreparationStart = profileRecursiveParts ? Stopwatch.GetTimestamp() : 0L;
        PrepareNodeBuffersForBuild(cellSize);
        long nodeReusePreparationTicks = profileRecursiveParts
            ? Stopwatch.GetTimestamp() - nodeReusePreparationStart
            : 0L;
        ResetState();
        _recursiveNodeReusePreparationTicks = nodeReusePreparationTicks;

        long cornerCachePreparationStart = profileRecursiveParts ? Stopwatch.GetTimestamp() : 0L;
        PrepareCornerSampleCacheForBuild(origin, cellSize);
        if (profileRecursiveParts)
            _recursiveCornerCachePreparationTicks += Stopwatch.GetTimestamp() - cornerCachePreparationStart;

        long centerCachePreparationStart = profileRecursiveParts ? Stopwatch.GetTimestamp() : 0L;
        PrepareCenterSampleCacheForBuild(origin, cellSize);
        if (profileRecursiveParts)
            _recursiveCenterCachePreparationTicks += Stopwatch.GetTimestamp() - centerCachePreparationStart;

        long crossingCachePreparationStart = profileRecursiveParts ? Stopwatch.GetTimestamp() : 0L;
        PrepareCrossingCacheForBuild(origin, cellSize);
        if (profileRecursiveParts)
            _recursiveCrossingCachePreparationTicks += Stopwatch.GetTimestamp() - crossingCachePreparationStart;

        BuildNode(source, buildBounds.center, buildBounds.size, 0, origin, cellSize, false, default, 0);

        recursiveStopwatch.Stop();
        Stopwatch phaseStopwatch = Stopwatch.StartNew();
        FlatOctreeLayout layout = CreateLayout();
        phaseStopwatch.Stop();
        double createLayoutMs = phaseStopwatch.Elapsed.TotalMilliseconds;
        phaseStopwatch.Restart();
        layout.EnsureRuntimeCache();
        RecordNodeBuildConfiguration();
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
                $"Flat Octree Build: nodes={_nodes.Count}, surfaceLeaves={_surfaceLeaves}, reusedNodes={_reusedNodeCount}, reusedSubtrees={_reusedSubtreeCount}, bounds={buildBounds}, refinementSteps={edgeRefinementSteps}, " +
                $"timing(total={LastBuildStats.totalMs:F2} ms, recursive={LastBuildStats.recursiveBuildMs:F2} ms, createLayout={LastBuildStats.createLayoutMs:F2} ms, runtimeCache={LastBuildStats.runtimeCacheMs:F2} ms, surfaceVertex={LastBuildStats.surfaceVertexMs:F2} ms, surfaceCrossing={LastBuildStats.surfaceCrossingMs:F2} ms, surfaceNormal={LastBuildStats.surfaceNormalMs:F2} ms), " +
                $"{FormatRecursivePartsLog(LastBuildStats)}, " +
                $"samples(total={LastBuildStats.sourceEvaluations}, cornerMiss={LastBuildStats.cornerCacheMisses}, center={LastBuildStats.centerEvaluations}, edge={LastBuildStats.edgeRefinementEvaluations}), " +
                $"cornerCache(hit={LastBuildStats.cornerCacheHits}, miss={LastBuildStats.cornerCacheMisses}, invalidated={LastBuildStats.persistentCornerCacheInvalidated}, size={LastBuildStats.persistentCornerCacheSize}), " +
                $"centerCache(hit={LastBuildStats.centerCacheHits}, miss={LastBuildStats.centerCacheMisses}, invalidated={LastBuildStats.persistentCenterCacheInvalidated}, size={LastBuildStats.persistentCenterCacheSize}), " +
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

    private int BuildNode(
        IScalarFieldSource source,
        Vector3 nodeCenter,
        Vector3 nodeSize,
        int depth,
        Vector3 origin,
        Vector3 cellSize,
        bool hasKnownCorners,
        CornerSamples knownCorners,
        int previousNodeIndex)
    {
        if (TryReusePreviousSubtree(nodeCenter, nodeSize, previousNodeIndex, out int reusedNodeIndex))
            return reusedNodeIndex;

        int nodeIndex = _nodes.Count;
        Vector3 halfSize = nodeSize * 0.5f;
        Vector3 min = nodeCenter - halfSize;
        Vector3 max = nodeCenter + halfSize;
        CornerSamples corners;
        if (hasKnownCorners)
        {
            corners = knownCorners;
        }
        else
        {
            long cornerSampleStart = profileRecursiveParts ? Stopwatch.GetTimestamp() : 0L;
            corners = SampleCorners(source, min, max, origin, cellSize);
            if (profileRecursiveParts)
                _recursiveCornerSampleTicks += Stopwatch.GetTimestamp() - cornerSampleStart;
        }

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
            long centerDecisionStart = profileRecursiveParts ? Stopwatch.GetTimestamp() : 0L;
            float centerValue = EvaluateCenter(source, nodeCenter, origin, cellSize);
            centerDiffersFromCorners =
                (centerValue < 0f && cornerHasPositive) ||
                (centerValue >= 0f && cornerHasNegative);
            couldContainSurface = Mathf.Abs(centerValue) <= halfSize.magnitude;
            if (profileRecursiveParts)
                _recursiveCenterDecisionTicks += Stopwatch.GetTimestamp() - centerDecisionStart;
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

        long nodeRecordStart = profileRecursiveParts ? Stopwatch.GetTimestamp() : 0L;
        NodeRecord record = new NodeRecord
        {
            Center = nodeCenter,
            Size = nodeSize,
            Coord = GetCoord(nodeCenter, origin, cellSize),
            SizeInCells = GetSizeInCells(nodeSize, cellSize),
            FirstChildIndex = -1,
            ChildMask = 0,
            Corners = corners
        };
        _nodes.Add(record);
        if (profileRecursiveParts)
            _recursiveNodeRecordTicks += Stopwatch.GetTimestamp() - nodeRecordStart;

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
                EstimateSurfaceVertexAndNormal(source, nodeCenter, nodeSize, min, max, corners, origin, cellSize, out record.SurfaceVertex, out record.SurfaceNormal);
                _surfaceVertexTicks += Stopwatch.GetTimestamp() - surfaceVertexStart;
                _surfaceLeaves++;
            }
            _nodes[nodeIndex] = record;
            return nodeIndex;
        }

        Vector3 childSize = nodeSize * 0.5f;
        int childWriteStart = _nodes.Count;
        byte childMask = 0;
        int previousChildCursor = GetPreviousFirstChildIndex(previousNodeIndex, nodeCenter, nodeSize);
        byte previousChildMask = previousChildCursor >= 0 ? _previousNodes[previousNodeIndex].ChildMask : (byte)0;
        long childCornerStart = profileRecursiveParts ? Stopwatch.GetTimestamp() : 0L;
        BuildChildCornerSamples(
            source,
            nodeCenter,
            min,
            max,
            corners,
            origin,
            cellSize,
            out CornerSamples child000,
            out CornerSamples child001,
            out CornerSamples child010,
            out CornerSamples child011,
            out CornerSamples child100,
            out CornerSamples child101,
            out CornerSamples child110,
            out CornerSamples child111);
        if (profileRecursiveParts)
            _recursiveChildCornerTicks += Stopwatch.GetTimestamp() - childCornerStart;

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
            CornerSamples childCorners = childOctant switch
            {
                0 => child000,
                1 => child001,
                2 => child010,
                3 => child011,
                4 => child100,
                5 => child101,
                6 => child110,
                _ => child111
            };
            int previousChildIndex = -1;
            if (previousChildCursor >= 0 && (previousChildMask & (1 << childOctant)) != 0)
            {
                previousChildIndex = previousChildCursor;
                previousChildCursor += GetPreviousSubtreeSize(previousChildCursor);
            }
            BuildNode(source, childCenter, childSize, depth + 1, origin, cellSize, true, childCorners, previousChildIndex);
            childMask |= (byte)(1 << childOctant);
        }

        record.FirstChildIndex = childWriteStart;
        record.ChildMask = childMask;
        _nodes[nodeIndex] = record;
        return nodeIndex;
    }

    private void BuildChildCornerSamples(
        IScalarFieldSource source,
        Vector3 center,
        Vector3 min,
        Vector3 max,
        CornerSamples parentCorners,
        Vector3 origin,
        Vector3 cellSize,
        out CornerSamples child000,
        out CornerSamples child001,
        out CornerSamples child010,
        out CornerSamples child011,
        out CornerSamples child100,
        out CornerSamples child101,
        out CornerSamples child110,
        out CornerSamples child111)
    {
        Vector3 mid = center;
        Vector3Int minCoord = WorldToGridVertex(min.x, min.y, min.z, origin, cellSize);
        Vector3Int maxCoord = WorldToGridVertex(max.x, max.y, max.z, origin, cellSize);
        Vector3Int midCoord = new Vector3Int(
            (minCoord.x + maxCoord.x) / 2,
            (minCoord.y + maxCoord.y) / 2,
            (minCoord.z + maxCoord.z) / 2);

        float[] samples = _childCornerSampleScratch;
        samples[Grid3Index(0, 0, 0)] = parentCorners[0];
        samples[Grid3Index(2, 0, 0)] = parentCorners[1];
        samples[Grid3Index(2, 2, 0)] = parentCorners[2];
        samples[Grid3Index(0, 2, 0)] = parentCorners[3];
        samples[Grid3Index(0, 0, 2)] = parentCorners[4];
        samples[Grid3Index(2, 0, 2)] = parentCorners[5];
        samples[Grid3Index(2, 2, 2)] = parentCorners[6];
        samples[Grid3Index(0, 2, 2)] = parentCorners[7];

        for (int z = 0; z < 3; z++)
        for (int y = 0; y < 3; y++)
        for (int x = 0; x < 3; x++)
        {
            if ((x == 0 || x == 2) && (y == 0 || y == 2) && (z == 0 || z == 2))
                continue;

            Vector3Int coord = GetGrid3Coord(x, y, z, minCoord, midCoord, maxCoord);
            Vector3 position = GetGrid3Position(x, y, z, min, mid, max);
            samples[Grid3Index(x, y, z)] = EvaluateCornerCached(source, coord, position);
        }

        child000 = BuildChildCornersFromGrid3(samples, 0, 0, 0);
        child001 = BuildChildCornersFromGrid3(samples, 0, 0, 1);
        child010 = BuildChildCornersFromGrid3(samples, 0, 1, 0);
        child011 = BuildChildCornersFromGrid3(samples, 0, 1, 1);
        child100 = BuildChildCornersFromGrid3(samples, 1, 0, 0);
        child101 = BuildChildCornersFromGrid3(samples, 1, 0, 1);
        child110 = BuildChildCornersFromGrid3(samples, 1, 1, 0);
        child111 = BuildChildCornersFromGrid3(samples, 1, 1, 1);
    }

    private static CornerSamples BuildChildCornersFromGrid3(float[] samples, int x, int y, int z)
    {
        return new CornerSamples(
            samples[Grid3Index(x, y, z)],
            samples[Grid3Index(x + 1, y, z)],
            samples[Grid3Index(x + 1, y + 1, z)],
            samples[Grid3Index(x, y + 1, z)],
            samples[Grid3Index(x, y, z + 1)],
            samples[Grid3Index(x + 1, y, z + 1)],
            samples[Grid3Index(x + 1, y + 1, z + 1)],
            samples[Grid3Index(x, y + 1, z + 1)]);
    }

    private static int Grid3Index(int x, int y, int z)
    {
        return x + 3 * (y + 3 * z);
    }

    private static Vector3Int GetGrid3Coord(
        int x,
        int y,
        int z,
        Vector3Int min,
        Vector3Int mid,
        Vector3Int max)
    {
        return new Vector3Int(
            x == 0 ? min.x : x == 1 ? mid.x : max.x,
            y == 0 ? min.y : y == 1 ? mid.y : max.y,
            z == 0 ? min.z : z == 1 ? mid.z : max.z);
    }

    private static Vector3 GetGrid3Position(int x, int y, int z, Vector3 min, Vector3 mid, Vector3 max)
    {
        return new Vector3(
            x == 0 ? min.x : x == 1 ? mid.x : max.x,
            y == 0 ? min.y : y == 1 ? mid.y : max.y,
            z == 0 ? min.z : z == 1 ? mid.z : max.z);
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

    private CornerSamples SampleCorners(IScalarFieldSource source, Vector3 min, Vector3 max, Vector3 origin, Vector3 cellSize)
    {
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
        Vector3 center,
        Vector3 size,
        Vector3 min,
        Vector3 max,
        CornerSamples cornerValues,
        Vector3 origin,
        Vector3 cellSize,
        out Vector3 surfaceVertex,
        out Vector3 surfaceNormal)
    {
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
            ? SnapToGridNearBoundary(center, origin, cellSize)
            : SnapToGridNearBoundary(sum / count, origin, cellSize);

        _surfaceCrossingTicks += Stopwatch.GetTimestamp() - crossingStart;
        long normalStart = Stopwatch.GetTimestamp();
        surfaceNormal = EstimateCornerNormal(cornerValues, size);
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
        ulong key = PackGridEdgeKey(ca, cb, _crossingCacheGridVertexSide);
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
        _averageCrossingCache[key] = new CrossingCacheEntry(crossing);
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

            if (EdgeRefinementUtility.ResidualIsAcceptable(fP))
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

    private Vector3Int GetCoord(Vector3 center, Vector3 origin, Vector3 cellSize)
    {
        Vector3 local = center - origin;
        return new Vector3Int(
            Mathf.RoundToInt(local.x / cellSize.x - 0.5f),
            Mathf.RoundToInt(local.y / cellSize.y - 0.5f),
            Mathf.RoundToInt(local.z / cellSize.z - 0.5f)
        );
    }

    private Vector3Int GetSizeInCells(Vector3 size, Vector3 cellSize)
    {
        return new Vector3Int(
            Mathf.Max(1, Mathf.RoundToInt(size.x / cellSize.x)),
            Mathf.Max(1, Mathf.RoundToInt(size.y / cellSize.y)),
            Mathf.Max(1, Mathf.RoundToInt(size.z / cellSize.z))
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
            _centerCacheHits++;
            return cached;
        }

        QuantizedVector3Key key = QuantizedVector3Key.FromPosition(position, origin, _centerCacheQuantum);
        if (_centerSampleCache.TryGetValue(key, out CenterCacheEntry entry))
        {
            _centerCacheHits++;
            return entry.Value;
        }

        _centerCacheMisses++;
        float value = EvaluateSource(source, position);
        _centerSampleCache[key] = new CenterCacheEntry(position, value);
        return value;
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

    private void PrepareCornerSampleCacheForBuild(Vector3 origin, Vector3 cellSize)
    {
        int side = (1 << maxDepth) + 1;
        long entries = (long)side * side * side;
        bool useDenseCache = entries > 0 && entries <= MaxDenseCornerCacheEntries;
        bool cacheShapeChanged =
            _cornerCacheBuildCenter != center ||
            _cornerCacheBuildSize != size ||
            !Mathf.Approximately(_cornerCacheBoundsPadding, boundsPadding) ||
            _cornerCacheMaxDepth != maxDepth;
        bool clearCache =
            !_hasPreparedCrossingCache ||
            !_hasCrossingCacheDirtyBounds ||
            cacheShapeChanged;

        if (useDenseCache)
        {
            int count = (int)entries;
            if (_cornerSampleValues == null || _cornerSampleValues.Length != count)
            {
                _cornerSampleValues = new float[count];
                _cornerSampleStates = new byte[count];
                clearCache = true;
            }
            _cornerSampleGridSide = side;
        }
        else
        {
            _cornerSampleValues = null;
            _cornerSampleStates = null;
            _cornerSampleGridSide = 0;
        }

        if (clearCache)
        {
            ClearCornerSampleCache();
        }
        else
        {
            InvalidateCornerSampleCacheForDirtyRegions(origin, cellSize);
        }

        _cornerCacheBuildCenter = center;
        _cornerCacheBuildSize = size;
        _cornerCacheBoundsPadding = boundsPadding;
        _cornerCacheMaxDepth = maxDepth;
    }

    private void PrepareCenterSampleCacheForBuild(Vector3 origin, Vector3 cellSize)
    {
        bool cacheShapeChanged =
            _centerCacheBuildCenter != center ||
            _centerCacheBuildSize != size ||
            !Mathf.Approximately(_centerCacheBoundsPadding, boundsPadding) ||
            _centerCacheMaxDepth != maxDepth;
        bool clearCache =
            !_hasPreparedCrossingCache ||
            !_hasCrossingCacheDirtyBounds ||
            cacheShapeChanged;

        _centerCacheQuantum = QuantizedVector3Key.GetQuantum(cellSize);
        if (clearCache)
        {
            _centerSampleCache.Clear();
            _persistentCenterCacheInvalidated = 0;
        }
        else
        {
            InvalidateCenterSampleCacheForDirtyRegions(cellSize);
        }

        _centerCacheBuildCenter = center;
        _centerCacheBuildSize = size;
        _centerCacheBoundsPadding = boundsPadding;
        _centerCacheMaxDepth = maxDepth;
    }

    private void InvalidateCenterSampleCache(Bounds dirtyBounds)
    {
        if (_centerSampleCache.Count == 0)
            return;

        _centerCacheRemovalBuffer.Clear();
        foreach (KeyValuePair<QuantizedVector3Key, CenterCacheEntry> pair in _centerSampleCache)
        {
            if (dirtyBounds.Contains(pair.Value.Position))
                _centerCacheRemovalBuffer.Add(pair.Key);
        }

        for (int i = 0; i < _centerCacheRemovalBuffer.Count; i++)
            _centerSampleCache.Remove(_centerCacheRemovalBuffer[i]);

        _persistentCenterCacheInvalidated += _centerCacheRemovalBuffer.Count;
        _centerCacheRemovalBuffer.Clear();
    }

    private void InvalidateCenterSampleCacheForDirtyRegions(Vector3 cellSize)
    {
        Bounds expandedDirtyBounds = ExpandBoundsByCellPadding(
            _crossingCacheDirtyBounds,
            cellSize,
            sampleCacheDirtyPaddingCells);
        InvalidateCenterSampleCache(expandedDirtyBounds);
    }

    private void ClearCornerSampleCache()
    {
        if (_cornerSampleStates != null)
            System.Array.Clear(_cornerSampleStates, 0, _cornerSampleStates.Length);

        _cornerSampleCacheFallback.Clear();
        _cornerCacheSize = 0;
        _persistentCornerCacheInvalidated = 0;
    }

    private void InvalidateCornerSampleCache(Bounds dirtyBounds, Vector3 origin, Vector3 cellSize)
    {
        GetDirtyGridRange(dirtyBounds, origin, cellSize, out Vector3Int dirtyMin, out Vector3Int dirtyMax);

        if (_cornerSampleStates != null)
        {
            int minX = Mathf.Clamp(dirtyMin.x, 0, _cornerSampleGridSide - 1);
            int minY = Mathf.Clamp(dirtyMin.y, 0, _cornerSampleGridSide - 1);
            int minZ = Mathf.Clamp(dirtyMin.z, 0, _cornerSampleGridSide - 1);
            int maxX = Mathf.Clamp(dirtyMax.x, 0, _cornerSampleGridSide - 1);
            int maxY = Mathf.Clamp(dirtyMax.y, 0, _cornerSampleGridSide - 1);
            int maxZ = Mathf.Clamp(dirtyMax.z, 0, _cornerSampleGridSide - 1);

            if (minX <= maxX && minY <= maxY && minZ <= maxZ)
            {
                for (int z = minZ; z <= maxZ; z++)
                {
                    for (int y = minY; y <= maxY; y++)
                    {
                        int rowStart = _cornerSampleGridSide * (y + _cornerSampleGridSide * z);
                        for (int x = minX; x <= maxX; x++)
                        {
                            Vector3Int coord = new Vector3Int(x, y, z);
                            int index = x + rowStart;
                            if (_cornerSampleStates[index] != 0 &&
                                CornerCoordOverlapsBounds(coord, dirtyBounds, origin, cellSize))
                            {
                                _cornerSampleStates[index] = 0;
                                _cornerCacheSize--;
                                _persistentCornerCacheInvalidated++;
                            }
                        }
                    }
                }
            }
        }

        if (_cornerSampleCacheFallback.Count == 0)
            return;

        _cornerCacheRemovalBuffer.Clear();
        foreach (KeyValuePair<Vector3Int, float> pair in _cornerSampleCacheFallback)
        {
            if (CornerCoordOverlapsGridRange(pair.Key, dirtyMin, dirtyMax) &&
                CornerCoordOverlapsBounds(pair.Key, dirtyBounds, origin, cellSize))
                _cornerCacheRemovalBuffer.Add(pair.Key);
        }

        for (int i = 0; i < _cornerCacheRemovalBuffer.Count; i++)
            _cornerSampleCacheFallback.Remove(_cornerCacheRemovalBuffer[i]);

        _cornerCacheSize -= _cornerCacheRemovalBuffer.Count;
        _persistentCornerCacheInvalidated += _cornerCacheRemovalBuffer.Count;
        _cornerCacheRemovalBuffer.Clear();
    }

    private void InvalidateCornerSampleCacheForDirtyRegions(Vector3 origin, Vector3 cellSize)
    {
        Bounds expandedDirtyBounds = ExpandBoundsByCellPadding(
            _crossingCacheDirtyBounds,
            cellSize,
            sampleCacheDirtyPaddingCells);
        InvalidateCornerSampleCache(expandedDirtyBounds, origin, cellSize);
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
            if (_cornerSampleStates[index] == 0)
                _cornerCacheSize++;
            _cornerSampleStates[index] = 1;
            return;
        }

        if (!_cornerSampleCacheFallback.ContainsKey(gridCoord))
            _cornerCacheSize++;
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

    private void PrepareCrossingCacheForBuild(Vector3 origin, Vector3 cellSize)
    {
        bool clearCache =
            !_hasPreparedCrossingCache ||
            !_hasCrossingCacheDirtyBounds ||
            _crossingCacheBuildCenter != center ||
            _crossingCacheBuildSize != size ||
            !Mathf.Approximately(_crossingCacheBoundsPadding, boundsPadding) ||
            _crossingCacheMaxDepth != maxDepth ||
            _crossingCacheRefinementSteps != edgeRefinementSteps;

        if (clearCache)
        {
            _averageCrossingCache.Clear();
        }
        else
        {
            InvalidateCrossingCacheForDirtyRegions(origin, cellSize);
        }

        _crossingCacheBuildCenter = center;
        _crossingCacheBuildSize = size;
        _crossingCacheBoundsPadding = boundsPadding;
        _crossingCacheMaxDepth = maxDepth;
        _crossingCacheRefinementSteps = edgeRefinementSteps;
        _crossingCacheGridVertexSide = (1 << maxDepth) + 1;
        _hasPreparedCrossingCache = false;
    }

    private void InvalidateCrossingCache(Bounds dirtyBounds)
    {
        if (_averageCrossingCache.Count == 0)
            return;

        _crossingCacheRemovalBuffer.Clear();
        foreach (KeyValuePair<ulong, CrossingCacheEntry> pair in _averageCrossingCache)
        {
            if (CrossingPointOverlapsBounds(pair.Value.Crossing, dirtyBounds))
                _crossingCacheRemovalBuffer.Add(pair.Key);
        }

        for (int i = 0; i < _crossingCacheRemovalBuffer.Count; i++)
            _averageCrossingCache.Remove(_crossingCacheRemovalBuffer[i]);

        _persistentCrossingCacheInvalidated += _crossingCacheRemovalBuffer.Count;
        _crossingCacheRemovalBuffer.Clear();
    }

    private void InvalidateCrossingCacheForDirtyRegions(Vector3 origin, Vector3 cellSize)
    {
        Bounds expandedDirtyBounds = _crossingCacheDirtyBounds;
        float epsilonPadding = Bounds.size.magnitude * 1e-5f;
        expandedDirtyBounds.Expand(epsilonPadding);
        InvalidateCrossingCache(expandedDirtyBounds);
    }

    public static ulong PackGridEdgeKey(Vector3Int a, Vector3Int b, int gridVertexSide)
    {
        int axis = a.x != b.x ? 0 : a.y != b.y ? 1 : 2;
        int x = a.x < b.x ? a.x : b.x;
        int y = a.y < b.y ? a.y : b.y;
        int z = a.z < b.z ? a.z : b.z;
        ulong side = (ulong)Mathf.Max(1, gridVertexSide);

        ulong key = (ulong)axis;
        key = key * side + (uint)z;
        key = key * side + (uint)y;
        key = key * side + (uint)x;
        return key;
    }

    public static bool CrossingEdgeOverlapsBounds(
        OctreeHermiteEdgeKey edge,
        Bounds dirtyBounds,
        Vector3 origin,
        Vector3 cellSize)
    {
        Vector3 a = GridCoordToPosition(edge.A, origin, cellSize);
        Vector3 b = GridCoordToPosition(edge.B, origin, cellSize);

        float minX = Mathf.Min(a.x, b.x);
        float minY = Mathf.Min(a.y, b.y);
        float minZ = Mathf.Min(a.z, b.z);
        float maxX = Mathf.Max(a.x, b.x);
        float maxY = Mathf.Max(a.y, b.y);
        float maxZ = Mathf.Max(a.z, b.z);
        Vector3 dirtyMin = dirtyBounds.min;
        Vector3 dirtyMax = dirtyBounds.max;

        return minX <= dirtyMax.x && maxX >= dirtyMin.x &&
               minY <= dirtyMax.y && maxY >= dirtyMin.y &&
               minZ <= dirtyMax.z && maxZ >= dirtyMin.z;
    }

    public static bool CrossingPointOverlapsBounds(Vector3 crossing, Bounds dirtyBounds)
    {
        Vector3 dirtyMin = dirtyBounds.min;
        Vector3 dirtyMax = dirtyBounds.max;
        return crossing.x >= dirtyMin.x && crossing.x <= dirtyMax.x &&
               crossing.y >= dirtyMin.y && crossing.y <= dirtyMax.y &&
               crossing.z >= dirtyMin.z && crossing.z <= dirtyMax.z;
    }

    private static Vector3 GridCoordToPosition(Vector3Int coord, Vector3 origin, Vector3 cellSize)
    {
        return new Vector3(
            origin.x + coord.x * cellSize.x,
            origin.y + coord.y * cellSize.y,
            origin.z + coord.z * cellSize.z);
    }

    public static bool CornerCoordOverlapsBounds(
        Vector3Int coord,
        Bounds dirtyBounds,
        Vector3 origin,
        Vector3 cellSize)
    {
        Vector3 position = GridCoordToPosition(coord, origin, cellSize);
        Vector3 dirtyMin = dirtyBounds.min;
        Vector3 dirtyMax = dirtyBounds.max;

        return position.x >= dirtyMin.x && position.x <= dirtyMax.x &&
               position.y >= dirtyMin.y && position.y <= dirtyMax.y &&
               position.z >= dirtyMin.z && position.z <= dirtyMax.z;
    }

    public static Bounds ExpandBoundsByCellPadding(Bounds bounds, Vector3 cellSize, float paddingCells)
    {
        float padding = Mathf.Max(0f, paddingCells) * MaxAbsComponent(cellSize);
        if (padding <= 0f)
            return bounds;

        bounds.Expand(padding * 2f);
        return bounds;
    }

    private static float MaxAbsComponent(Vector3 value)
    {
        return Mathf.Max(Mathf.Abs(value.x), Mathf.Abs(value.y), Mathf.Abs(value.z));
    }

    private static bool CornerCoordOverlapsGridRange(Vector3Int coord, Vector3Int dirtyMin, Vector3Int dirtyMax)
    {
        return coord.x >= dirtyMin.x && coord.x <= dirtyMax.x &&
               coord.y >= dirtyMin.y && coord.y <= dirtyMax.y &&
               coord.z >= dirtyMin.z && coord.z <= dirtyMax.z;
    }

    private static void GetDirtyGridRange(
        Bounds dirtyBounds,
        Vector3 origin,
        Vector3 cellSize,
        out Vector3Int dirtyMin,
        out Vector3Int dirtyMax)
    {
        Vector3 min = dirtyBounds.min;
        Vector3 max = dirtyBounds.max;
        dirtyMin = new Vector3Int(
            Mathf.FloorToInt((min.x - origin.x) / cellSize.x),
            Mathf.FloorToInt((min.y - origin.y) / cellSize.y),
            Mathf.FloorToInt((min.z - origin.z) / cellSize.z));
        dirtyMax = new Vector3Int(
            Mathf.CeilToInt((max.x - origin.x) / cellSize.x),
            Mathf.CeilToInt((max.y - origin.y) / cellSize.y),
            Mathf.CeilToInt((max.z - origin.z) / cellSize.z));
    }

    private void ResetState()
    {
        _nodes.Clear();
        _sourceEvaluations = 0;
        _cornerCacheHits = 0;
        _cornerCacheMisses = 0;
        _persistentCornerCacheInvalidated = 0;
        _centerEvaluations = 0;
        _centerCacheHits = 0;
        _centerCacheMisses = 0;
        _persistentCenterCacheInvalidated = 0;
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
        _reusedNodeCount = 0;
        _reusedSubtreeCount = 0;
        _recursiveCornerSampleTicks = 0;
        _recursiveCenterDecisionTicks = 0;
        _recursiveChildCornerTicks = 0;
        _recursiveNodeRecordTicks = 0;
        _recursiveNodeReusePreparationTicks = 0;
        _recursiveCornerCachePreparationTicks = 0;
        _recursiveCenterCachePreparationTicks = 0;
        _recursiveCrossingCachePreparationTicks = 0;
        _recursiveSubtreeCopyTicks = 0;
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
        double recursiveCornerSampleMs = profileRecursiveParts ? _recursiveCornerSampleTicks * 1000d / Stopwatch.Frequency : 0d;
        double recursiveCenterDecisionMs = profileRecursiveParts ? _recursiveCenterDecisionTicks * 1000d / Stopwatch.Frequency : 0d;
        double recursiveChildCornerMs = profileRecursiveParts ? _recursiveChildCornerTicks * 1000d / Stopwatch.Frequency : 0d;
        double recursiveNodeRecordMs = profileRecursiveParts ? _recursiveNodeRecordTicks * 1000d / Stopwatch.Frequency : 0d;
        double recursiveNodeReusePreparationMs = profileRecursiveParts ? _recursiveNodeReusePreparationTicks * 1000d / Stopwatch.Frequency : 0d;
        double recursiveCornerCachePreparationMs = profileRecursiveParts ? _recursiveCornerCachePreparationTicks * 1000d / Stopwatch.Frequency : 0d;
        double recursiveCenterCachePreparationMs = profileRecursiveParts ? _recursiveCenterCachePreparationTicks * 1000d / Stopwatch.Frequency : 0d;
        double recursiveCrossingCachePreparationMs = profileRecursiveParts ? _recursiveCrossingCachePreparationTicks * 1000d / Stopwatch.Frequency : 0d;
        double recursiveSubtreeCopyMs = profileRecursiveParts ? _recursiveSubtreeCopyTicks * 1000d / Stopwatch.Frequency : 0d;
        double recursiveOtherMs = 0d;
        if (profileRecursiveParts)
        {
            double recursiveMeasuredMs =
                (_recursiveCornerSampleTicks +
                 _recursiveCenterDecisionTicks +
                 _recursiveChildCornerTicks +
                 _recursiveNodeRecordTicks +
                 _recursiveNodeReusePreparationTicks +
                 _recursiveCornerCachePreparationTicks +
                 _recursiveCenterCachePreparationTicks +
                 _recursiveCrossingCachePreparationTicks +
                 _recursiveSubtreeCopyTicks +
                 surfaceVertexExclusiveTicks +
                 _surfaceCrossingTicks +
                 _surfaceNormalTicks) * 1000d / Stopwatch.Frequency;
            recursiveOtherMs = System.Math.Max(0d, recursiveBuildMs - recursiveMeasuredMs);
        }

        LastBuildStats = new BuildStats(
            totalMs,
            recursiveBuildMs,
            recursiveCornerSampleMs,
            recursiveCenterDecisionMs,
            recursiveChildCornerMs,
            recursiveNodeRecordMs,
            recursiveNodeReusePreparationMs,
            recursiveCornerCachePreparationMs,
            recursiveCenterCachePreparationMs,
            recursiveCrossingCachePreparationMs,
            recursiveSubtreeCopyMs,
            recursiveOtherMs,
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
            _persistentCornerCacheInvalidated,
            _cornerCacheSize,
            _centerEvaluations,
            _centerCacheHits,
            _centerCacheMisses,
            _persistentCenterCacheInvalidated,
            _centerSampleCache.Count,
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
            _reusedNodeCount,
            _reusedSubtreeCount,
            gcGen0Delta,
            gcGen1Delta,
            gcGen2Delta
        );
    }

    private string FormatRecursivePartsLog(BuildStats stats)
    {
        if (!profileRecursiveParts)
            return "recursiveParts=disabled";

        return $"recursiveParts(nodePrep={stats.recursiveNodeReusePreparationMs:F2} ms, cornerCachePrep={stats.recursiveCornerCachePreparationMs:F2} ms, centerCachePrep={stats.recursiveCenterCachePreparationMs:F2} ms, crossingCachePrep={stats.recursiveCrossingCachePreparationMs:F2} ms, subtreeCopy={stats.recursiveSubtreeCopyMs:F2} ms, corner={stats.recursiveCornerSampleMs:F2} ms, center={stats.recursiveCenterDecisionMs:F2} ms, childCorner={stats.recursiveChildCornerMs:F2} ms, nodeRecord={stats.recursiveNodeRecordMs:F2} ms, other={stats.recursiveOtherMs:F2} ms)";
    }
}
