#if UNITY_EDITOR
using UnityEditor;
using UnityEditorInternal;
#endif

using System;
using System.IO;
using UnityEngine;

public enum VolumeDataStructure
{
    VoxelGrid,
    Octree,
    SparseVoxelOctree
}

public enum QefVertexMode
{
    AverageCrossings,
    QefFeaturePreserving,
    QefAxisSnap
}

public enum OctreeMesherType
{
    DualContouring,
    DualMarchingCubes,
    DualMarchingTetrahedra,
    SurfaceNets
}

public enum QefFeatureClassWeightMode
{
    Off,
    SurfaceEdgeCorner
}

public enum VolumeStorageMode
{
    Tree,
    Flat
}

public enum VolumeRebuildMode
{
    PreviewAndOnChange,
    OnChange,
    EveryFrame,
    Manual
}

public enum VolumeBenchmarkType
{
    DirtyMove,
    DirtyMoveSweep,
    Rebuild
}

#if LEGACY
[ExecuteAlways]
[DisallowMultipleComponent]
[RequireComponent(typeof(VolumeSceneComposer))]
public class VolumeModelOld : VolumeModel
{
    private readonly System.Collections.Generic.List<Bounds> _chunkBoundsCache = new();
    private bool _hasDirtyBounds;
    private Bounds _dirtyBounds;
    private readonly System.Collections.Generic.List<Bounds> _dirtyBoundsParts = new();
    private bool _isPreviewRebuild;
    private bool _forceFullChunkRenderOnce;

    [Header("Rendering")]
    public bool enableChunking = true;
    public bool forceFullChunkRedraw = false;
    public bool uniformChunkResolution = true;
    public int maxChunksPerRebuild = 8;
    public bool octreeExpandDirtyNeighbors = true;
    public int octreeDirtyNeighborRings = 2;
    public float dirtyHaloMultiplier = 3f;
    public Material surfaceMaterial;
    public ChunkingSettings chunking = new ChunkingSettings
    {
        voxelChunkCount = new Vector3Int(4, 4, 4),
        octreeChunkCount = new Vector3Int(4, 4, 4),
        octreeTargetTrianglesPerChunk = 10000,
        octreeEstimatedTrianglesPerLeaf = 12,
        octreeMaxLeafNodesPerChunk = 1024
    };

    private Transform ObjectsRoot
    {
        get
        {
            Transform existing = transform.Find("Objects");

            if (existing != null)
                return existing;

            GameObject go = new GameObject("Objects");
            go.transform.SetParent(transform, false);

            return go.transform;
        }
    }


#if UNITY_EDITOR
    /// <summary>Keeps VolumeModel first in the component stack when added.</summary>
    private void Reset()
    {
        MoveToTop();
    }

    /// <summary>Validates nested sampler settings after inspector edits.</summary>
    private void OnValidate()
    {
        MoveToTop();

        voxelGridSampler?.builder?.Validate();
        maxChunksPerRebuild = Mathf.Max(1, maxChunksPerRebuild);
        octreeDirtyNeighborRings = Mathf.Max(0, octreeDirtyNeighborRings);
        dirtyHaloMultiplier = Mathf.Max(0f, dirtyHaloMultiplier);
        if (uniformChunkResolution)
        {
            int voxelUniform = Mathf.Max(1, voxelGridSampler.builder.gridSize.x);
            voxelGridSampler.builder.gridSize = new Vector3Int(voxelUniform, voxelUniform, voxelUniform);

            int octreeUniform = Mathf.Max(1, chunking.octreeChunkCount.x);
            chunking.octreeChunkCount = new Vector3Int(octreeUniform, octreeUniform, octreeUniform);

            int voxelChunkUniform = Mathf.Max(1, chunking.voxelChunkCount.x);
            chunking.voxelChunkCount = new Vector3Int(voxelChunkUniform, voxelChunkUniform, voxelChunkUniform);
        }
        moveReleaseDelaySeconds = Mathf.Max(0f, moveReleaseDelaySeconds);
        previewInteractionMaxDepth = Mathf.Max(1, previewInteractionMaxDepth);
        previewVoxelGridSize.x = Mathf.Max(2, previewVoxelGridSize.x);
        previewVoxelGridSize.y = Mathf.Max(2, previewVoxelGridSize.y);
        previewVoxelGridSize.z = Mathf.Max(2, previewVoxelGridSize.z);
        if (previewVoxelUniformResolution)
        {
            int previewUniform = Mathf.Max(2, previewVoxelGridSize.x);
            previewVoxelGridSize = new Vector3Int(previewUniform, previewUniform, previewUniform);
        }
        previewInteractionHoldSeconds = Mathf.Max(0f, previewInteractionHoldSeconds);
        qefBlendFactor = Mathf.Clamp01(qefBlendFactor);
        qefSnapEpsilon = Mathf.Max(0f, qefSnapEpsilon);
        qefMaxOffsetCells = Mathf.Max(0f, qefMaxOffsetCells);
        qefAxisSnapStrength = Mathf.Max(1f, qefAxisSnapStrength);
        qefHermiteSamplesPerEdge = Mathf.Max(1, qefHermiteSamplesPerEdge);
        edgeRefinementSteps = Mathf.Max(0, edgeRefinementSteps);
        previewEdgeRefinementSteps = Mathf.Max(0, previewEdgeRefinementSteps);
        qefRobustScale = Mathf.Max(0.1f, qefRobustScale);
        qefIrlsIterations = Mathf.Max(1, qefIrlsIterations);
        qefAnisotropicStrength = Mathf.Max(0f, qefAnisotropicStrength);
        qefSurfaceWeight = Mathf.Max(0f, qefSurfaceWeight);
        qefEdgeWeight = Mathf.Max(0f, qefEdgeWeight);
        qefCornerWeight = Mathf.Max(0f, qefCornerWeight);
        benchmarkRuns = Mathf.Max(2, benchmarkRuns);
        benchmarkLogArchiveLimit = Mathf.Max(1, benchmarkLogArchiveLimit);
        dirtyMoveBenchmarkStepDelayMs = Mathf.Max(0f, dirtyMoveBenchmarkStepDelayMs);
    }

    /// <summary>Moves this component above companion components in the inspector.</summary>
    private void MoveToTop()
    {
        while (ComponentUtility.MoveComponentUp(this)) { }
    }
#endif

    [Header("Pipeline")]
    public VolumeDataStructure dataStructure = VolumeDataStructure.Octree;
    public VolumeStorageMode storageMode = VolumeStorageMode.Tree;
    public OctreeMesherType octreeMesherType = OctreeMesherType.DualContouring;

    [Header("Samplers")]
    public VoxelGridSampler voxelGridSampler = new();
    public OctreeVolumeSampler octreeSampler = new();
    public SparseVoxelOctreeSampler sparseVoxelOctreeSampler = new();

    [Header("Meshing")]
    public float isoLevel = 0f;
    public bool useQefVertices = true;
    public QefVertexMode qefVertexMode = QefVertexMode.AverageCrossings;
    [Range(0f, 1f)]
    public float qefBlendFactor = 0.5f;
    [Min(0f)]
    public float qefSnapEpsilon = 0.015f;
    [Min(0f)]
    public float qefMaxOffsetCells = 0.75f;
    [Min(1f)]
    public float qefAxisSnapStrength = 2.5f;
    public bool qefEnableMultiHermite = false;
    [Min(1)]
    public int qefHermiteSamplesPerEdge = 3;
    [Min(0)]
    public int edgeRefinementSteps = 3;
    public QefSolver.RobustKernel qefRobustKernel = QefSolver.RobustKernel.Cauchy;
    [Min(0.1f)]
    public float qefRobustScale = 2.5f;
    [Min(1)]
    public int qefIrlsIterations = 3;
    public bool qefUseAnisotropicRegularization = false;
    [Min(0f)]
    public float qefAnisotropicStrength = 0.2f;
    public QefFeatureClassWeightMode qefFeatureWeightMode = QefFeatureClassWeightMode.Off;
    [Min(0f)]
    public float qefSurfaceWeight = 1f;
    [Min(0f)]
    public float qefEdgeWeight = 1.2f;
    [Min(0f)]
    public float qefCornerWeight = 1.4f;
    public bool recalculateNormals = true;
    public bool recalculateBounds = true;

    [Header("Rebuild")]
    public VolumeRebuildMode rebuildMode = VolumeRebuildMode.PreviewAndOnChange;
    public bool rebuildOnMoveRelease = true;
    public float moveReleaseDelaySeconds = 0.5f;
    public bool usePreviewDepthWhileInteracting = true;
    [Min(1)]
    public int previewInteractionMaxDepth = 5;
    public bool usePreviewResolutionWhileInteracting = true;
    public bool previewVoxelUniformResolution = true;
    public Vector3Int previewVoxelGridSize = new Vector3Int(24, 24, 24);
    public bool useFlatDualContouringPreview = true;
    public bool simplifyQefDuringPreview = true;
    [Min(0)]
    public int previewEdgeRefinementSteps = 3;
    [Min(0f)]
    public float previewInteractionHoldSeconds = 0.2f;

    [Header("Debug")]
    public bool drawChildGizmos = true;
    public bool drawChunkGizmosAlways = false;
    public bool renderOctreeDebugCubes = false;
    public bool logChunkRebuildStats = false;
    public bool logRebuildDuration = true;
    public bool writeBenchmarkLogsToFile = true;
    [Min(1)] public int benchmarkLogArchiveLimit = 20;
    public bool profileFlatRecursiveParts = false;

    [Header("Add Object")]
    public VolumeShapeType shapeToAdd = VolumeShapeType.Sphere;
    public VolumeOperationRole roleToAdd = VolumeOperationRole.Add;

#if UNITY_EDITOR
    private double _lastInteractiveEditTime = double.NegativeInfinity;
    private bool _finalizePreviewRebuildQueued;
    private bool _hasFinalizePreviewDirtyBounds;
    private Bounds _finalizePreviewDirtyBounds;
    private bool _suppressRebuildProfileLog;
    private bool _benchmarkLogFileReady;
    private string _benchmarkArchiveLogPath;
    public VolumeBenchmarkType benchmarkType = VolumeBenchmarkType.DirtyMove;
    [Min(2)]
    public int benchmarkRuns = 10;
    public VolumeObject dirtyMoveBenchmarkObject;
    public Vector3 dirtyMoveBenchmarkOffset = Vector3.back;
    public bool visualizeDirtyMoveBenchmark;
    [Min(0f)]
    public float dirtyMoveBenchmarkStepDelayMs = 0f;
    public bool restoreDirtyMoveBenchmarkObject = true;
    public RebuildProfileSample LastRebuildProfileSample { get; private set; }

    private bool _dirtyMoveBenchmarkActive;
    private VolumeObject _dirtyMoveBenchmarkTarget;
    private RebuildProfileSample[] _dirtyMoveBenchmarkSamples;
    private Vector3 _dirtyMoveBenchmarkOriginalPosition;
    private Vector3 _dirtyMoveBenchmarkCurrentPosition;
    private Vector3 _dirtyMoveBenchmarkA;
    private Vector3 _dirtyMoveBenchmarkB;
    private Vector3 _dirtyMoveBenchmarkActiveOffset;
    private int _dirtyMoveBenchmarkLogicalRuns;
    private int _dirtyMoveBenchmarkStep;
    private double _dirtyMoveBenchmarkNextStepTime;
    private bool _dirtyMoveBenchmarkPreviousSuppressLog;
    private VolumeRebuildMode _dirtyMoveBenchmarkPreviousRebuildMode;

