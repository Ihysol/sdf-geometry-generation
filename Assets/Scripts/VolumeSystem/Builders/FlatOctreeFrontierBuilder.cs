using System.Collections.Generic;
using System.Diagnostics;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;

internal static class FlatOctreeFrontierBuilder
{
    static int s_batchCount;
    static int s_sampleCount;
    static int s_jobSampleCount;
    static int s_serialSampleCount;
    static double s_preparationMs;
    static double s_evaluationMs;
    static double s_insertionMs;
    static double s_traversalMs;
    static double s_reuseCheckMs;
    static double s_collectCornersMs;
    static double s_collectCentersMs;
    static double s_subdivideDecisionMs;
    static double s_enqueueChildrenMs;
    static double s_nodeRecordMs;
    static double s_buildReplayMs;

    public static int FrontierBatchCount => s_batchCount;
    public static int FrontierSampleCount => s_sampleCount;
    public static int FrontierJobSampleCount => s_jobSampleCount;
    public static int FrontierSerialSampleCount => s_serialSampleCount;
    public static double FrontierPreparationMs => s_preparationMs;
    public static double FrontierEvaluationMs => s_evaluationMs;
    public static double FrontierInsertionMs => s_insertionMs;
    public static double FrontierTraversalMs => s_traversalMs;
    public static double FrontierReuseCheckMs => s_reuseCheckMs;
    public static double FrontierCollectCornersMs => s_collectCornersMs;
    public static double FrontierCollectCentersMs => s_collectCentersMs;
    public static double FrontierSubdivideDecisionMs => s_subdivideDecisionMs;
    public static double FrontierEnqueueChildrenMs => s_enqueueChildrenMs;
    public static double FrontierNodeRecordMs => s_nodeRecordMs;
    public static double FrontierBuildReplayMs => s_buildReplayMs;

    struct PendingNode
    {
        public Vector3 Center;
        public Vector3 Size;
        public int Depth;
        public int PreviousNodeIndex;
        public int ParentNodeIndex;
        public int ParentOctant;
        public int NodeIndex;
        public bool Reused;
    }

    readonly struct SubdivisionDecision
    {
        public readonly bool ShouldSubdivide;
        public readonly bool CornerContainsSurface;

        public SubdivisionDecision(bool shouldSubdivide, bool cornerContainsSurface)
        {
            ShouldSubdivide = shouldSubdivide;
            CornerContainsSurface = cornerContainsSurface;
        }
    }

