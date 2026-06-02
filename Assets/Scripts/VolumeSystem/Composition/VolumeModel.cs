#if UNITY_EDITOR
using UnityEditor;
using UnityEditorInternal;
#endif

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

[ExecuteAlways]
[DisallowMultipleComponent]
[RequireComponent(typeof(VolumeSceneComposer))]
public class VolumeModel : MonoBehaviour
{
    private readonly System.Collections.Generic.List<Bounds> _chunkBoundsCache = new();
    private bool _hasDirtyBounds;
    private Bounds _dirtyBounds;
    private bool _isPreviewRebuild;

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
        qefRobustScale = Mathf.Max(0.1f, qefRobustScale);
        qefIrlsIterations = Mathf.Max(1, qefIrlsIterations);
        qefAnisotropicStrength = Mathf.Max(0f, qefAnisotropicStrength);
        qefSurfaceWeight = Mathf.Max(0f, qefSurfaceWeight);
        qefEdgeWeight = Mathf.Max(0f, qefEdgeWeight);
        qefCornerWeight = Mathf.Max(0f, qefCornerWeight);
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
    [Min(0f)]
    public float previewInteractionHoldSeconds = 0.2f;

    [Header("Debug")]
    public bool drawChildGizmos = true;
    public bool drawChunkGizmosAlways = false;
    public bool renderOctreeDebugCubes = false;
    public bool logChunkRebuildStats = false;
    public bool logRebuildDuration = true;

    [Header("Add Object")]
    public VolumeShapeType shapeToAdd = VolumeShapeType.Sphere;
    public VolumeOperationRole roleToAdd = VolumeOperationRole.Add;

#if UNITY_EDITOR
    private double _lastInteractiveEditTime = double.NegativeInfinity;
    private bool _finalizePreviewRebuildQueued;
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
        if (logRebuildDuration)
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
                activeBuilder.suppressBuildLog = !ShouldLogRebuildDuration();
                activeBuilder.useQefVertices = useQefVertices;
                activeBuilder.qefVertexMode = qefVertexMode;
                activeBuilder.qefBlendFactor = qefBlendFactor;
                activeBuilder.qefSnapEpsilon = qefSnapEpsilon;
                activeBuilder.qefMaxOffsetCells = qefMaxOffsetCells;
                activeBuilder.qefAxisSnapStrength = qefAxisSnapStrength;
                activeBuilder.qefEnableMultiHermite = qefEnableMultiHermite;
                activeBuilder.qefHermiteSamplesPerEdge = qefHermiteSamplesPerEdge;
                activeBuilder.edgeRefinementSteps = edgeRefinementSteps;
                activeBuilder.qefRobustKernel = qefRobustKernel;
                activeBuilder.qefRobustScale = qefRobustScale;
                activeBuilder.qefIrlsIterations = qefIrlsIterations;
                activeBuilder.qefUseAnisotropicRegularization = qefUseAnisotropicRegularization;
                activeBuilder.qefAnisotropicStrength = qefAnisotropicStrength;
                activeBuilder.qefFeatureWeightMode = qefFeatureWeightMode;
                activeBuilder.qefSurfaceWeight = qefSurfaceWeight;
                activeBuilder.qefEdgeWeight = qefEdgeWeight;
                activeBuilder.qefCornerWeight = qefCornerWeight;

                bool hasInitializedVolume = dataStructure == VolumeDataStructure.Octree
                    ? octreeSampler.Volume != null
                    : sparseVoxelOctreeSampler.Volume != null;

                if (!hasInitializedVolume)
                {
                    rebuildCause = dataStructure == VolumeDataStructure.Octree
                        ? "octree-full-init"
                        : "svo-full-init";
                }

                bool canAttemptIncrementalOctreeUpdate = hasDirtyBounds && !usingPreviewDepth;
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
                        activeBuilder.suppressBuildLog = !ShouldLogRebuildDuration();
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
                    octreeSampler.RebuildVolume(source);
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
                activeBuilder.suppressBuildLog = !logRebuildDuration;
                ClearDirtyBounds();
                break;
        }
#if UNITY_EDITOR
        if (phaseStopwatch != null)
        {
            volumeBuildMs = phaseStopwatch.Elapsed.TotalMilliseconds;
            phaseStopwatch.Restart();
        }
#endif

        if (!enableChunking)
            ClearDirtyBounds();

        RenderOutput.Rebuild(this);
#if UNITY_EDITOR
        if (phaseStopwatch != null)
            renderMs = phaseStopwatch.Elapsed.TotalMilliseconds;
#endif

#if UNITY_EDITOR
        if (rebuildStopwatch != null)
        {
            rebuildStopwatch.Stop();
            if (ShouldLogRebuildDuration())
            {
                Debug.Log(
                    $"VolumeModel Rebuild [{GetPipelineDebugLabel()}]: total={rebuildStopwatch.Elapsed.TotalMilliseconds:F2} ms, composition={compositionMs:F2} ms, volumeBuild={volumeBuildMs:F2} ms, render={renderMs:F2} ms, cause={rebuildCause}, hasDirty={hasDirtyBounds}, incremental={usedIncrementalUpdate}");
            }
        }
#endif
        _isPreviewRebuild = previousPreviewRebuild;
    }

    public string GetPipelineDebugLabel()
    {
        return $"{dataStructure}/{storageMode}/{octreeMesherType}";
    }

    public bool IsPreviewRebuild => _isPreviewRebuild;

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

    private void ClearDirtyBounds()
    {
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
        RebuildModel();
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