    public readonly struct RebuildProfileSample
    {
        public readonly double totalMs;
        public readonly double compositionMs;
        public readonly double volumeBuildMs;
        public readonly double renderMs;
        public readonly double flatBuildMs;
        public readonly double flatRecursiveMs;
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
        public readonly double flatCreateLayoutMs;
        public readonly double flatRuntimeCacheMs;
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
        public readonly int edgeEvaluations;
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
        public readonly int reusedNodes;
        public readonly int reusedSubtrees;
        public readonly double rendererMs;
        public readonly double rendererChunkMs;
        public readonly double rendererChunkLocalBuildMs;
        public readonly double rendererChunkClusterBuildMs;
        public readonly double rendererChunkMeshBuildMs;
        public readonly double rendererChunkApplyMeshMs;
        public readonly int rendererChunkLocalBuilds;
        public readonly int rendererChunkClusters;
        public readonly int rendererChunkClusterBuilds;
        public readonly int rendererChunkClusterFallbacks;
        public readonly int rebuiltChunks;
        public readonly int queuedDirtyChunks;
        public readonly bool dirtySeen;
        public readonly bool canUseDirtyChunks;
        public readonly bool fullChunkRebuild;
        public readonly bool frontierUsed;
        public readonly int frontierBatchCount;
        public readonly int frontierSampleCount;
        public readonly int frontierJobSampleCount;
        public readonly int frontierSerialSampleCount;
        public readonly double frontierPreparationMs;
        public readonly double frontierEvaluationMs;
        public readonly double frontierInsertionMs;
        public readonly double frontierTraversalMs;
        public readonly double frontierReuseCheckMs;
        public readonly double frontierCollectCornersMs;
        public readonly double frontierCollectCentersMs;
        public readonly double frontierSubdivideDecisionMs;
        public readonly double frontierEnqueueChildrenMs;
        public readonly double frontierNodeRecordMs;
        public readonly double frontierBuildReplayMs;

        public RebuildProfileSample(
            double totalMs,
            double compositionMs,
            double volumeBuildMs,
            double renderMs,
            double flatBuildMs,
            double flatRecursiveMs,
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
            double flatCreateLayoutMs,
            double flatRuntimeCacheMs,
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
            int edgeEvaluations,
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
            int reusedNodes,
            int reusedSubtrees,
            double rendererMs,
            double rendererChunkMs,
            double rendererChunkLocalBuildMs,
            double rendererChunkClusterBuildMs,
            double rendererChunkMeshBuildMs,
            double rendererChunkApplyMeshMs,
            int rendererChunkLocalBuilds,
            int rendererChunkClusters,
            int rendererChunkClusterBuilds,
            int rendererChunkClusterFallbacks,
            int rebuiltChunks,
            int queuedDirtyChunks,
            bool dirtySeen,
            bool canUseDirtyChunks,
            bool fullChunkRebuild,
            bool frontierUsed = false,
            int frontierBatchCount = 0,
            int frontierSampleCount = 0,
            int frontierJobSampleCount = 0,
            int frontierSerialSampleCount = 0,
            double frontierPreparationMs = 0d,
            double frontierEvaluationMs = 0d,
            double frontierInsertionMs = 0d,
            double frontierTraversalMs = 0d,
            double frontierReuseCheckMs = 0d,
            double frontierCollectCornersMs = 0d,
            double frontierCollectCentersMs = 0d,
            double frontierSubdivideDecisionMs = 0d,
            double frontierEnqueueChildrenMs = 0d,
            double frontierNodeRecordMs = 0d,
            double frontierBuildReplayMs = 0d)
        {
            this.totalMs = totalMs;
            this.compositionMs = compositionMs;
            this.volumeBuildMs = volumeBuildMs;
            this.renderMs = renderMs;
            this.flatBuildMs = flatBuildMs;
            this.flatRecursiveMs = flatRecursiveMs;
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
            this.flatCreateLayoutMs = flatCreateLayoutMs;
            this.flatRuntimeCacheMs = flatRuntimeCacheMs;
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
            this.edgeEvaluations = edgeEvaluations;
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
            this.reusedNodes = reusedNodes;
            this.reusedSubtrees = reusedSubtrees;
            this.rendererMs = rendererMs;
            this.rendererChunkMs = rendererChunkMs;
            this.rendererChunkLocalBuildMs = rendererChunkLocalBuildMs;
            this.rendererChunkClusterBuildMs = rendererChunkClusterBuildMs;
            this.rendererChunkMeshBuildMs = rendererChunkMeshBuildMs;
            this.rendererChunkApplyMeshMs = rendererChunkApplyMeshMs;
            this.rendererChunkLocalBuilds = rendererChunkLocalBuilds;
            this.rendererChunkClusters = rendererChunkClusters;
            this.rendererChunkClusterBuilds = rendererChunkClusterBuilds;
            this.rendererChunkClusterFallbacks = rendererChunkClusterFallbacks;
            this.rebuiltChunks = rebuiltChunks;
            this.queuedDirtyChunks = queuedDirtyChunks;
            this.dirtySeen = dirtySeen;
            this.canUseDirtyChunks = canUseDirtyChunks;
            this.fullChunkRebuild = fullChunkRebuild;
            this.frontierUsed = frontierUsed;
            this.frontierBatchCount = frontierBatchCount;
            this.frontierSampleCount = frontierSampleCount;
            this.frontierJobSampleCount = frontierJobSampleCount;
            this.frontierSerialSampleCount = frontierSerialSampleCount;
            this.frontierPreparationMs = frontierPreparationMs;
            this.frontierEvaluationMs = frontierEvaluationMs;
            this.frontierInsertionMs = frontierInsertionMs;
            this.frontierTraversalMs = frontierTraversalMs;
            this.frontierReuseCheckMs = frontierReuseCheckMs;
            this.frontierCollectCornersMs = frontierCollectCornersMs;
            this.frontierCollectCentersMs = frontierCollectCentersMs;
            this.frontierSubdivideDecisionMs = frontierSubdivideDecisionMs;
            this.frontierEnqueueChildrenMs = frontierEnqueueChildrenMs;
            this.frontierNodeRecordMs = frontierNodeRecordMs;
            this.frontierBuildReplayMs = frontierBuildReplayMs;
        }
    }
#endif

    /// <summary>Continuously rebuilds the model when realtime rebuild is enabled.</summary>
    private void Update()
    {
        if (ShouldRebuildEveryFrame())
            RebuildModel();
    }

    public bool ShouldAutoRebuildOnChange()
    {
        return rebuildMode == VolumeRebuildMode.PreviewAndOnChange ||
               rebuildMode == VolumeRebuildMode.OnChange;
    }

    public bool ShouldAutoRebuildOnTransformChange()
    {
        return ShouldAutoRebuildOnChange();
    }

    public bool ShouldUseInteractionPreview()
    {
        return rebuildMode == VolumeRebuildMode.PreviewAndOnChange;
    }

    public bool ShouldRebuildEveryFrame()
    {
        return rebuildMode == VolumeRebuildMode.EveryFrame;
    }

    /// <summary>Adds an object using the currently selected inspector defaults.</summary>
    public void AddSelectedObject()
    {
        AddObject(shapeToAdd, roleToAdd);
    }

    /// <summary>Creates a new child volume object and rebuilds the model.</summary>
    public void AddObject(VolumeShapeType shape, VolumeOperationRole role)
    {
        GameObject child = new GameObject($"VolumeObject_{shape}_{role}");
        child.transform.SetParent(ObjectsRoot, false);

        VolumeObject volumeObject = child.AddComponent<VolumeObject>();
        volumeObject.shapeType = shape;
        volumeObject.role = role;

        VolumeSceneComposer composer = GetComponent<VolumeSceneComposer>();

        if (!composer.objects.Contains(volumeObject))
            composer.objects.Add(volumeObject);

        RebuildModel();
    }