    public static bool Build(
        FlatOctreeVolumeBuilder builder,
        IScalarFieldSource source,
        BurstSdfSceneSnapshot burst,
        Vector3 origin,
        Vector3 cellSize)
    {
        ResetMetrics();

        try
        {
            long prepStart = Stopwatch.GetTimestamp();

            Bounds buildBounds = builder.Bounds;
            Vector3 rootCenter = buildBounds.center;
            Vector3 rootSize = buildBounds.size;
            int maxDepth = builder.maxDepth;

            List<PendingNode> currentLevel = new(256)
            {
                new PendingNode
                {
                    Center = rootCenter,
                    Size = rootSize,
                    Depth = 0,
                    PreviousNodeIndex = 0,
                    ParentNodeIndex = -1,
                    ParentOctant = -1,
                    NodeIndex = -1,
                    Reused = false
                }
            };

            List<PendingNode> nextLevel = new(1024);

            var unsampledPositions = new NativeList<float3>(Allocator.TempJob);
            var cornerCoords = new List<(Vector3Int coord, int writeIndex)>(256);
            var centerEntries = new List<(QuantizedVector3Key key, int writeIndex, Vector3 position)>(128);

            try
            {
                for (int depth = 0; depth <= maxDepth && currentLevel.Count > 0; depth++)
                {
                    cornerCoords.Clear();
                    centerEntries.Clear();
                    unsampledPositions.Clear();

                    long traversalStart = Stopwatch.GetTimestamp();
                    for (int i = 0; i < currentLevel.Count; i++)
                    {
                        PendingNode pending = currentLevel[i];

                        long reuseStart = Stopwatch.GetTimestamp();
                        if (builder.TryReusePreviousSubtree(pending.Center, pending.Size, pending.PreviousNodeIndex, out int reusedNodeIndex))
                        {
                            pending.NodeIndex = reusedNodeIndex;
                            pending.Reused = true;
                            currentLevel[i] = pending;
                            LinkToParent(builder, pending);
                            s_reuseCheckMs += ElapsedMs(reuseStart);
                            continue;
                        }
                        s_reuseCheckMs += ElapsedMs(reuseStart);

                        long collectCornersStart = Stopwatch.GetTimestamp();
                        CollectUnsampledCorners(builder, pending, origin, cellSize, unsampledPositions, cornerCoords);
                        s_collectCornersMs += ElapsedMs(collectCornersStart);

                        long collectCentersStart = Stopwatch.GetTimestamp();
                        CollectUnsampledCenter(builder, pending, origin, cellSize, unsampledPositions, centerEntries);
                        s_collectCentersMs += ElapsedMs(collectCentersStart);

                        currentLevel[i] = pending;
                    }
                    s_traversalMs += ElapsedMs(traversalStart);

                    if (unsampledPositions.Length > 0)
                    {
                        BatchEvaluate(builder, burst, unsampledPositions, cornerCoords, centerEntries);
                    }

                    for (int i = 0; i < currentLevel.Count; i++)
                    {
                        PendingNode pending = currentLevel[i];
                        if (pending.Reused)
                            continue;

                        long nodeRecordStart = Stopwatch.GetTimestamp();
                        Vector3 halfSize = pending.Size * 0.5f;
                        Vector3 min = pending.Center - halfSize;
                        Vector3 max = pending.Center + halfSize;
                        FlatOctreeVolumeBuilder.CornerSamples corners = builder.SampleCorners(source, min, max, origin, cellSize);

                        long decisionStart = Stopwatch.GetTimestamp();
                        SubdivisionDecision decision = DecideSubdivision(builder, source, pending, corners, origin, cellSize);
                        s_subdivideDecisionMs += ElapsedMs(decisionStart);

                        int nodeIndex = builder._nodes.Count;
                        FlatOctreeVolumeBuilder.NodeRecord record = new()
                        {
                            Center = pending.Center,
                            Size = pending.Size,
                            Coord = builder.GetCoord(pending.Center, origin, cellSize),
                            SizeInCells = builder.GetSizeInCells(pending.Size, cellSize),
                            FirstChildIndex = -1,
                            ChildMask = 0,
                            Corners = corners
                        };

                        bool leaf = !decision.ShouldSubdivide || pending.Depth >= maxDepth;
                        if (leaf)
                        {
                            record.Flags = FlatOctreeLayout.FlagLeaf;
                            if (pending.Depth >= maxDepth && decision.CornerContainsSurface)
                            {
                                record.Flags |= FlatOctreeLayout.FlagSurface;
                                long surfaceVertexStart = Stopwatch.GetTimestamp();
                                builder.EstimateSurfaceVertexAndNormal(source, pending.Center, pending.Size, min, max, corners, origin, cellSize, out record.SurfaceVertex, out record.SurfaceNormal);
                                builder._surfaceVertexTicks += Stopwatch.GetTimestamp() - surfaceVertexStart;
                                builder._surfaceLeaves++;
                            }
                        }

                        builder._nodes.Add(record);
                        pending.NodeIndex = nodeIndex;
                        currentLevel[i] = pending;
                        LinkToParent(builder, pending);
                        s_nodeRecordMs += ElapsedMs(nodeRecordStart);

                        if (!leaf)
                        {
                            long enqueueStart = Stopwatch.GetTimestamp();
                            EnqueueChildren(builder, pending, nextLevel);
                            s_enqueueChildrenMs += ElapsedMs(enqueueStart);
                        }
                    }

                    var swap = currentLevel;
                    currentLevel = nextLevel;
                    nextLevel = swap;
                    nextLevel.Clear();
                }

                s_preparationMs = System.Math.Max(0d, ElapsedMs(prepStart) - s_evaluationMs - s_insertionMs);
                s_buildReplayMs = 0d;
                return builder._nodes.Count > 0;
            }
            finally
            {
                if (unsampledPositions.IsCreated)
                    unsampledPositions.Dispose();
            }
        }
        catch
        {
            builder._nodes.Clear();
            return false;
        }
    }

    static void ResetMetrics()
    {
        s_batchCount = 0;
        s_sampleCount = 0;
        s_jobSampleCount = 0;
        s_serialSampleCount = 0;
        s_preparationMs = 0d;
        s_evaluationMs = 0d;
        s_insertionMs = 0d;
        s_traversalMs = 0d;
        s_reuseCheckMs = 0d;
        s_collectCornersMs = 0d;
        s_collectCentersMs = 0d;
        s_subdivideDecisionMs = 0d;
        s_enqueueChildrenMs = 0d;
        s_nodeRecordMs = 0d;
        s_buildReplayMs = 0d;
    }

    static double ElapsedMs(long startTicks)
    {
        return (Stopwatch.GetTimestamp() - startTicks) * 1000d / Stopwatch.Frequency;
    }

    static void LinkToParent(FlatOctreeVolumeBuilder builder, PendingNode pending)
    {
        if (pending.ParentNodeIndex < 0 || pending.ParentOctant < 0)
            return;

        FlatOctreeVolumeBuilder.NodeRecord parent = builder._nodes[pending.ParentNodeIndex];
        if (parent.FirstChildIndex < 0)
            parent.FirstChildIndex = pending.NodeIndex;
        parent.ChildMask |= (byte)(1 << pending.ParentOctant);
        builder._nodes[pending.ParentNodeIndex] = parent;
    }

    static void CollectUnsampledCorners(
        FlatOctreeVolumeBuilder builder,
        PendingNode pending,
        Vector3 origin,
        Vector3 cellSize,
        NativeList<float3> positions,
        List<(Vector3Int coord, int writeIndex)> cornerCoords)
    {
        Vector3 halfSize = pending.Size * 0.5f;
        Vector3 min = pending.Center - halfSize;
        Vector3 max = pending.Center + halfSize;

        Vector3Int minCoord = FlatOctreeVolumeBuilder.WorldToGridVertex(min.x, min.y, min.z, origin, cellSize);
        Vector3Int maxCoord = FlatOctreeVolumeBuilder.WorldToGridVertex(max.x, max.y, max.z, origin, cellSize);

        for (int c = 0; c < 8; c++)
        {
            Vector3Int coord = FlatOctreeVolumeBuilder.GetCornerGridCoord(c, minCoord, maxCoord);
            if (!builder.TryGetCornerSample(coord, out _))
            {
                Vector3 pos = FlatOctreeVolumeBuilder.GetCornerPosition(c, min, max);
                cornerCoords.Add((coord, positions.Length));
                positions.Add(new float3(pos.x, pos.y, pos.z));
            }
        }
    }

    static void CollectUnsampledCenter(
        FlatOctreeVolumeBuilder builder,
        PendingNode pending,
        Vector3 origin,
        Vector3 cellSize,
        NativeList<float3> positions,
        List<(QuantizedVector3Key key, int writeIndex, Vector3 position)> centerEntries)
    {
        int depth = pending.Depth;
        bool canSubdivide = depth < builder.maxDepth;
        bool forcedSubdivide = depth < builder.minDepth;

        if (forcedSubdivide || !canSubdivide)
            return;

        QuantizedVector3Key centerKey = QuantizedVector3Key.FromPosition(pending.Center, origin, builder._centerCacheQuantum);
        if (builder._centerSampleCache.ContainsKey(centerKey))
            return;

        if (builder.TryGetGridVertex(pending.Center, origin, cellSize, out Vector3Int gridCoord) &&
            builder.TryGetCornerSample(gridCoord, out _))
            return;

        centerEntries.Add((centerKey, positions.Length, pending.Center));
        positions.Add(new float3(pending.Center.x, pending.Center.y, pending.Center.z));
    }

    static void BatchEvaluate(
        FlatOctreeVolumeBuilder builder,
        BurstSdfSceneSnapshot burst,
        NativeList<float3> positions,
        List<(Vector3Int coord, int writeIndex)> cornerCoords,
        List<(QuantizedVector3Key key, int writeIndex, Vector3 position)> centerEntries)
    {
        int count = positions.Length;

        var values = new NativeArray<float>(count, Allocator.TempJob);
        try
        {
            long evalStart = Stopwatch.GetTimestamp();
            NativeArray<float3> posArray = positions.AsArray();
            BurstSdfBatchResult result = burst.EvaluateBatch(posArray, values, FlatOctreeVolumeBuilder.MinBurstBatchSize);
            s_evaluationMs += ElapsedMs(evalStart);

            if (result.UsedJob)
                s_jobSampleCount += count;
            else
                s_serialSampleCount += count;

            long insertStart = Stopwatch.GetTimestamp();
            for (int i = 0; i < cornerCoords.Count; i++)
            {
                var (coord, writeIndex) = cornerCoords[i];
                builder.StoreCornerSample(coord, values[writeIndex]);
            }

            for (int i = 0; i < centerEntries.Count; i++)
            {
                var (key, writeIndex, position) = centerEntries[i];
                builder._centerSampleCache[key] = new FlatOctreeVolumeBuilder.CenterCacheEntry(position, values[writeIndex]);
            }
            s_insertionMs += ElapsedMs(insertStart);
            s_sampleCount += count;
            s_batchCount++;
        }
        finally
        {
            if (values.IsCreated)
                values.Dispose();
        }
    }