    /// <summary>Rebuilds composition, volume data, and render output.</summary>
    public void RebuildModel()
    {
        bool previousPreviewRebuild = _isPreviewRebuild;
        _isPreviewRebuild = false;

#if UNITY_EDITOR
        System.Diagnostics.Stopwatch rebuildStopwatch = null;
        System.Diagnostics.Stopwatch phaseStopwatch = null;
        double compositionMs = 0d;
        double volumeBuildMs = 0d;
        double renderMs = 0d;
        if (logRebuildDuration || _suppressRebuildProfileLog)
        {
            rebuildStopwatch = System.Diagnostics.Stopwatch.StartNew();
            phaseStopwatch = System.Diagnostics.Stopwatch.StartNew();
        }
#endif
        VolumeSceneComposer composer = GetComponent<VolumeSceneComposer>();

        if (composer == null)
        {
            _isPreviewRebuild = previousPreviewRebuild;
            return;
        }

        composer.RebuildComposition();
#if UNITY_EDITOR
        if (phaseStopwatch != null)
        {
            compositionMs = phaseStopwatch.Elapsed.TotalMilliseconds;
            phaseStopwatch.Restart();
        }
#endif

        IScalarFieldSource source = composer;
        bool didIncrementalVoxelUpdate = false;
        bool hasDirtyBounds = TryGetPendingDirtyBounds(out Bounds dirtyBounds);
        string rebuildCause = "unknown";
        bool usedIncrementalUpdate = false;
        bool builtFlatOctreeThisRebuild = false;

        switch (dataStructure)
        {
            case VolumeDataStructure.VoxelGrid:
                Vector3Int configuredVoxelGridSize = voxelGridSampler.builder.gridSize;
                bool usingPreviewVoxelResolution = ShouldUsePreviewVoxelResolution(configuredVoxelGridSize, out Vector3Int effectiveVoxelGridSize);
                _isPreviewRebuild = usingPreviewVoxelResolution;
                if (usingPreviewVoxelResolution)
                {
                    voxelGridSampler.builder.gridSize = effectiveVoxelGridSize;
                    rebuildCause = $"voxel-preview({effectiveVoxelGridSize.x}x{effectiveVoxelGridSize.y}x{effectiveVoxelGridSize.z}/{configuredVoxelGridSize.x}x{configuredVoxelGridSize.y}x{configuredVoxelGridSize.z})";
                }

                if (hasDirtyBounds)
                {
                    didIncrementalVoxelUpdate = voxelGridSampler.RebuildVolumeRegion(source, dirtyBounds, 3);
                    if (didIncrementalVoxelUpdate)
                    {
                        usedIncrementalUpdate = true;
                        if (!usingPreviewVoxelResolution)
                            rebuildCause = "voxel-incremental";
                    }
                }

                if (!didIncrementalVoxelUpdate)
                {
                    if (!usingPreviewVoxelResolution)
                        rebuildCause = hasDirtyBounds ? "voxel-incremental-failed" : "voxel-full-no-dirty";
                    voxelGridSampler.MarkDirty();
                    voxelGridSampler.RebuildVolume(source);
                    ClearDirtyBounds();
                }

                if (usingPreviewVoxelResolution)
                {
                    QueueFinalizePreviewRebuild();
                    voxelGridSampler.builder.gridSize = configuredVoxelGridSize;
                }
                break;

            case VolumeDataStructure.Octree:
            case VolumeDataStructure.SparseVoxelOctree:
                OctreeVolumeBuilder activeBuilder = dataStructure == VolumeDataStructure.Octree
                    ? octreeSampler.builder
                    : sparseVoxelOctreeSampler.builder.backend;
                int configuredMaxDepth = activeBuilder.maxDepth;
                bool usingPreviewDepth = ShouldUsePreviewDepth(configuredMaxDepth, out int effectiveMaxDepth);
                _isPreviewRebuild = usingPreviewDepth;
                activeBuilder.maxDepth = effectiveMaxDepth;
                activeBuilder.suppressBuildLog = true;
                activeBuilder.useQefVertices = GetEffectiveUseQefVertices();
                activeBuilder.qefVertexMode = GetEffectiveQefVertexMode();
                activeBuilder.qefBlendFactor = qefBlendFactor;
                activeBuilder.qefSnapEpsilon = qefSnapEpsilon;
                activeBuilder.qefMaxOffsetCells = qefMaxOffsetCells;
                activeBuilder.qefAxisSnapStrength = qefAxisSnapStrength;
                activeBuilder.qefEnableMultiHermite = GetEffectiveQefEnableMultiHermite();
                activeBuilder.qefHermiteSamplesPerEdge = qefHermiteSamplesPerEdge;
                activeBuilder.edgeRefinementSteps = GetEffectiveEdgeRefinementSteps();
                activeBuilder.qefRobustKernel = qefRobustKernel;
                activeBuilder.qefRobustScale = qefRobustScale;
                activeBuilder.qefIrlsIterations = qefIrlsIterations;
                activeBuilder.qefUseAnisotropicRegularization = qefUseAnisotropicRegularization;
                activeBuilder.qefAnisotropicStrength = qefAnisotropicStrength;
                activeBuilder.qefFeatureWeightMode = qefFeatureWeightMode;
                activeBuilder.qefSurfaceWeight = qefSurfaceWeight;
                activeBuilder.qefEdgeWeight = qefEdgeWeight;
                activeBuilder.qefCornerWeight = qefCornerWeight;
                bool canUseFlatBuilder = CanUseFlatOctreeBuilder();

                bool hasInitializedVolume = dataStructure == VolumeDataStructure.Octree
                    ? octreeSampler.Volume != null
                    : sparseVoxelOctreeSampler.Volume != null;
                bool canUseChunkLocalRendererBuild = hasDirtyBounds &&
                    hasInitializedVolume &&
                    enableChunking &&
                    RenderOutput != null &&
                    RenderOutput.CanBuildDirtyChunksLocally(this);

                if (!hasInitializedVolume)
                {
                    rebuildCause = dataStructure == VolumeDataStructure.Octree
                        ? "octree-full-init"
                        : "svo-full-init";
                }

                bool canAttemptIncrementalOctreeUpdate = hasDirtyBounds && !usingPreviewDepth && !canUseFlatBuilder;
                if (canAttemptIncrementalOctreeUpdate)
                {
                    bool didIncrementalOctreeUpdate = hasInitializedVolume && (dataStructure == VolumeDataStructure.Octree
                        ? octreeSampler.RebuildVolumeRegion(source, dirtyBounds)
                        : sparseVoxelOctreeSampler.RebuildVolumeRegion(source, dirtyBounds));

                    if (didIncrementalOctreeUpdate)
                    {
                        usedIncrementalUpdate = true;
                        rebuildCause = dataStructure == VolumeDataStructure.Octree
                            ? "octree-incremental"
                            : "svo-incremental";
                        if (usingPreviewDepth)
                        {
                            rebuildCause += $"-preview(d{effectiveMaxDepth}/{configuredMaxDepth})";
                            QueueFinalizePreviewRebuild();
                        }
                        activeBuilder.maxDepth = configuredMaxDepth;
                        _isPreviewRebuild = false;
                        activeBuilder.suppressBuildLog = true;
                        break;
                    }

                    if (hasInitializedVolume)
                    {
                        string reason = dataStructure == VolumeDataStructure.Octree
                            ? octreeSampler.LastIncrementalFallbackReason
                            : sparseVoxelOctreeSampler.LastIncrementalFallbackReason;
                        if (string.IsNullOrEmpty(reason))
                            reason = "unspecified";
                        rebuildCause = dataStructure == VolumeDataStructure.Octree
                            ? $"octree-full-fallback:{reason}"
                            : $"svo-full-fallback:{reason}";
#if UNITY_EDITOR
                        if (ShouldLogChunkRebuildStats() || ShouldLogRebuildDuration())
                            Debug.LogWarning($"Octree incremental rebuild failed ({reason}); falling back to full rebuild.");
#endif
                    }
                }
                else if (hasDirtyBounds && usingPreviewDepth)
                {
                    rebuildCause = dataStructure == VolumeDataStructure.Octree
                        ? $"octree-preview-full(d{effectiveMaxDepth}/{configuredMaxDepth})"
                        : $"svo-preview-full(d{effectiveMaxDepth}/{configuredMaxDepth})";
                }

                if (dataStructure == VolumeDataStructure.Octree)
                {
                    if (!hasDirtyBounds)
                        rebuildCause = "octree-full-no-dirty";
                    octreeSampler.MarkDirty();
                    if (canUseFlatBuilder)
                    {
                        if (canUseChunkLocalRendererBuild)
                        {
                            if (rebuildCause == "unknown")
                                rebuildCause = "octree-flat-dirty-chunk-local";
                        }
                        else
                        {
                            if (hasDirtyBounds && rebuildCause == "unknown")
                                rebuildCause = "octree-flat-dirty-full";
                            ConfigureFlatOctreeBuilder(activeBuilder);
                            octreeSampler.RebuildFlatOctreeVolume(source, hasDirtyBounds, dirtyBounds, _dirtyBoundsParts);
                            builtFlatOctreeThisRebuild = true;
                            rebuildCause += "-flat-builder";
                        }
                    }
                    else
                    {
                        octreeSampler.RebuildVolume(source);
                    }
                }
                else
                {
                    if (!hasDirtyBounds)
                        rebuildCause = "svo-full-no-dirty";
                    sparseVoxelOctreeSampler.MarkDirty();
                    sparseVoxelOctreeSampler.RebuildVolume(source);
                }
                if (usingPreviewDepth)
                {
                    if (!rebuildCause.Contains("-preview"))
                        rebuildCause += $"-preview(d{effectiveMaxDepth}/{configuredMaxDepth})";
                    QueueFinalizePreviewRebuild();
                }
                activeBuilder.maxDepth = configuredMaxDepth;
                activeBuilder.suppressBuildLog = true;
                break;
        }
#if UNITY_EDITOR
        if (phaseStopwatch != null)
        {
            volumeBuildMs = phaseStopwatch.Elapsed.TotalMilliseconds;
            phaseStopwatch.Restart();
        }
#endif

        if (_isPreviewRebuild && hasDirtyBounds)
            CaptureFinalizePreviewDirtyBounds(dirtyBounds);

        if (!enableChunking)
            ClearDirtyBounds();

        RenderOutput.Rebuild(this);
        ClearDirtyBounds();
#if UNITY_EDITOR
        if (phaseStopwatch != null)
            renderMs = phaseStopwatch.Elapsed.TotalMilliseconds;
#endif

#if UNITY_EDITOR
        if (rebuildStopwatch != null)
        {
            rebuildStopwatch.Stop();
            LastRebuildProfileSample = CreateRebuildProfileSample(
                rebuildStopwatch.Elapsed.TotalMilliseconds,
                compositionMs,
                volumeBuildMs,
                renderMs,
                builtFlatOctreeThisRebuild);
            if (ShouldLogRebuildDuration() && !_suppressRebuildProfileLog)
            {
                string rebuildProfileLog = BuildRebuildProfileLog(
                    rebuildStopwatch.Elapsed.TotalMilliseconds,
                    compositionMs,
                    volumeBuildMs,
                    renderMs,
                    rebuildCause,
                    hasDirtyBounds,
                    usedIncrementalUpdate,
                    builtFlatOctreeThisRebuild);
                Debug.Log(rebuildProfileLog);
                AppendBenchmarkLogSection("Manual Move / Rebuild Profile", rebuildProfileLog);
            }
        }
#endif
        _isPreviewRebuild = previousPreviewRebuild;
    }

#if UNITY_EDITOR
    private RebuildProfileSample CreateRebuildProfileSample(
        double totalMs,
        double compositionMs,
        double volumeBuildMs,
        double renderMs,
        bool includeFlatBuildStats)
    {
        VolumeMeshRenderer.RenderStats renderStats = RenderOutput.LastRenderStats;
        double flatBuildMs = 0d;
        double flatRecursiveMs = 0d;
        double recursiveCornerSampleMs = 0d;
        double recursiveCenterDecisionMs = 0d;
        double recursiveChildCornerMs = 0d;
        double recursiveNodeRecordMs = 0d;
        double recursiveNodeReusePreparationMs = 0d;
        double recursiveCornerCachePreparationMs = 0d;
        double recursiveCenterCachePreparationMs = 0d;
        double recursiveCrossingCachePreparationMs = 0d;
        double recursiveSubtreeCopyMs = 0d;
        double recursiveOtherMs = 0d;
        double flatCreateLayoutMs = 0d;
        double flatRuntimeCacheMs = 0d;
        double surfaceVertexMs = 0d;
        double surfaceCrossingMs = 0d;
        double surfaceNormalMs = 0d;
        int totalNodes = 0;
        int surfaceLeaves = 0;
        Vector3 buildBoundsSize = Vector3.zero;
        int sourceEvaluations = 0;
        int cornerCacheHits = 0;
        int cornerCacheMisses = 0;
        int persistentCornerCacheInvalidated = 0;
        int persistentCornerCacheSize = 0;
        int centerEvaluations = 0;
        int centerCacheHits = 0;
        int centerCacheMisses = 0;
        int persistentCenterCacheInvalidated = 0;
        int persistentCenterCacheSize = 0;
        int edgeEvaluations = 0;
        int crossingCacheHits = 0;
        int crossingCacheMisses = 0;
        int persistentCrossingCacheHits = 0;
        int persistentCrossingCacheInvalidated = 0;
        int persistentCrossingCacheSize = 0;
        int subdivisionMinDepth = 0;
        int subdivisionCornerCrossing = 0;
        int subdivisionCenterMismatch = 0;
        int subdivisionDistanceThreshold = 0;
        int subdivisionOnlyMinDepth = 0;
        int subdivisionOnlyCornerCrossing = 0;
        int subdivisionOnlyCenterMismatch = 0;
        int subdivisionOnlyDistanceThreshold = 0;
        int subdivisionMixedReasons = 0;
        int reusedNodes = 0;
        int reusedSubtrees = 0;
        bool frontierUsed = false;
        int frontierBatchCount = 0;
        int frontierSampleCount = 0;
        int frontierJobSampleCount = 0;
        int frontierSerialSampleCount = 0;
        double frontierPreparationMs = 0d;
        double frontierEvaluationMs = 0d;
        double frontierInsertionMs = 0d;
        double frontierTraversalMs = 0d;
        double frontierReuseCheckMs = 0d;
        double frontierCollectCornersMs = 0d;
        double frontierCollectCentersMs = 0d;
        double frontierSubdivideDecisionMs = 0d;
        double frontierEnqueueChildrenMs = 0d;
        double frontierNodeRecordMs = 0d;
        double frontierBuildReplayMs = 0d;

        if (includeFlatBuildStats)
        {
            FlatOctreeVolumeBuilder.BuildStats stats = octreeSampler.flatBuilder.LastBuildStats;
            flatBuildMs = stats.totalMs;
            flatRecursiveMs = stats.recursiveBuildMs;
            recursiveCornerSampleMs = stats.recursiveCornerSampleMs;
            recursiveCenterDecisionMs = stats.recursiveCenterDecisionMs;
            recursiveChildCornerMs = stats.recursiveChildCornerMs;
            recursiveNodeRecordMs = stats.recursiveNodeRecordMs;
            recursiveNodeReusePreparationMs = stats.recursiveNodeReusePreparationMs;
            recursiveCornerCachePreparationMs = stats.recursiveCornerCachePreparationMs;
            recursiveCenterCachePreparationMs = stats.recursiveCenterCachePreparationMs;
            recursiveCrossingCachePreparationMs = stats.recursiveCrossingCachePreparationMs;
            recursiveSubtreeCopyMs = stats.recursiveSubtreeCopyMs;
            recursiveOtherMs = stats.recursiveOtherMs;
            flatCreateLayoutMs = stats.createLayoutMs;
            flatRuntimeCacheMs = stats.runtimeCacheMs;
            surfaceVertexMs = stats.surfaceVertexMs;
            surfaceCrossingMs = stats.surfaceCrossingMs;
            surfaceNormalMs = stats.surfaceNormalMs;
            totalNodes = stats.totalNodes;
            surfaceLeaves = stats.surfaceLeaves;
            buildBoundsSize = stats.buildBoundsSize;
            sourceEvaluations = stats.sourceEvaluations;
            cornerCacheHits = stats.cornerCacheHits;
            cornerCacheMisses = stats.cornerCacheMisses;
            persistentCornerCacheInvalidated = stats.persistentCornerCacheInvalidated;
            persistentCornerCacheSize = stats.persistentCornerCacheSize;
            centerEvaluations = stats.centerEvaluations;
            centerCacheHits = stats.centerCacheHits;
            centerCacheMisses = stats.centerCacheMisses;
            persistentCenterCacheInvalidated = stats.persistentCenterCacheInvalidated;
            persistentCenterCacheSize = stats.persistentCenterCacheSize;
            edgeEvaluations = stats.edgeRefinementEvaluations;
            crossingCacheHits = stats.crossingCacheHits;
            crossingCacheMisses = stats.crossingCacheMisses;
            persistentCrossingCacheHits = stats.persistentCrossingCacheHits;
            persistentCrossingCacheInvalidated = stats.persistentCrossingCacheInvalidated;
            persistentCrossingCacheSize = stats.persistentCrossingCacheSize;
            subdivisionMinDepth = stats.subdivisionMinDepth;
            subdivisionCornerCrossing = stats.subdivisionCornerCrossing;
            subdivisionCenterMismatch = stats.subdivisionCenterMismatch;
            subdivisionDistanceThreshold = stats.subdivisionDistanceThreshold;
            subdivisionOnlyMinDepth = stats.subdivisionOnlyMinDepth;
            subdivisionOnlyCornerCrossing = stats.subdivisionOnlyCornerCrossing;
            subdivisionOnlyCenterMismatch = stats.subdivisionOnlyCenterMismatch;
            subdivisionOnlyDistanceThreshold = stats.subdivisionOnlyDistanceThreshold;
            subdivisionMixedReasons = stats.subdivisionMixedReasons;
            reusedNodes = stats.reusedNodeCount;
            reusedSubtrees = stats.reusedSubtreeCount;
            frontierUsed = stats.frontierUsed;
            frontierBatchCount = stats.frontierBatchCount;
            frontierSampleCount = stats.frontierSampleCount;
            frontierJobSampleCount = stats.frontierJobSampleCount;
            frontierSerialSampleCount = stats.frontierSerialSampleCount;
            frontierPreparationMs = stats.frontierPreparationMs;
            frontierEvaluationMs = stats.frontierEvaluationMs;
            frontierInsertionMs = stats.frontierInsertionMs;
            frontierTraversalMs = stats.frontierTraversalMs;
            frontierReuseCheckMs = stats.frontierReuseCheckMs;
            frontierCollectCornersMs = stats.frontierCollectCornersMs;
            frontierCollectCentersMs = stats.frontierCollectCentersMs;
            frontierSubdivideDecisionMs = stats.frontierSubdivideDecisionMs;
            frontierEnqueueChildrenMs = stats.frontierEnqueueChildrenMs;
            frontierNodeRecordMs = stats.frontierNodeRecordMs;
            frontierBuildReplayMs = stats.frontierBuildReplayMs;
        }

        return new RebuildProfileSample(
            totalMs,
            compositionMs,
            volumeBuildMs,
            renderMs,
            flatBuildMs,
            flatRecursiveMs,
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
            flatCreateLayoutMs,
            flatRuntimeCacheMs,
            surfaceVertexMs,
            surfaceCrossingMs,
            surfaceNormalMs,
            totalNodes,
            surfaceLeaves,
            buildBoundsSize,
            sourceEvaluations,
            cornerCacheHits,
            cornerCacheMisses,
            persistentCornerCacheInvalidated,
            persistentCornerCacheSize,
            centerEvaluations,
            centerCacheHits,
            centerCacheMisses,
            persistentCenterCacheInvalidated,
            persistentCenterCacheSize,
            edgeEvaluations,
            crossingCacheHits,
            crossingCacheMisses,
            persistentCrossingCacheHits,
            persistentCrossingCacheInvalidated,
            persistentCrossingCacheSize,
            subdivisionMinDepth,
            subdivisionCornerCrossing,
            subdivisionCenterMismatch,
            subdivisionDistanceThreshold,
            subdivisionOnlyMinDepth,
            subdivisionOnlyCornerCrossing,
            subdivisionOnlyCenterMismatch,
            subdivisionOnlyDistanceThreshold,
            subdivisionMixedReasons,
            reusedNodes,
            reusedSubtrees,
            renderStats.totalMs,
            renderStats.chunkRebuildMs,
            renderStats.chunkLocalBuildMs,
            renderStats.chunkClusterBuildMs,
            renderStats.chunkMeshBuildMs,
            renderStats.chunkApplyMeshMs,
            renderStats.chunkLocalBuilds,
            renderStats.chunkClusters,
            renderStats.chunkClusterBuilds,
            renderStats.chunkClusterFallbacks,
            renderStats.rebuilt,
            renderStats.queuedDirtyChunks,
            renderStats.hadDirtyBounds,
            renderStats.canDoDirtyRebuild,
            renderStats.fullRebuildRequested,
            frontierUsed,
            frontierBatchCount,
            frontierSampleCount,
            frontierJobSampleCount,
            frontierSerialSampleCount,
            frontierPreparationMs,
            frontierEvaluationMs,
            frontierInsertionMs,
            frontierTraversalMs,
            frontierReuseCheckMs,
            frontierCollectCornersMs,
            frontierCollectCentersMs,
            frontierSubdivideDecisionMs,
            frontierEnqueueChildrenMs,
            frontierNodeRecordMs,
            frontierBuildReplayMs);
    }

    private string BuildRebuildProfileLog(
        double totalMs,
        double compositionMs,
        double volumeBuildMs,
        double renderMs,
        string rebuildCause,
        bool hadDirtyBounds,
        bool usedIncrementalUpdate,
        bool includeFlatBuildStats)
    {
        VolumeMeshRenderer.RenderStats renderStats = RenderOutput.LastRenderStats;
        string log =
            $"Volume Rebuild Profile [{GetPipelineDebugLabel()}]: " +
            $"model(total={totalMs:F2} ms, composition={compositionMs:F2} ms, volumeBuild={volumeBuildMs:F2} ms, render={renderMs:F2} ms), " +
            $"cause={rebuildCause}, hasDirty={hadDirtyBounds}, incremental={usedIncrementalUpdate}, refinementSteps={GetEffectiveEdgeRefinementSteps()}, " +
            $"renderer(total={renderStats.totalMs:F2} ms, queueSetup={renderStats.queueSetupMs:F2} ms, chunkRebuild={renderStats.chunkRebuildMs:F2} ms, chunkLocalBuild={renderStats.chunkLocalBuildMs:F2} ms, chunkClusterBuild={renderStats.chunkClusterBuildMs:F2} ms, chunkMeshBuild={renderStats.chunkMeshBuildMs:F2} ms, chunkApplyMesh={renderStats.chunkApplyMeshMs:F2} ms, chunkLocalChunks={renderStats.chunkLocalBuilds}, chunkClusters={renderStats.chunkClusters}, chunkClusterBuilds={renderStats.chunkClusterBuilds}, chunkFallbacks={renderStats.chunkClusterFallbacks}, rebuilt={renderStats.rebuilt}, pending={renderStats.pending}, budget={renderStats.budget}, " +
            $"dirtySeen={renderStats.hadDirtyBounds}, canDirty={renderStats.canDoDirtyRebuild}, full={renderStats.fullRebuildRequested}, queuedDirty={renderStats.queuedDirtyChunks}, dirtySize={FormatVector(renderStats.dirtyBounds.size)})";

        if (includeFlatBuildStats)
        {
            FlatOctreeVolumeBuilder.BuildStats stats = octreeSampler.flatBuilder.LastBuildStats;
            log +=
                $", flatBuild(total={stats.totalMs:F2} ms, recursive={stats.recursiveBuildMs:F2} ms, createLayout={stats.createLayoutMs:F2} ms, runtimeCache={stats.runtimeCacheMs:F2} ms, reusedNodes={stats.reusedNodeCount}, reusedSubtrees={stats.reusedSubtreeCount}), " +
                $"{FormatRecursivePartsLog(stats)}, " +
                $"surface(vertex={stats.surfaceVertexMs:F2} ms, crossing={stats.surfaceCrossingMs:F2} ms, normal={stats.surfaceNormalMs:F2} ms), " +
                $"samples(total={stats.sourceEvaluations}, cornerMiss={stats.cornerCacheMisses}, center={stats.centerEvaluations}, edge={stats.edgeRefinementEvaluations}), " +
                $"cache(cornerHit={stats.cornerCacheHits}, cornerMiss={stats.cornerCacheMisses}, cornerInvalidated={stats.persistentCornerCacheInvalidated}, cornerSize={stats.persistentCornerCacheSize}, centerHit={stats.centerCacheHits}, centerMiss={stats.centerCacheMisses}, centerInvalidated={stats.persistentCenterCacheInvalidated}, centerSize={stats.persistentCenterCacheSize}, crossingHit={stats.crossingCacheHits}, crossingMiss={stats.crossingCacheMisses}), " +
                $"gc(gen0={stats.gcGen0Delta}, gen1={stats.gcGen1Delta}, gen2={stats.gcGen2Delta}), " +
                $"frontier(used={stats.frontierUsed}, batches={stats.frontierBatchCount}, samples={stats.frontierSampleCount}, jobSamples={stats.frontierJobSampleCount}, serialSamples={stats.frontierSerialSampleCount}, prep={stats.frontierPreparationMs:F2} ms, traversal={stats.frontierTraversalMs:F2} ms, reuse={stats.frontierReuseCheckMs:F2} ms, collectCorners={stats.frontierCollectCornersMs:F2} ms, collectCenters={stats.frontierCollectCentersMs:F2} ms, subdivide={stats.frontierSubdivideDecisionMs:F2} ms, enqueue={stats.frontierEnqueueChildrenMs:F2} ms, nodeRecord={stats.frontierNodeRecordMs:F2} ms, eval={stats.frontierEvaluationMs:F2} ms, insert={stats.frontierInsertionMs:F2} ms, replay={stats.frontierBuildReplayMs:F2} ms)";
        }

        return log;
    }

    private string BenchmarkLogDirectoryPath
    {
        get
        {
#if UNITY_EDITOR
            DirectoryInfo projectRoot = Directory.GetParent(Application.dataPath);
            if (projectRoot != null)
                return Path.Combine(projectRoot.FullName, "Logs");
#endif
            return Path.Combine(Application.persistentDataPath, "Logs");
        }
    }

    private string BenchmarkLatestLogPath =>
        Path.Combine(BenchmarkLogDirectoryPath, "volume-benchmark-latest.log");

    private void EnsureBenchmarkLogFile()
    {
        if (!writeBenchmarkLogsToFile || _benchmarkLogFileReady)
            return;

        try
        {
            Directory.CreateDirectory(BenchmarkLogDirectoryPath);
            string timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
            _benchmarkArchiveLogPath = Path.Combine(
                BenchmarkLogDirectoryPath,
                $"volume-benchmark-{timestamp}.log");

            string header =
                $"SDF benchmark log session started {DateTime.Now:O}\n" +
                $"Scene: {gameObject.scene.name}\n" +
                $"Model: {name}\n\n";
            File.WriteAllText(BenchmarkLatestLogPath, header);
            File.WriteAllText(_benchmarkArchiveLogPath, header);
            _benchmarkLogFileReady = true;
            TrimBenchmarkLogArchive();
        }
        catch (Exception ex)
        {
            _benchmarkLogFileReady = false;
            Debug.LogWarning($"Failed to initialize SDF benchmark log file: {ex.Message}");
        }
    }