    static SubdivisionDecision DecideSubdivision(
        FlatOctreeVolumeBuilder builder,
        IScalarFieldSource source,
        PendingNode pending,
        FlatOctreeVolumeBuilder.CornerSamples corners,
        Vector3 origin,
        Vector3 cellSize)
    {
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
        int depth = pending.Depth;
        bool canSubdivide = depth < builder.maxDepth;
        bool forcedSubdivide = depth < builder.minDepth || cornerContainsSurface;
        bool needsCenterDecision = canSubdivide && !forcedSubdivide;
        bool centerDiffersFromCorners = false;
        bool couldContainSurface = false;

        if (needsCenterDecision)
        {
            float centerValue = builder.EvaluateCenter(source, pending.Center, origin, cellSize);
            centerDiffersFromCorners =
                (centerValue < 0f && cornerHasPositive) ||
                (centerValue >= 0f && cornerHasNegative);
            couldContainSurface = Mathf.Abs(centerValue) <= (pending.Size * 0.5f).magnitude;
        }

        bool minDepthReason = depth < builder.minDepth;
        bool cornerCrossingReason = cornerContainsSurface;
        bool centerMismatchReason = centerDiffersFromCorners;
        bool distanceThresholdReason = couldContainSurface;
        int subdivisionReasons = 0;

        if (minDepthReason)
        {
            builder._subdivisionMinDepth++;
            subdivisionReasons |= 1;
        }
        if (cornerCrossingReason)
        {
            builder._subdivisionCornerCrossing++;
            subdivisionReasons |= 2;
        }
        if (centerMismatchReason)
        {
            builder._subdivisionCenterMismatch++;
            subdivisionReasons |= 4;
        }
        if (distanceThresholdReason)
        {
            builder._subdivisionDistanceThreshold++;
            subdivisionReasons |= 8;
        }

        builder.CountExclusiveSubdivisionReason(subdivisionReasons);
        return new SubdivisionDecision(subdivisionReasons != 0, cornerContainsSurface);
    }

    static void EnqueueChildren(
        FlatOctreeVolumeBuilder builder,
        PendingNode pending,
        List<PendingNode> nextLevel)
    {
        Vector3 halfSize = pending.Size * 0.5f;
        Vector3 min = pending.Center - halfSize;
        Vector3 childSize = pending.Size * 0.5f;

        int previousChildCursor = builder.GetPreviousFirstChildIndex(pending.PreviousNodeIndex, pending.Center, pending.Size);
        byte previousChildMask = previousChildCursor >= 0
            ? builder._previousNodes[pending.PreviousNodeIndex].ChildMask
            : (byte)0;

        for (int x = 0; x < 2; x++)
        for (int y = 0; y < 2; y++)
        for (int z = 0; z < 2; z++)
        {
            int childOctant = (x << 2) | (y << 1) | z;
            Vector3 childCenter = min + new Vector3(
                (x + 0.5f) * childSize.x,
                (y + 0.5f) * childSize.y,
                (z + 0.5f) * childSize.z);

            int previousChildIndex = -1;
            if (previousChildCursor >= 0 && (previousChildMask & (1 << childOctant)) != 0)
            {
                previousChildIndex = previousChildCursor;
                previousChildCursor += builder.GetPreviousSubtreeSize(previousChildCursor);
            }

            nextLevel.Add(new PendingNode
            {
                Center = childCenter,
                Size = childSize,
                Depth = pending.Depth + 1,
                PreviousNodeIndex = previousChildIndex,
                ParentNodeIndex = pending.NodeIndex,
                ParentOctant = childOctant,
                NodeIndex = -1,
                Reused = false
            });
        }
    }
}