    private void AppendBenchmarkLogSection(string sectionTitle, string message)
    {
        if (!writeBenchmarkLogsToFile || string.IsNullOrEmpty(message))
            return;

        EnsureBenchmarkLogFile();
        if (!_benchmarkLogFileReady)
            return;

        string block =
            $"===== {sectionTitle} | {DateTime.Now:O} =====\n" +
            message +
            "\n\n";

        try
        {
            File.AppendAllText(BenchmarkLatestLogPath, block);
            if (!string.IsNullOrEmpty(_benchmarkArchiveLogPath))
                File.AppendAllText(_benchmarkArchiveLogPath, block);
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"Failed to append SDF benchmark log file: {ex.Message}");
        }
    }

    private void TrimBenchmarkLogArchive()
    {
        int limit = Mathf.Max(1, benchmarkLogArchiveLimit);
        FileInfo[] files = new DirectoryInfo(BenchmarkLogDirectoryPath)
            .GetFiles("volume-benchmark-*.log");
        Array.Sort(files, (a, b) => b.CreationTimeUtc.CompareTo(a.CreationTimeUtc));

        for (int i = limit; i < files.Length; i++)
        {
            try
            {
                files[i].Delete();
            }
            catch (Exception)
            {
                // Best-effort cleanup only; logging must not fail because old files are locked.
            }
        }
    }

    private void LogBenchmarkSection(string sectionTitle, string message)
    {
        Debug.Log(message);
        AppendBenchmarkLogSection(sectionTitle, message);
    }

    public void RunRebuildBenchmark()
    {
        int runs = Mathf.Max(2, benchmarkRuns);
        RebuildProfileSample[] samples = new RebuildProfileSample[runs];
        bool previousSuppressRebuildProfileLog = _suppressRebuildProfileLog;
        _suppressRebuildProfileLog = true;

        try
        {
            for (int i = 0; i < runs; i++)
            {
                RebuildModel();
                samples[i] = LastRebuildProfileSample;
            }
        }
        finally
        {
            _suppressRebuildProfileLog = previousSuppressRebuildProfileLog;
        }

        LogBenchmarkSection("Non Visual Steps / Rebuild Benchmark", BuildRebuildBenchmarkLog(samples));
    }

    private string BuildRebuildBenchmarkLog(RebuildProfileSample[] samples)
    {
        return
            $"Volume Rebuild Benchmark [{GetPipelineDebugLabel()}] runs={samples.Length}, refinementSteps={GetEffectiveEdgeRefinementSteps()}: " +
            $"model({Summarize(samples, s => s.totalMs)}), " +
            $"volumeBuild({Summarize(samples, s => s.volumeBuildMs)}), " +
            $"render({Summarize(samples, s => s.renderMs)}), " +
            $"rendererChunk({Summarize(samples, s => s.rendererChunkMs)}), " +
            $"chunkLocalBuild({Summarize(samples, s => s.rendererChunkLocalBuildMs)}), " +
            $"chunkClusterBuild({Summarize(samples, s => s.rendererChunkClusterBuildMs)}), " +
            $"chunkMeshBuild({Summarize(samples, s => s.rendererChunkMeshBuildMs)}), " +
            $"chunkApplyMesh({Summarize(samples, s => s.rendererChunkApplyMeshMs)}), " +
            $"chunkLocal(avgLocalChunks={AverageInt(samples, s => s.rendererChunkLocalBuilds):F1}, avgClusters={AverageInt(samples, s => s.rendererChunkClusters):F1}, avgClusterBuilds={AverageInt(samples, s => s.rendererChunkClusterBuilds):F1}, avgFallbacks={AverageInt(samples, s => s.rendererChunkClusterFallbacks):F1}), " +
            $"flatBuild({Summarize(samples, s => s.flatBuildMs)}), " +
            $"recursive({Summarize(samples, s => s.flatRecursiveMs)}), " +
            $"{FormatRecursivePartsLog(samples)}, " +
            $"createLayout({Summarize(samples, s => s.flatCreateLayoutMs)}), " +
            $"runtimeCache({Summarize(samples, s => s.flatRuntimeCacheMs)}), " +
            $"crossing({Summarize(samples, s => s.surfaceCrossingMs)}), " +
            $"normal({Summarize(samples, s => s.surfaceNormalMs)}), " +
            $"tree(avgNodes={AverageInt(samples, s => s.totalNodes):F0}, avgSurfaceLeaves={AverageInt(samples, s => s.surfaceLeaves):F0}, avgReusedNodes={AverageInt(samples, s => s.reusedNodes):F0}, avgReusedSubtrees={AverageInt(samples, s => s.reusedSubtrees):F0}, bounds={FormatVector(samples[0].buildBoundsSize)}), " +
            $"samples(avgSource={AverageInt(samples, s => s.sourceEvaluations):F0}, avgCornerMiss={AverageInt(samples, s => s.cornerCacheMisses):F0}, avgCenter={AverageInt(samples, s => s.centerEvaluations):F0}, avgEdge={AverageInt(samples, s => s.edgeEvaluations):F0}), " +
            $"cache(avgCornerHit={AverageInt(samples, s => s.cornerCacheHits):F0}, avgCornerMiss={AverageInt(samples, s => s.cornerCacheMisses):F0}, avgCornerInvalidated={AverageInt(samples, s => s.persistentCornerCacheInvalidated):F0}, avgCornerCacheSize={AverageInt(samples, s => s.persistentCornerCacheSize):F0}, avgCenterHit={AverageInt(samples, s => s.centerCacheHits):F0}, avgCenterMiss={AverageInt(samples, s => s.centerCacheMisses):F0}, avgCenterInvalidated={AverageInt(samples, s => s.persistentCenterCacheInvalidated):F0}, avgCenterCacheSize={AverageInt(samples, s => s.persistentCenterCacheSize):F0}, avgCrossingHit={AverageInt(samples, s => s.crossingCacheHits):F0}, avgCrossingMiss={AverageInt(samples, s => s.crossingCacheMisses):F0}, avgPersistentCrossingHit={AverageInt(samples, s => s.persistentCrossingCacheHits):F0}, avgCrossingInvalidated={AverageInt(samples, s => s.persistentCrossingCacheInvalidated):F0}, avgCrossingCacheSize={AverageInt(samples, s => s.persistentCrossingCacheSize):F0}), " +
            $"subdivision(avgMinDepth={AverageInt(samples, s => s.subdivisionMinDepth):F0}, avgCrossing={AverageInt(samples, s => s.subdivisionCornerCrossing):F0}, avgCenterMismatch={AverageInt(samples, s => s.subdivisionCenterMismatch):F0}, avgDistance={AverageInt(samples, s => s.subdivisionDistanceThreshold):F0}), " +
            $"exclusive(avgMinDepth={AverageInt(samples, s => s.subdivisionOnlyMinDepth):F0}, avgCrossing={AverageInt(samples, s => s.subdivisionOnlyCornerCrossing):F0}, avgCenterMismatch={AverageInt(samples, s => s.subdivisionOnlyCenterMismatch):F0}, avgDistance={AverageInt(samples, s => s.subdivisionOnlyDistanceThreshold):F0}, avgMixed={AverageInt(samples, s => s.subdivisionMixedReasons):F0}), " +
            $"chunks(avgRebuilt={AverageInt(samples, s => s.rebuiltChunks):F1}, avgQueuedDirty={AverageInt(samples, s => s.queuedDirtyChunks):F1}), " +
            $"frontier(used={Count(samples, s => s.frontierUsed)}/{samples.Length}, avgBatches={AverageInt(samples, s => s.frontierBatchCount):F0}, avgSamples={AverageInt(samples, s => s.frontierSampleCount):F0}, avgJobSamples={AverageInt(samples, s => s.frontierJobSampleCount):F0}, avgSerialSamples={AverageInt(samples, s => s.frontierSerialSampleCount):F0}, avgPrep={Average(samples, s => s.frontierPreparationMs):F2} ms, avgTraversal={Average(samples, s => s.frontierTraversalMs):F2} ms, avgReuse={Average(samples, s => s.frontierReuseCheckMs):F2} ms, avgCollectCorners={Average(samples, s => s.frontierCollectCornersMs):F2} ms, avgCollectCenters={Average(samples, s => s.frontierCollectCentersMs):F2} ms, avgSubdivide={Average(samples, s => s.frontierSubdivideDecisionMs):F2} ms, avgEnqueue={Average(samples, s => s.frontierEnqueueChildrenMs):F2} ms, avgNodeRecord={Average(samples, s => s.frontierNodeRecordMs):F2} ms, avgEval={Average(samples, s => s.frontierEvaluationMs):F2} ms, avgInsert={Average(samples, s => s.frontierInsertionMs):F2} ms, avgReplay={Average(samples, s => s.frontierBuildReplayMs):F2} ms), " +
            BuildWorstSampleLog(samples);
    }

    public void RunDirtyMoveBenchmark()
    {
        RunDirtyMoveBenchmark(visualizeDirtyMoveBenchmark);
    }

    public void RunDirtyMoveBenchmark(bool visualSteps)
    {
        if (visualSteps)
        {
            StartVisualDirtyMoveBenchmark();
            return;
        }

        VolumeObject target = ResolveDirtyMoveBenchmarkObject();
        if (target == null)
        {
            Debug.LogWarning("Volume dirty move benchmark skipped: assign a Dirty Move Benchmark Object or add at least one VolumeObject.");
            return;
        }

        int logicalRuns = Mathf.Max(2, benchmarkRuns);
        int sampleCount = GetDirtyMoveBenchmarkSampleCount(logicalRuns);
        RebuildProfileSample[] samples = new RebuildProfileSample[sampleCount];
        Vector3 originalPosition = target.transform.localPosition;
        Vector3 offset = dirtyMoveBenchmarkOffset;
        if (offset == Vector3.zero)
            offset = Vector3.back;

        Vector3 a = originalPosition;
        Vector3 b = originalPosition + offset;
        bool previousSuppressRebuildProfileLog = _suppressRebuildProfileLog;
        VolumeRebuildMode previousRebuildMode = rebuildMode;
        _suppressRebuildProfileLog = true;
        rebuildMode = VolumeRebuildMode.Manual;

        try
        {
            RebuildModel();
            Vector3 current = target.transform.localPosition;

            for (int i = 0; i < sampleCount; i++)
            {
                Vector3 next = GetDirtyMoveBenchmarkTargetPosition(i, logicalRuns, a, b);
                samples[i] = RunDirtyMoveBenchmarkStep(target, ref current, next);
            }
        }
        finally
        {
            try
            {
                if (restoreDirtyMoveBenchmarkObject && target != null && target.transform.localPosition != originalPosition)
                {
                    target.EstimateLocalMoveDirtyBoundsParts(
                        target.transform.localPosition,
                        originalPosition,
                        out Bounds restorePreviousDirtyBounds,
                        out Bounds restoreNextDirtyBounds);
                    target.transform.localPosition = originalPosition;
                    target.SyncEditorTransformCache();
                    MarkDirtyBounds(restorePreviousDirtyBounds);
                    MarkDirtyBounds(restoreNextDirtyBounds);
                    RebuildModel();
                }
            }
            finally
            {
                rebuildMode = previousRebuildMode;
                _suppressRebuildProfileLog = previousSuppressRebuildProfileLog;
            }
        }

        LogBenchmarkSection(
            "Non Visual Steps / Dirty Move Benchmark",
            BuildDirtyMoveBenchmarkLog(samples, target.name, offset, logicalRuns, visual: false));
    }

    private void StartVisualDirtyMoveBenchmark()
    {
        if (_dirtyMoveBenchmarkActive)
        {
            Debug.LogWarning("Volume dirty move benchmark is already running.");
            return;
        }

        VolumeObject target = ResolveDirtyMoveBenchmarkObject();
        if (target == null)
        {
            Debug.LogWarning("Volume dirty move benchmark skipped: assign a Dirty Move Benchmark Object or add at least one VolumeObject.");
            return;
        }

        _dirtyMoveBenchmarkActive = true;
        _dirtyMoveBenchmarkTarget = target;
        _dirtyMoveBenchmarkLogicalRuns = Mathf.Max(2, benchmarkRuns);
        _dirtyMoveBenchmarkSamples = new RebuildProfileSample[GetDirtyMoveBenchmarkSampleCount(_dirtyMoveBenchmarkLogicalRuns)];
        _dirtyMoveBenchmarkOriginalPosition = target.transform.localPosition;
        _dirtyMoveBenchmarkCurrentPosition = _dirtyMoveBenchmarkOriginalPosition;
        _dirtyMoveBenchmarkActiveOffset = dirtyMoveBenchmarkOffset == Vector3.zero
            ? Vector3.back
            : dirtyMoveBenchmarkOffset;
        _dirtyMoveBenchmarkA = _dirtyMoveBenchmarkOriginalPosition;
        _dirtyMoveBenchmarkB = _dirtyMoveBenchmarkOriginalPosition + _dirtyMoveBenchmarkActiveOffset;
        _dirtyMoveBenchmarkStep = 0;
        _dirtyMoveBenchmarkNextStepTime = EditorApplication.timeSinceStartup;
        _dirtyMoveBenchmarkPreviousSuppressLog = _suppressRebuildProfileLog;
        _dirtyMoveBenchmarkPreviousRebuildMode = rebuildMode;
        _suppressRebuildProfileLog = true;
        rebuildMode = VolumeRebuildMode.Manual;

        RebuildModel();
        EditorApplication.update -= RunVisualDirtyMoveBenchmarkStep;
        EditorApplication.update += RunVisualDirtyMoveBenchmarkStep;
        RequestVisualBenchmarkEditorTick();
    }

    private void RunVisualDirtyMoveBenchmarkStep()
    {
        if (!_dirtyMoveBenchmarkActive || _dirtyMoveBenchmarkTarget == null)
        {
            FinishVisualDirtyMoveBenchmark(cancelled: true);
            return;
        }

        if (RenderOutput.LastRenderStats.pending > 0)
        {
            RequestVisualBenchmarkEditorTick();
            return;
        }

        if (EditorApplication.timeSinceStartup < _dirtyMoveBenchmarkNextStepTime)
        {
            RequestVisualBenchmarkEditorTick();
            return;
        }

        if (_dirtyMoveBenchmarkStep < _dirtyMoveBenchmarkSamples.Length)
        {
            Vector3 next = GetDirtyMoveBenchmarkTargetPosition(
                _dirtyMoveBenchmarkStep,
                _dirtyMoveBenchmarkLogicalRuns,
                _dirtyMoveBenchmarkA,
                _dirtyMoveBenchmarkB
            );
            _dirtyMoveBenchmarkSamples[_dirtyMoveBenchmarkStep] = RunDirtyMoveBenchmarkStep(
                _dirtyMoveBenchmarkTarget,
                ref _dirtyMoveBenchmarkCurrentPosition,
                next
            );
            RenderOutput?.DrainPendingChunksImmediately();
            _dirtyMoveBenchmarkStep++;
            _dirtyMoveBenchmarkNextStepTime = EditorApplication.timeSinceStartup +
                Mathf.Max(0f, dirtyMoveBenchmarkStepDelayMs) / 1000d;
            RequestVisualBenchmarkEditorTick();
            return;
        }

        FinishVisualDirtyMoveBenchmark(cancelled: false);
    }

    private static void RequestVisualBenchmarkEditorTick()
    {
        EditorApplication.QueuePlayerLoopUpdate();
        SceneView.RepaintAll();
    }

    private void FinishVisualDirtyMoveBenchmark(bool cancelled)
    {
        EditorApplication.update -= RunVisualDirtyMoveBenchmarkStep;

        try
        {
            if (!cancelled &&
                restoreDirtyMoveBenchmarkObject &&
                _dirtyMoveBenchmarkTarget != null &&
                _dirtyMoveBenchmarkTarget.transform.localPosition != _dirtyMoveBenchmarkOriginalPosition)
            {
                Vector3 current = _dirtyMoveBenchmarkTarget.transform.localPosition;
                RunDirtyMoveBenchmarkStep(
                    _dirtyMoveBenchmarkTarget,
                    ref current,
                    _dirtyMoveBenchmarkOriginalPosition
                );
            }
        }
        finally
        {
            rebuildMode = _dirtyMoveBenchmarkPreviousRebuildMode;
            _suppressRebuildProfileLog = _dirtyMoveBenchmarkPreviousSuppressLog;
            _dirtyMoveBenchmarkActive = false;
        }

        if (!cancelled && _dirtyMoveBenchmarkSamples != null && _dirtyMoveBenchmarkTarget != null)
        {
            LogBenchmarkSection(
                "Visual Steps / Dirty Move Benchmark",
                BuildDirtyMoveBenchmarkLog(
                _dirtyMoveBenchmarkSamples,
                _dirtyMoveBenchmarkTarget.name,
                _dirtyMoveBenchmarkActiveOffset,
                _dirtyMoveBenchmarkLogicalRuns,
                visual: true
            ));
        }

        _dirtyMoveBenchmarkTarget = null;
        _dirtyMoveBenchmarkSamples = null;
    }

    private Vector3 GetDirtyMoveBenchmarkTargetPosition(int step, int runs, Vector3 a, Vector3 b)
    {
        if (benchmarkType == VolumeBenchmarkType.DirtyMove)
            return step % 2 == 0 ? b : a;

        if (benchmarkType == VolumeBenchmarkType.DirtyMoveSweep)
        {
            if (step % 2 == 1)
                return a;

            int sweepStep = step / 2;
            float sweepT = (sweepStep + 1f) / Mathf.Max(1, runs);
            return Vector3.LerpUnclamped(a, b, sweepT);
        }

        float t = (step + 1f) / Mathf.Max(1, runs);
        return Vector3.LerpUnclamped(a, b, t);
    }

    private int GetDirtyMoveBenchmarkSampleCount(int logicalRuns)
    {
        if (benchmarkType == VolumeBenchmarkType.DirtyMoveSweep)
            return Mathf.Max(1, logicalRuns) * 2;

        return Mathf.Max(1, logicalRuns);
    }

    private RebuildProfileSample RunDirtyMoveBenchmarkStep(
        VolumeObject target,
        ref Vector3 current,
        Vector3 next,
        bool forceFullRebuild = false)
    {
        target.EstimateLocalMoveDirtyBoundsParts(current, next, out Bounds previousDirtyBounds, out Bounds nextDirtyBounds);
        target.transform.localPosition = next;
        target.SyncEditorTransformCache();
        if (forceFullRebuild)
            ClearDirtyBounds();
        else
        {
            MarkDirtyBounds(previousDirtyBounds);
            MarkDirtyBounds(nextDirtyBounds);
        }
        RebuildModel();
        current = next;
        return LastRebuildProfileSample;
    }

    private VolumeObject ResolveDirtyMoveBenchmarkObject()
    {
        if (dirtyMoveBenchmarkObject != null)
            return dirtyMoveBenchmarkObject;

        VolumeSceneComposer composer = GetComponent<VolumeSceneComposer>();
        if (composer == null)
            return null;

        composer.objects.RemoveAll(o => o == null);
        return composer.objects.Count > 0 ? composer.objects[composer.objects.Count - 1] : null;
    }

    private string BuildDirtyMoveBenchmarkLog(
        RebuildProfileSample[] samples,
        string targetName,
        Vector3 offset,
        int logicalRuns,
        bool visual)
    {
        return
            $"Volume Dirty Move Benchmark [{GetPipelineDebugLabel()}] type={benchmarkType}, target={targetName}, logicalRuns={logicalRuns}, samples={samples.Length}, visual={visual}, restore={restoreDirtyMoveBenchmarkObject}, offset={FormatVector(offset)}, refinementSteps={GetEffectiveEdgeRefinementSteps()}: " +
            $"model({Summarize(samples, s => s.totalMs)}), " +
            $"volumeBuild({Summarize(samples, s => s.volumeBuildMs)}), " +
            $"render({Summarize(samples, s => s.renderMs)}), " +
            $"rendererChunk({Summarize(samples, s => s.rendererChunkMs)}), " +
            $"chunkLocalBuild({Summarize(samples, s => s.rendererChunkLocalBuildMs)}), " +
            $"chunkClusterBuild({Summarize(samples, s => s.rendererChunkClusterBuildMs)}), " +
            $"chunkMeshBuild({Summarize(samples, s => s.rendererChunkMeshBuildMs)}), " +
            $"chunkApplyMesh({Summarize(samples, s => s.rendererChunkApplyMeshMs)}), " +
            $"chunkLocal(avgLocalChunks={AverageInt(samples, s => s.rendererChunkLocalBuilds):F1}, avgClusters={AverageInt(samples, s => s.rendererChunkClusters):F1}, avgClusterBuilds={AverageInt(samples, s => s.rendererChunkClusterBuilds):F1}, avgFallbacks={AverageInt(samples, s => s.rendererChunkClusterFallbacks):F1}), " +
            $"flatBuild({Summarize(samples, s => s.flatBuildMs)}), " +
            $"recursive({Summarize(samples, s => s.flatRecursiveMs)}), " +
            $"{FormatRecursivePartsLog(samples)}, " +
            $"createLayout({Summarize(samples, s => s.flatCreateLayoutMs)}), " +
            $"runtimeCache({Summarize(samples, s => s.flatRuntimeCacheMs)}), " +
            $"crossing({Summarize(samples, s => s.surfaceCrossingMs)}), " +
            $"tree(avgNodes={AverageInt(samples, s => s.totalNodes):F0}, avgSurfaceLeaves={AverageInt(samples, s => s.surfaceLeaves):F0}, avgReusedNodes={AverageInt(samples, s => s.reusedNodes):F0}, avgReusedSubtrees={AverageInt(samples, s => s.reusedSubtrees):F0}, bounds={FormatVector(samples[0].buildBoundsSize)}), " +
            $"samples(avgSource={AverageInt(samples, s => s.sourceEvaluations):F0}, avgCornerMiss={AverageInt(samples, s => s.cornerCacheMisses):F0}, avgCenter={AverageInt(samples, s => s.centerEvaluations):F0}, avgEdge={AverageInt(samples, s => s.edgeEvaluations):F0}), " +
            $"cache(avgCornerHit={AverageInt(samples, s => s.cornerCacheHits):F0}, avgCornerMiss={AverageInt(samples, s => s.cornerCacheMisses):F0}, avgCornerInvalidated={AverageInt(samples, s => s.persistentCornerCacheInvalidated):F0}, avgCornerCacheSize={AverageInt(samples, s => s.persistentCornerCacheSize):F0}, avgCenterHit={AverageInt(samples, s => s.centerCacheHits):F0}, avgCenterMiss={AverageInt(samples, s => s.centerCacheMisses):F0}, avgCenterInvalidated={AverageInt(samples, s => s.persistentCenterCacheInvalidated):F0}, avgCenterCacheSize={AverageInt(samples, s => s.persistentCenterCacheSize):F0}, avgCrossingHit={AverageInt(samples, s => s.crossingCacheHits):F0}, avgCrossingMiss={AverageInt(samples, s => s.crossingCacheMisses):F0}, avgPersistentCrossingHit={AverageInt(samples, s => s.persistentCrossingCacheHits):F0}, avgCrossingInvalidated={AverageInt(samples, s => s.persistentCrossingCacheInvalidated):F0}, avgCrossingCacheSize={AverageInt(samples, s => s.persistentCrossingCacheSize):F0}), " +
            $"subdivision(avgMinDepth={AverageInt(samples, s => s.subdivisionMinDepth):F0}, avgCrossing={AverageInt(samples, s => s.subdivisionCornerCrossing):F0}, avgCenterMismatch={AverageInt(samples, s => s.subdivisionCenterMismatch):F0}, avgDistance={AverageInt(samples, s => s.subdivisionDistanceThreshold):F0}), " +
            $"exclusive(avgMinDepth={AverageInt(samples, s => s.subdivisionOnlyMinDepth):F0}, avgCrossing={AverageInt(samples, s => s.subdivisionOnlyCornerCrossing):F0}, avgCenterMismatch={AverageInt(samples, s => s.subdivisionOnlyCenterMismatch):F0}, avgDistance={AverageInt(samples, s => s.subdivisionOnlyDistanceThreshold):F0}, avgMixed={AverageInt(samples, s => s.subdivisionMixedReasons):F0}), " +
            $"chunks(avgRebuilt={AverageInt(samples, s => s.rebuiltChunks):F1}, avgQueuedDirty={AverageInt(samples, s => s.queuedDirtyChunks):F1}, dirtySeen={Count(samples, s => s.dirtySeen)}/{samples.Length}, canDirty={Count(samples, s => s.canUseDirtyChunks)}/{samples.Length}, full={Count(samples, s => s.fullChunkRebuild)}/{samples.Length}), " +
            $"frontier(used={Count(samples, s => s.frontierUsed)}/{samples.Length}, avgBatches={AverageInt(samples, s => s.frontierBatchCount):F0}, avgSamples={AverageInt(samples, s => s.frontierSampleCount):F0}, avgJobSamples={AverageInt(samples, s => s.frontierJobSampleCount):F0}, avgSerialSamples={AverageInt(samples, s => s.frontierSerialSampleCount):F0}, avgPrep={Average(samples, s => s.frontierPreparationMs):F2} ms, avgTraversal={Average(samples, s => s.frontierTraversalMs):F2} ms, avgReuse={Average(samples, s => s.frontierReuseCheckMs):F2} ms, avgCollectCorners={Average(samples, s => s.frontierCollectCornersMs):F2} ms, avgCollectCenters={Average(samples, s => s.frontierCollectCentersMs):F2} ms, avgSubdivide={Average(samples, s => s.frontierSubdivideDecisionMs):F2} ms, avgEnqueue={Average(samples, s => s.frontierEnqueueChildrenMs):F2} ms, avgNodeRecord={Average(samples, s => s.frontierNodeRecordMs):F2} ms, avgEval={Average(samples, s => s.frontierEvaluationMs):F2} ms, avgInsert={Average(samples, s => s.frontierInsertionMs):F2} ms, avgReplay={Average(samples, s => s.frontierBuildReplayMs):F2} ms), " +
            BuildWorstSampleLog(samples);
    }

    private static string Summarize(RebuildProfileSample[] samples, System.Func<RebuildProfileSample, double> selector)
    {
        double[] values = new double[samples.Length];
        double sum = 0d;
        for (int i = 0; i < samples.Length; i++)
        {
            double value = selector(samples[i]);
            values[i] = value;
            sum += value;
        }

        System.Array.Sort(values);
        double median = values.Length % 2 == 0
            ? (values[values.Length / 2 - 1] + values[values.Length / 2]) * 0.5d
            : values[values.Length / 2];
        int p95Index = Mathf.Clamp(Mathf.CeilToInt(values.Length * 0.95f) - 1, 0, values.Length - 1);
        double p95 = values[p95Index];

        return $"min={values[0]:F2} ms, med={median:F2} ms, avg={sum / values.Length:F2} ms, p95={p95:F2} ms, max={values[values.Length - 1]:F2} ms";
    }

    private string BuildWorstSampleLog(RebuildProfileSample[] samples)
    {
        int index = FindMaxSampleIndex(samples, s => s.totalMs);
        RebuildProfileSample sample = samples[index];

        return
            $"worst(index={index}, total={sample.totalMs:F2} ms, volumeBuild={sample.volumeBuildMs:F2} ms, render={sample.renderMs:F2} ms, " +
            $"recursive={sample.flatRecursiveMs:F2} ms, createLayout={sample.flatCreateLayoutMs:F2} ms, runtimeCache={sample.flatRuntimeCacheMs:F2} ms, crossing={sample.surfaceCrossingMs:F2} ms, " +
            $"{FormatRecursivePartsLog(sample)}, " +
            $"nodes={sample.totalNodes}, surfaceLeaves={sample.surfaceLeaves}, reusedNodes={sample.reusedNodes}, reusedSubtrees={sample.reusedSubtrees}, bounds={FormatVector(sample.buildBoundsSize)}, " +
            $"rebuilt={sample.rebuiltChunks}, queuedDirty={sample.queuedDirtyChunks}, source={sample.sourceEvaluations}, cornerMiss={sample.cornerCacheMisses}, center={sample.centerEvaluations}, edge={sample.edgeEvaluations}, " +
            $"cornerHit={sample.cornerCacheHits}, cornerInvalidated={sample.persistentCornerCacheInvalidated}, cornerCacheSize={sample.persistentCornerCacheSize}, centerHit={sample.centerCacheHits}, centerMiss={sample.centerCacheMisses}, centerInvalidated={sample.persistentCenterCacheInvalidated}, centerCacheSize={sample.persistentCenterCacheSize}, crossingHit={sample.crossingCacheHits}, crossingMiss={sample.crossingCacheMisses}, persistentCrossingHit={sample.persistentCrossingCacheHits}, crossingInvalidated={sample.persistentCrossingCacheInvalidated}, crossingCacheSize={sample.persistentCrossingCacheSize}, " +
            $"subdivision(minDepth={sample.subdivisionMinDepth}, crossing={sample.subdivisionCornerCrossing}, centerMismatch={sample.subdivisionCenterMismatch}, distance={sample.subdivisionDistanceThreshold}), " +
            $"exclusive(minDepth={sample.subdivisionOnlyMinDepth}, crossing={sample.subdivisionOnlyCornerCrossing}, centerMismatch={sample.subdivisionOnlyCenterMismatch}, distance={sample.subdivisionOnlyDistanceThreshold}, mixed={sample.subdivisionMixedReasons}), " +
            $"dirtySeen={sample.dirtySeen}, canDirty={sample.canUseDirtyChunks}, full={sample.fullChunkRebuild}, " +
            $"frontier(used={sample.frontierUsed}, batches={sample.frontierBatchCount}, samples={sample.frontierSampleCount}, jobSamples={sample.frontierJobSampleCount}, serialSamples={sample.frontierSerialSampleCount}, prep={sample.frontierPreparationMs:F2} ms, traversal={sample.frontierTraversalMs:F2} ms, reuse={sample.frontierReuseCheckMs:F2} ms, collectCorners={sample.frontierCollectCornersMs:F2} ms, collectCenters={sample.frontierCollectCentersMs:F2} ms, subdivide={sample.frontierSubdivideDecisionMs:F2} ms, enqueue={sample.frontierEnqueueChildrenMs:F2} ms, nodeRecord={sample.frontierNodeRecordMs:F2} ms, eval={sample.frontierEvaluationMs:F2} ms, insert={sample.frontierInsertionMs:F2} ms, replay={sample.frontierBuildReplayMs:F2} ms))";
    }

    private static int FindMaxSampleIndex(RebuildProfileSample[] samples, System.Func<RebuildProfileSample, double> selector)
    {
        int bestIndex = 0;
        double bestValue = selector(samples[0]);
        for (int i = 1; i < samples.Length; i++)
        {
            double value = selector(samples[i]);
            if (value > bestValue)
            {
                bestValue = value;
                bestIndex = i;
            }
        }

        return bestIndex;
    }

    private static double AverageInt(RebuildProfileSample[] samples, System.Func<RebuildProfileSample, int> selector)
    {
        double sum = 0d;
        for (int i = 0; i < samples.Length; i++)
            sum += selector(samples[i]);

        return samples.Length > 0 ? sum / samples.Length : 0d;
    }

    private static double Average(RebuildProfileSample[] samples, System.Func<RebuildProfileSample, double> selector)
    {
        double sum = 0d;
        for (int i = 0; i < samples.Length; i++)
            sum += selector(samples[i]);

        return samples.Length > 0 ? sum / samples.Length : 0d;
    }

    private string FormatRecursivePartsLog(FlatOctreeVolumeBuilder.BuildStats stats)
    {
        if (!profileFlatRecursiveParts)
            return "recursiveParts=disabled";

        return $"recursiveParts(nodePrep={stats.recursiveNodeReusePreparationMs:F2} ms, cornerCachePrep={stats.recursiveCornerCachePreparationMs:F2} ms, centerCachePrep={stats.recursiveCenterCachePreparationMs:F2} ms, crossingCachePrep={stats.recursiveCrossingCachePreparationMs:F2} ms, subtreeCopy={stats.recursiveSubtreeCopyMs:F2} ms, corner={stats.recursiveCornerSampleMs:F2} ms, center={stats.recursiveCenterDecisionMs:F2} ms, childCorner={stats.recursiveChildCornerMs:F2} ms, nodeRecord={stats.recursiveNodeRecordMs:F2} ms, other={stats.recursiveOtherMs:F2} ms)";
    }

    private string FormatRecursivePartsLog(RebuildProfileSample[] samples)
    {
        if (!profileFlatRecursiveParts)
            return "recursiveParts=disabled";

        return $"recursiveParts(nodePrep={Average(samples, s => s.recursiveNodeReusePreparationMs):F2} ms, cornerCachePrep={Average(samples, s => s.recursiveCornerCachePreparationMs):F2} ms, centerCachePrep={Average(samples, s => s.recursiveCenterCachePreparationMs):F2} ms, crossingCachePrep={Average(samples, s => s.recursiveCrossingCachePreparationMs):F2} ms, subtreeCopy={Average(samples, s => s.recursiveSubtreeCopyMs):F2} ms, corner={Average(samples, s => s.recursiveCornerSampleMs):F2} ms, center={Average(samples, s => s.recursiveCenterDecisionMs):F2} ms, childCorner={Average(samples, s => s.recursiveChildCornerMs):F2} ms, nodeRecord={Average(samples, s => s.recursiveNodeRecordMs):F2} ms, other={Average(samples, s => s.recursiveOtherMs):F2} ms)";
    }

    private string FormatRecursivePartsLog(RebuildProfileSample sample)
    {
        if (!profileFlatRecursiveParts)
            return "recursiveParts=disabled";

        return $"recursiveParts(nodePrep={sample.recursiveNodeReusePreparationMs:F2} ms, cornerCachePrep={sample.recursiveCornerCachePreparationMs:F2} ms, centerCachePrep={sample.recursiveCenterCachePreparationMs:F2} ms, crossingCachePrep={sample.recursiveCrossingCachePreparationMs:F2} ms, subtreeCopy={sample.recursiveSubtreeCopyMs:F2} ms, corner={sample.recursiveCornerSampleMs:F2} ms, center={sample.recursiveCenterDecisionMs:F2} ms, childCorner={sample.recursiveChildCornerMs:F2} ms, nodeRecord={sample.recursiveNodeRecordMs:F2} ms, other={sample.recursiveOtherMs:F2} ms)";
    }

    private static int Count(RebuildProfileSample[] samples, System.Func<RebuildProfileSample, bool> selector)
    {
        int count = 0;
        for (int i = 0; i < samples.Length; i++)
        {
            if (selector(samples[i]))
                count++;
        }

        return count;
    }

    private static string FormatVector(Vector3 value)
    {
        return $"({value.x:F2},{value.y:F2},{value.z:F2})";
    }
#endif

    public string GetPipelineDebugLabel()
    {
        return $"{dataStructure}/{storageMode}/{octreeMesherType}";
    }

    public bool IsPreviewRebuild => _isPreviewRebuild;

    public bool GetEffectiveUseQefVertices()
    {
        return useQefVertices && !(simplifyQefDuringPreview && _isPreviewRebuild);
    }

    public QefVertexMode GetEffectiveQefVertexMode()
    {
        return simplifyQefDuringPreview && _isPreviewRebuild
            ? QefVertexMode.AverageCrossings
            : qefVertexMode;
    }

    public bool GetEffectiveQefEnableMultiHermite()
    {
        return qefEnableMultiHermite && !(simplifyQefDuringPreview && _isPreviewRebuild);
    }

    public VolumeStorageMode GetEffectiveStorageMode()
    {
        if (useFlatDualContouringPreview &&
            _isPreviewRebuild &&
            octreeMesherType == OctreeMesherType.DualContouring &&
            (dataStructure == VolumeDataStructure.Octree || dataStructure == VolumeDataStructure.SparseVoxelOctree))
        {
            return VolumeStorageMode.Flat;
        }

        return storageMode;
    }

    public int GetEffectiveEdgeRefinementSteps()
    {
        return _isPreviewRebuild
            ? Mathf.Min(Mathf.Max(0, edgeRefinementSteps), Mathf.Max(0, previewEdgeRefinementSteps))
            : Mathf.Max(0, edgeRefinementSteps);
    }

    public bool SetPreviewRebuildContext(bool isPreview)
    {
        bool previous = _isPreviewRebuild;
        _isPreviewRebuild = isPreview;
        return previous;
    }

    public void RestorePreviewRebuildContext(bool previous)
    {
        _isPreviewRebuild = previous;
    }

    public bool ShouldLogRebuildDuration()
    {
        return logRebuildDuration && !_isPreviewRebuild;
    }

    public bool ShouldLogChunkRebuildStats()
    {
        return logChunkRebuildStats && !_isPreviewRebuild;
    }

    private bool CanUseFlatOctreeBuilder()
    {
        bool usesAverageCrossingVertices =
            !GetEffectiveUseQefVertices() ||
            GetEffectiveQefVertexMode() == QefVertexMode.AverageCrossings;

        return dataStructure == VolumeDataStructure.Octree &&
               octreeMesherType == OctreeMesherType.DualContouring &&
               GetEffectiveStorageMode() == VolumeStorageMode.Flat &&
               usesAverageCrossingVertices;
    }

    private void ConfigureFlatOctreeBuilder(OctreeVolumeBuilder sourceBuilder)
    {
        FlatOctreeVolumeBuilder target = octreeSampler.flatBuilder;
        target.center = octreeSampler.center;
        target.size = octreeSampler.extent;
        target.boundsPadding = sourceBuilder.boundsPadding;
        target.maxDepth = sourceBuilder.maxDepth;
        target.minDepth = sourceBuilder.minDepth;
        target.suppressBuildLog = sourceBuilder.suppressBuildLog;
        target.edgeRefinementSteps = GetEffectiveEdgeRefinementSteps();
        target.profileRecursiveParts = profileFlatRecursiveParts;
    }

    /// <summary>Returns the currently active sampled volume data.</summary>
    public IVolumeData GetActiveVolume()
    {
        switch (dataStructure)
        {
            case VolumeDataStructure.VoxelGrid:
                return voxelGridSampler.Volume;

            case VolumeDataStructure.Octree:
                return octreeSampler.Volume;
            case VolumeDataStructure.SparseVoxelOctree:
                return sparseVoxelOctreeSampler.Volume;

            default:
                return null;
        }
    }

    /// <summary>Deletes all child volume objects and clears generated output.</summary>
    public void ClearObjects()
    {
        VolumeSceneComposer composer = GetComponent<VolumeSceneComposer>();

        if (composer == null)
            return;

        VolumeObject[] allObjects = GetComponentsInChildren<VolumeObject>(true);

        for (int i = allObjects.Length - 1; i >= 0; i--)
        {
#if UNITY_EDITOR
            if (!Application.isPlaying)
                DestroyImmediate(allObjects[i].gameObject);
            else
                Destroy(allObjects[i].gameObject);
#else
        Destroy(allObjects[i].gameObject);
#endif
        }

        composer.objects.Clear();
        composer.RebuildComposition();

        ClearRenderOutput();
    }

    /// <summary>Deletes the last registered volume object and rebuilds if needed.</summary>
    public void RemoveLastObject()
    {
        VolumeSceneComposer composer = GetComponent<VolumeSceneComposer>();

        if (composer == null)
            return;

        composer.objects.RemoveAll(o => o == null);

        if (composer.objects.Count == 0)
        {
            ClearRenderOutput();
            return;
        }

        VolumeObject last = composer.objects[composer.objects.Count - 1];
        composer.objects.RemoveAt(composer.objects.Count - 1);

#if UNITY_EDITOR
        if (!Application.isPlaying)
            DestroyImmediate(last.gameObject);
        else
            Destroy(last.gameObject);
#else
    Destroy(last.gameObject);
#endif

        composer.RebuildComposition();

        if (composer.objects.Count == 0)
        {
            ClearRenderOutput();
            return;
        }

        RebuildModel();
    }

    /// <summary>Draws the active volume bounds in the scene view.</summary>
    private void OnDrawGizmos()
    {
        DrawActiveBoundsGizmo(false);
    }

    /// <summary>Draws selected-state bounds and optional octree debug nodes.</summary>
    private void OnDrawGizmosSelected()
    {
        DrawActiveBoundsGizmo(true);
    }

    /// <summary>Draws the active sampler bounds and optional octree leaf boxes.</summary>
    private void DrawActiveBoundsGizmo(bool selected)
    {
        Bounds bounds;

        switch (dataStructure)
        {
            case VolumeDataStructure.VoxelGrid:
                bounds = voxelGridSampler.builder.Bounds;
                break;

            case VolumeDataStructure.Octree:
                bounds = octreeSampler.builder.Bounds;
                break;
            case VolumeDataStructure.SparseVoxelOctree:
                bounds = sparseVoxelOctreeSampler.builder.Bounds;
                break;

            default:
                return;
        }

        Gizmos.matrix = transform.localToWorldMatrix;

        Gizmos.color = selected
            ? new Color(0f, 1f, 1f, 1f)
            : new Color(0f, 1f, 1f, 0.35f);

        Gizmos.DrawWireCube(bounds.center, bounds.size);

        if (dataStructure == VolumeDataStructure.Octree &&
            renderOctreeDebugCubes &&
            octreeSampler.Volume != null)
        {
            DrawOctreeNode(octreeSampler.Volume.Root);
        }
        else if (dataStructure == VolumeDataStructure.SparseVoxelOctree &&
                 renderOctreeDebugCubes &&
                 sparseVoxelOctreeSampler.Volume != null)
        {
            DrawOctreeNode(sparseVoxelOctreeSampler.Volume.Root);
        }

        Gizmos.matrix = Matrix4x4.identity;
    }

    /// <summary>Recursively draws octree leaves that contain surface samples.</summary>
    private void DrawOctreeNode(OctreeNode node)
    {
        if (node == null)
            return;

        if (node.IsLeaf)
        {
            if (node.ContainsSurface)
            {
                Gizmos.color = new Color(1f, 1f, 0f, 0.2f);

                Gizmos.DrawWireCube(
                    node.Bounds.center,
                    node.Bounds.size
                );
            }

            return;
        }

        if (node.Children == null)
            return;

        for (int i = 0; i < node.Children.Length; i++)
        {
            DrawOctreeNode(node.Children[i]);
        }
    }

    private VolumeRenderOutput RenderOutput
    {
        get
        {
            Transform existing = transform.Find("VolumeRenderOutput");

            if (existing != null)
            {
                VolumeRenderOutput output = existing.GetComponent<VolumeRenderOutput>();

                if (output != null)
                    return output;
            }

            GameObject go = new GameObject("VolumeRenderOutput");
            go.transform.SetParent(transform, false);

            return go.AddComponent<VolumeRenderOutput>();
        }
    }

    /// <summary>Clears the current render output if it exists.</summary>
    private void ClearRenderOutput()
    {
        VolumeRenderOutput output = RenderOutput;

        if (output != null)
            output.Clear();
    }

    public void DrainPendingRenderChunksImmediately()
    {
        VolumeRenderOutput output = RenderOutput;
        output?.DrainPendingChunksImmediately();
    }

    public bool TryGetChunkBounds(out System.Collections.Generic.List<Bounds> bounds)
    {
        bounds = _chunkBoundsCache;
        bounds.Clear();

        IVolumeData activeVolume = GetActiveVolume();

        if (activeVolume is not IChunkLayoutVolume chunkLayoutVolume)
        {
            bounds = null;
            return false;
        }

        chunkLayoutVolume.BuildChunkBounds(chunking, bounds);

        if (bounds.Count == 0)
            bounds.Add(activeVolume.Bounds);

        return true;
    }

    public void MarkDirtyBounds(Bounds dirtyBounds)
    {
        _dirtyBoundsParts.Add(dirtyBounds);

        if (!_hasDirtyBounds)
        {
            _dirtyBounds = dirtyBounds;
            _hasDirtyBounds = true;
            return;
        }

        _dirtyBounds.Encapsulate(dirtyBounds);
    }

    public bool TryConsumeDirtyBounds(out Bounds dirtyBounds)
    {
        if (_hasDirtyBounds)
        {
            dirtyBounds = _dirtyBounds;
            _hasDirtyBounds = false;
            return true;
        }

        dirtyBounds = default;
        return false;
    }

    public bool TryGetPendingDirtyBounds(out Bounds dirtyBounds)
    {
        if (_hasDirtyBounds)
        {
            dirtyBounds = _dirtyBounds;
            return true;
        }

        dirtyBounds = default;
        return false;
    }

    public bool ConsumeForceFullChunkRenderOnce()
    {
        if (!_forceFullChunkRenderOnce)
            return false;

        _forceFullChunkRenderOnce = false;
        return true;
    }

    private void ClearDirtyBounds()
    {
#if UNITY_EDITOR
        if (_isPreviewRebuild && _hasDirtyBounds)
            CaptureFinalizePreviewDirtyBounds(_dirtyBounds);
#endif
        _dirtyBoundsParts.Clear();
        _hasDirtyBounds = false;
    }

    public OctreeVolume GetActiveOctreeVolume()
    {
        OctreeVolume active;
        switch (dataStructure)
        {
            case VolumeDataStructure.Octree:
                active = octreeSampler.Volume;
                break;
            case VolumeDataStructure.SparseVoxelOctree:
                active = sparseVoxelOctreeSampler.Volume?.AsOctreeVolume();
                break;
            default:
                return null;
        }

        if (active != null && storageMode == VolumeStorageMode.Flat)
        {
            // Force-build flat cache so downstream processors can use it immediately.
            active.GetFlatLayout();
        }
        return active;
    }

    public IFlatAdaptiveVolumeData GetActiveFlatAdaptiveVolume()
    {
        IFlatAdaptiveVolumeData active;
        switch (dataStructure)
        {
            case VolumeDataStructure.Octree:
                active = octreeSampler.Volume;
                break;
            case VolumeDataStructure.SparseVoxelOctree:
                active = sparseVoxelOctreeSampler.Volume;
                break;
            default:
                return null;
        }

        active?.GetFlatLayout(includeCornerValues: true);
        return active;
    }

#if UNITY_EDITOR
    public bool SupportsPreviewDepth()
    {
        return dataStructure == VolumeDataStructure.Octree ||
               dataStructure == VolumeDataStructure.SparseVoxelOctree;
    }

    public bool SupportsPreviewResolution()
    {
        return dataStructure == VolumeDataStructure.VoxelGrid;
    }

    public void NotifyInteractiveEdit()
    {
        _lastInteractiveEditTime = EditorApplication.timeSinceStartup;
    }

    public bool IsPreviewInteractionActive()
    {
        if (Application.isPlaying || !ShouldUseInteractionPreview())
            return false;

        bool previewEnabled =
            (SupportsPreviewDepth() && usePreviewDepthWhileInteracting) ||
            (SupportsPreviewResolution() && usePreviewResolutionWhileInteracting);

        if (!previewEnabled)
            return false;

        double elapsed = EditorApplication.timeSinceStartup - _lastInteractiveEditTime;
        return elapsed <= previewInteractionHoldSeconds;
    }

    private void QueueFinalizePreviewRebuild()
    {
        if (_finalizePreviewRebuildQueued || Application.isPlaying)
            return;

        _finalizePreviewRebuildQueued = true;
        EditorApplication.update -= FinalizePreviewRebuildIfNeeded;
        EditorApplication.update += FinalizePreviewRebuildIfNeeded;
    }

    private void FinalizePreviewRebuildIfNeeded()
    {
        if (this == null)
        {
            EditorApplication.update -= FinalizePreviewRebuildIfNeeded;
            return;
        }

        if (rebuildOnMoveRelease && IsPointerOrHandleActive())
        {
            return;
        }

        bool previewStillActive =
            ShouldUsePreviewDepth(GetConfiguredMaxDepth(), out _) ||
            ShouldUsePreviewVoxelResolution(voxelGridSampler.builder.gridSize, out _);
        if (previewStillActive)
        {
            return;
        }

        _finalizePreviewRebuildQueued = false;
        EditorApplication.update -= FinalizePreviewRebuildIfNeeded;
        _forceFullChunkRenderOnce = !_hasFinalizePreviewDirtyBounds;
        RestoreFinalizePreviewDirtyBounds();
        RebuildModel();
    }

    private void CaptureFinalizePreviewDirtyBounds(Bounds dirtyBounds)
    {
        if (_hasFinalizePreviewDirtyBounds)
        {
            _finalizePreviewDirtyBounds.Encapsulate(dirtyBounds);
            return;
        }

        _hasFinalizePreviewDirtyBounds = true;
        _finalizePreviewDirtyBounds = dirtyBounds;
    }

    private void RestoreFinalizePreviewDirtyBounds()
    {
        if (!_hasFinalizePreviewDirtyBounds)
            return;

        MarkDirtyBounds(_finalizePreviewDirtyBounds);
        _hasFinalizePreviewDirtyBounds = false;
        _finalizePreviewDirtyBounds = default;
    }

    private int GetConfiguredMaxDepth()
    {
        return dataStructure == VolumeDataStructure.Octree
            ? octreeSampler.builder.maxDepth
            : sparseVoxelOctreeSampler.builder.backend.maxDepth;
    }

    private static bool IsPointerOrHandleActive()
    {
        return GUIUtility.hotControl != 0;
    }

    private bool ShouldUsePreviewDepth(int configuredMaxDepth, out int effectiveMaxDepth)
    {
        effectiveMaxDepth = configuredMaxDepth;

        if (Application.isPlaying || !ShouldUseInteractionPreview() || !usePreviewDepthWhileInteracting)
            return false;

        if (configuredMaxDepth <= 1)
            return false;

        double elapsed = EditorApplication.timeSinceStartup - _lastInteractiveEditTime;
        if (elapsed > previewInteractionHoldSeconds)
            return false;

        effectiveMaxDepth = Mathf.Clamp(previewInteractionMaxDepth, 1, configuredMaxDepth);
        return effectiveMaxDepth < configuredMaxDepth;
    }

    private bool ShouldUsePreviewVoxelResolution(Vector3Int configuredGridSize, out Vector3Int effectiveGridSize)
    {
        effectiveGridSize = configuredGridSize;

        if (Application.isPlaying || !ShouldUseInteractionPreview() || dataStructure != VolumeDataStructure.VoxelGrid || !usePreviewResolutionWhileInteracting)
            return false;

        double elapsed = EditorApplication.timeSinceStartup - _lastInteractiveEditTime;
        if (elapsed > previewInteractionHoldSeconds)
            return false;

        effectiveGridSize = new Vector3Int(
            Mathf.Clamp(previewVoxelGridSize.x, 2, configuredGridSize.x),
            Mathf.Clamp(previewVoxelGridSize.y, 2, configuredGridSize.y),
            Mathf.Clamp(previewVoxelGridSize.z, 2, configuredGridSize.z)
        );

        return effectiveGridSize != configuredGridSize;
    }
#else
    private bool ShouldUsePreviewDepth(int configuredMaxDepth, out int effectiveMaxDepth)
    {
        effectiveMaxDepth = configuredMaxDepth;
        return false;
    }

    private bool ShouldUsePreviewVoxelResolution(Vector3Int configuredGridSize, out Vector3Int effectiveGridSize)
    {
        effectiveGridSize = configuredGridSize;
        return false;
    }
#endif

}
#endif // LEGACY
