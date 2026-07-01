using UnityEngine;
using UnityEngine.Rendering;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
#if UNITY_EDITOR
using UnityEditor;
#endif

[RequireComponent(typeof(MeshFilter))]
[RequireComponent(typeof(MeshRenderer))]
[ExecuteAlways]
public class VolumeMeshRenderer : MonoBehaviour, IVolumeRenderer
{
    public readonly struct RenderStats
    {
        public readonly double totalMs;
        public readonly double queueSetupMs;
        public readonly double chunkRebuildMs;
        public readonly double chunkLocalBuildMs;
        public readonly int rebuilt;
        public readonly int pending;
        public readonly int budget;
        public readonly int chunkLocalBuilds;
        public readonly bool hadDirtyBounds;
        public readonly bool canDoDirtyRebuild;
        public readonly bool fullRebuildRequested;
        public readonly int queuedDirtyChunks;
        public readonly Bounds dirtyBounds;

        public RenderStats(
            double totalMs,
            double queueSetupMs,
            double chunkRebuildMs,
            double chunkLocalBuildMs,
            int rebuilt,
            int pending,
            int budget,
            int chunkLocalBuilds,
            bool hadDirtyBounds = false,
            bool canDoDirtyRebuild = false,
            bool fullRebuildRequested = false,
            int queuedDirtyChunks = 0,
            Bounds dirtyBounds = default)
        {
            this.totalMs = totalMs;
            this.queueSetupMs = queueSetupMs;
            this.chunkRebuildMs = chunkRebuildMs;
            this.chunkLocalBuildMs = chunkLocalBuildMs;
            this.rebuilt = rebuilt;
            this.pending = pending;
            this.budget = budget;
            this.chunkLocalBuilds = chunkLocalBuilds;
            this.hadDirtyBounds = hadDirtyBounds;
            this.canDoDirtyRebuild = canDoDirtyRebuild;
            this.fullRebuildRequested = fullRebuildRequested;
            this.queuedDirtyChunks = queuedDirtyChunks;
            this.dirtyBounds = dirtyBounds;
        }
    }

    private MeshFilter meshFilter;
    private MeshRenderer meshRenderer;
    private Mesh mesh;
    private Transform _chunkRoot;
    private readonly List<MeshVolumeChunk> _chunks = new();
    private readonly List<Bounds> _lastChunkBounds = new();
    private readonly Queue<int> _pendingChunkQueue = new();
    private readonly HashSet<int> _pendingChunkSet = new();
    private readonly List<int> _dirtyChunkScratch = new();
    private readonly List<int> _neighborFrontierScratch = new();
    private readonly List<int> _neighborNextFrontierScratch = new();
    private VolumeModel _activeChunkModel;
    private VolumeSceneComposer _activeChunkComposer;
    private readonly List<Bounds> _activeChunkBounds = new();
    private IVolumeData _lastActiveVolumeData;
    private bool _chunkCycleActive;
    private readonly Stopwatch _chunkCycleTimer = new();
    private int _chunkCyclePasses;
    private int _chunkCycleRebuiltTotal;
    private double _chunkCycleChunkMsTotal;
    private double _chunkCycleChunkMsMax;
    private int _chunkCycleExpected;
    private bool _chunkCycleIsPreview;
    private double _lastPassChunkLocalBuildMs;
    private int _lastPassChunkLocalBuilds;
#if UNITY_EDITOR
    private bool _editorChunkUpdateRegistered;
#endif

    private readonly DualContouringVoxelMesher voxelMesher = new();
    private readonly DualMarchingCubesVoxelMesher dualMarchingCubesVoxelMesher = new();
    private readonly DualMarchingTetrahedraVoxelMesher dualMarchingTetrahedraVoxelMesher = new();
    private readonly SurfaceNetsVoxelMesher surfaceNetsVoxelMesher = new();
    private readonly DualContouringOctreeMesher octreeMesher = new();
    private readonly DualContouringFlatOctreeMesher flatOctreeMesher = new();
    private readonly DualMarchingCubesOctreeMesher dualMarchingCubesMesher = new();
    private readonly DualMarchingTetrahedraOctreeMesher dualMarchingTetrahedraMesher = new();
    private readonly SurfaceNetsOctreeMesher surfaceNetsOctreeMesher = new();
    private readonly OctreeChunkMesher sharedOctreeChunkMesher = new();
    [Header("Parallel Rendering")]
    public bool enableParallelChunkMeshing = true;
    [Min(1)]
    public int maxParallelChunkMeshingTasks = 0;
    [Header("Chunk-local Build")]
    public bool enableChunkLocalVolumeBuild = true;
    [Min(0f)]
    public float chunkLocalBuildHaloCells = 2f;

    public RenderStats LastRenderStats { get; private set; }

    /// <summary>Regenerates the single-mesh output for the model.</summary>
    public void Rebuild(VolumeModel model)
    {
        if (model == null)
            return;

        if (model.enableChunking)
            RebuildChunked(model);
        else
            RebuildSingle(model);
    }

    /// <summary>Clears the generated mesh and detaches it from the mesh filter.</summary>
    public void Clear()
    {
        ClearChunks();

        if (mesh != null)
            mesh.Clear();

        if (meshFilter == null)
            meshFilter = GetComponent<MeshFilter>();

        if (meshFilter != null)
            meshFilter.sharedMesh = null;
    }

    /// <summary>Builds the active volume data structure into one Unity mesh.</summary>
    public void RebuildSingle(VolumeModel model)
    {
        ClearChunks();
        EnsureSetup();
        ApplyMaterial(model.surfaceMaterial);

        if (meshRenderer != null)
            meshRenderer.enabled = true;

        mesh.indexFormat = IndexFormat.UInt32;
        mesh.Clear();

        switch (model.dataStructure)
        {
            case VolumeDataStructure.VoxelGrid:
                RebuildSingleVoxel(model);
                break;

            case VolumeDataStructure.Octree:
            case VolumeDataStructure.SparseVoxelOctree:
                RebuildSingleOctree(model);
                break;
        }

        LastRenderStats = new RenderStats(0d, 0d, 0d, 0d, 1, 0, 1, 0);
    }

    private void RebuildSingleVoxel(VolumeModel model)
    {
        switch (model.octreeMesherType)
        {
            case OctreeMesherType.DualMarchingCubes:
                dualMarchingCubesVoxelMesher.BuildMesh(model.voxelGridSampler.Volume, model.isoLevel, mesh);
                break;
            case OctreeMesherType.DualMarchingTetrahedra:
                dualMarchingTetrahedraVoxelMesher.BuildMesh(model.voxelGridSampler.Volume, model.isoLevel, mesh);
                break;
            case OctreeMesherType.SurfaceNets:
                surfaceNetsVoxelMesher.BuildMesh(model.voxelGridSampler.Volume, model.isoLevel, mesh);
                break;
            case OctreeMesherType.DualContouring:
            default:
                voxelMesher.BuildMesh(model.voxelGridSampler.Volume, model.isoLevel, mesh);
                break;
        }

        if (model.recalculateNormals)
            mesh.RecalculateNormals();
        if (model.recalculateBounds)
            mesh.RecalculateBounds();
    }

    private void RebuildSingleOctree(VolumeModel model)
    {
        switch (model.octreeMesherType)
        {
            case OctreeMesherType.DualMarchingCubes:
                dualMarchingCubesMesher.BuildMesh(model.GetActiveOctreeVolume(), model.isoLevel, mesh);
                break;
            case OctreeMesherType.DualMarchingTetrahedra:
                dualMarchingTetrahedraMesher.BuildMesh(model.GetActiveOctreeVolume(), model.isoLevel, mesh);
                break;
            case OctreeMesherType.SurfaceNets:
                surfaceNetsOctreeMesher.BuildMesh(model.GetActiveOctreeVolume(), model.isoLevel, mesh);
                break;

            case OctreeMesherType.DualContouring:
            default:
                OctreeVolume active = model.GetActiveOctreeVolume();
                if (active == null)
                    break;

                if (model.GetEffectiveStorageMode() == VolumeStorageMode.Flat)
                {
                    IFlatAdaptiveVolumeData flatActive = model.GetActiveFlatAdaptiveVolume();
                    if (flatActive == null)
                        break;

                    ConfigureFlatOctreeMesher(model);
                    flatOctreeMesher.BuildMesh(flatActive, model.isoLevel, mesh);
                }
                else
                {
                    ConfigureOctreeMesher(model);
                    octreeMesher.BuildMesh(active, model.isoLevel, mesh);
                }
                break;
        }
    }

    private void ConfigureOctreeMesher(VolumeModel model)
    {
        octreeMesher.enableDebugLog = false;
        octreeMesher.useQefVertices = model.GetEffectiveUseQefVertices();
        octreeMesher.qefVertexMode = model.GetEffectiveQefVertexMode();
        octreeMesher.qefBlendFactor = model.qefBlendFactor;
        octreeMesher.qefSnapEpsilon = model.qefSnapEpsilon;
        octreeMesher.qefMaxOffsetCells = model.qefMaxOffsetCells;
        octreeMesher.qefAxisSnapStrength = model.qefAxisSnapStrength;
        octreeMesher.qefEnableMultiHermite = model.GetEffectiveQefEnableMultiHermite();
        octreeMesher.qefHermiteSamplesPerEdge = model.qefHermiteSamplesPerEdge;
        octreeMesher.edgeRefinementSteps = model.GetEffectiveEdgeRefinementSteps();
    }

    private void ConfigureFlatOctreeMesher(VolumeModel model)
    {
        flatOctreeMesher.enableDebugLog = false;
        flatOctreeMesher.useQefVertices = model.GetEffectiveUseQefVertices();
        flatOctreeMesher.qefVertexMode = model.GetEffectiveQefVertexMode();
        flatOctreeMesher.qefBlendFactor = model.qefBlendFactor;
        flatOctreeMesher.qefSnapEpsilon = model.qefSnapEpsilon;
        flatOctreeMesher.qefMaxOffsetCells = model.qefMaxOffsetCells;
        flatOctreeMesher.qefAxisSnapStrength = model.qefAxisSnapStrength;
        flatOctreeMesher.qefEnableMultiHermite = model.GetEffectiveQefEnableMultiHermite();
        flatOctreeMesher.qefHermiteSamplesPerEdge = model.qefHermiteSamplesPerEdge;
    }

    public void RebuildChunked(VolumeModel model)
    {
        double queueSetupMs = 0d;
        double chunkRebuildMs = 0d;
        int rebuiltNow = 0;
        LastRenderStats = default;
#if UNITY_EDITOR
        Stopwatch phaseTimer = null;
        Stopwatch totalTimer = null;
        if (model != null && model.ShouldLogRebuildDuration())
        {
            phaseTimer = Stopwatch.StartNew();
            totalTimer = Stopwatch.StartNew();
        }
#endif
        EnsureSetup();

        mesh.Clear();

        if (meshFilter != null)
            meshFilter.sharedMesh = null;

        if (meshRenderer != null)
            meshRenderer.enabled = false;

        if (!model.TryGetChunkBounds(out List<Bounds> bounds))
            return;

        IVolumeData activeVolume = model.GetActiveVolume();
        bool volumeDataChanged = !ReferenceEquals(_lastActiveVolumeData, activeVolume);

        EnsureChunks(bounds.Count);
        SetSurfaceMaterial(model.surfaceMaterial);

        VolumeSceneComposer composer = model.GetComponent<VolumeSceneComposer>();

        if (composer == null)
            return;

        _activeChunkModel = model;
        _activeChunkComposer = composer;
        _activeChunkBounds.Clear();
        _activeChunkBounds.AddRange(bounds);

        bool hasDirtyBounds = model.TryConsumeDirtyBounds(out Bounds dirtyBounds);
        bool hasSameLayout = HasSameChunkLayout(bounds);
        bool canDoDirtyRebuild = hasDirtyBounds && hasSameLayout;
        Bounds expandedDirtyBounds = dirtyBounds;
        int queuedDirtyChunks = 0;

        if (canDoDirtyRebuild)
        {
            float halo = GetDirtyHaloSize(model);
            expandedDirtyBounds.Expand(Vector3.one * halo * 2f);
        }

        bool fullRebuildRequested = false;
        bool forceFullChunkRebuild = model.forceFullChunkRedraw || model.ConsumeForceFullChunkRenderOnce();

        if (forceFullChunkRebuild)
        {
            QueueAllChunks(bounds.Count);
            fullRebuildRequested = true;
        }
        else if (volumeDataChanged && !canDoDirtyRebuild)
        {
            QueueAllChunks(bounds.Count);
            fullRebuildRequested = true;
        }
        else if (!hasSameLayout)
        {
            QueueAllChunks(bounds.Count);
            fullRebuildRequested = true;
        }
        else if (canDoDirtyRebuild)
        {
            bool isFlatOctreeDc = IsFlatOctreeDualContouring(model);
            QueueDirtyChunks(bounds, expandedDirtyBounds);
            queuedDirtyChunks = _pendingChunkQueue.Count;
            if (!isFlatOctreeDc &&
                (model.dataStructure == VolumeDataStructure.Octree || model.dataStructure == VolumeDataStructure.SparseVoxelOctree) &&
                model.octreeExpandDirtyNeighbors)
            {
                ExpandQueuedChunks(bounds, Mathf.Max(1, model.octreeDirtyNeighborRings));
                queuedDirtyChunks = _pendingChunkQueue.Count;
            }
        }

        for (int i = 0; i < _chunks.Count && i < bounds.Count; i++)
        {
            MeshVolumeChunk chunk = _chunks[i];
            Bounds chunkBounds = bounds[i];

            chunk.name = $"MeshVolumeChunk_{i:000}";
            chunk.coreBounds = chunkBounds;
            chunk.buildBounds = chunkBounds;

        }

#if UNITY_EDITOR
        if (phaseTimer != null)
        {
            queueSetupMs = phaseTimer.Elapsed.TotalMilliseconds;
            phaseTimer.Restart();
        }
#endif
        if (fullRebuildRequested)
            ResetChunkCycleState();

        if (IsFlatOctreeDualContouring(model))
            WarmupFlatOctreeRuntimeCache(model);

        int rebuildBudget = GetChunkRebuildBudget(model, fullRebuildRequested);
        float rebuildTimeBudgetMs = GetChunkRebuildTimeBudgetMs(model, fullRebuildRequested);
        bool isPreviewPass = model != null && model.IsPreviewRebuild;
        StartChunkCycleIfNeeded(bounds.Count, isPreviewPass);
        rebuiltNow = RebuildQueuedChunks(rebuildBudget, rebuildTimeBudgetMs, out double passChunkMs);
        RecordChunkCyclePass(rebuiltNow, passChunkMs);
        TryFinalizeChunkCycle(model);
        UpdateEditorChunkDrainSubscription();
#if UNITY_EDITOR
        if (phaseTimer != null)
            chunkRebuildMs = phaseTimer.Elapsed.TotalMilliseconds;
#endif

        _lastActiveVolumeData = activeVolume;
        StoreChunkLayout(bounds);

#if UNITY_EDITOR
        if (totalTimer != null)
        {
            totalTimer.Stop();
            LastRenderStats = new RenderStats(
                totalTimer.Elapsed.TotalMilliseconds,
                queueSetupMs,
                chunkRebuildMs,
                _lastPassChunkLocalBuildMs,
                rebuiltNow,
                _pendingChunkQueue.Count,
                rebuildBudget,
                _lastPassChunkLocalBuilds,
                hasDirtyBounds,
                canDoDirtyRebuild,
                fullRebuildRequested,
                queuedDirtyChunks,
                dirtyBounds);
        }
#else
        LastRenderStats = new RenderStats(
            0d,
            0d,
            0d,
            _lastPassChunkLocalBuildMs,
            rebuiltNow,
            _pendingChunkQueue.Count,
            rebuildBudget,
            _lastPassChunkLocalBuilds,
            hasDirtyBounds,
            canDoDirtyRebuild,
            fullRebuildRequested,
            queuedDirtyChunks,
            dirtyBounds);
#endif
    }

    public void SetSurfaceMaterial(Material material)
    {
        EnsureSetup();
        ApplyMaterial(material);

        for (int i = 0; i < _chunks.Count; i++)
            _chunks[i].SetSurfaceMaterial(material);
    }

    private void ApplyMaterial(Material material)
    {
        if (meshRenderer == null)
            meshRenderer = GetComponent<MeshRenderer>();

        if (meshRenderer == null)
            return;

        if (material != null)
        {
            meshRenderer.sharedMaterial = material;
            return;
        }

        if (meshRenderer.sharedMaterial == null)
            meshRenderer.sharedMaterial = new Material(Shader.Find("Standard"));
    }

    private Transform ChunkRoot
    {
        get
        {
            if (_chunkRoot != null)
                return _chunkRoot;

            Transform existing = transform.Find("Chunks");

            if (existing != null)
            {
                _chunkRoot = existing;
                return _chunkRoot;
            }

            GameObject go = new GameObject("Chunks");
            go.transform.SetParent(transform, false);
            _chunkRoot = go.transform;
            return _chunkRoot;
        }
    }

    private void EnsureChunks(int needed)
    {
        needed = Mathf.Max(0, needed);

        _chunks.Clear();

        Transform root = ChunkRoot;

        for (int i = 0; i < root.childCount; i++)
        {
            MeshVolumeChunk chunk = root.GetChild(i).GetComponent<MeshVolumeChunk>();

            if (chunk != null)
                _chunks.Add(chunk);
        }

        while (_chunks.Count < needed)
        {
            GameObject go = new GameObject($"MeshVolumeChunk_{_chunks.Count:000}");
            go.transform.SetParent(root, false);
            MeshVolumeChunk chunk = go.AddComponent<MeshVolumeChunk>();
            _chunks.Add(chunk);
        }

        while (_chunks.Count > needed)
        {
            MeshVolumeChunk last = _chunks[^1];
            _chunks.RemoveAt(_chunks.Count - 1);

#if UNITY_EDITOR
            if (!Application.isPlaying)
                DestroyImmediate(last.gameObject);
            else
                Destroy(last.gameObject);
#else
            Destroy(last.gameObject);
#endif
        }
    }

    private void ClearChunks()
    {
        _chunks.Clear();
        _lastChunkBounds.Clear();
        _lastActiveVolumeData = null;
        _pendingChunkQueue.Clear();
        _pendingChunkSet.Clear();
        _activeChunkBounds.Clear();
        _activeChunkModel = null;
        _activeChunkComposer = null;
        UpdateEditorChunkDrainSubscription();

        Transform root = transform.Find("Chunks");

        if (root == null)
            return;

        for (int i = root.childCount - 1; i >= 0; i--)
        {
            Transform child = root.GetChild(i);
#if UNITY_EDITOR
            if (!Application.isPlaying)
                DestroyImmediate(child.gameObject);
            else
                Destroy(child.gameObject);
#else
            Destroy(child.gameObject);
#endif
        }
    }

    private bool HasSameChunkLayout(List<Bounds> bounds)
    {
        if (_lastChunkBounds.Count != bounds.Count)
            return false;

        const float epsilon = 1e-4f;

        for (int i = 0; i < bounds.Count; i++)
        {
            Bounds a = _lastChunkBounds[i];
            Bounds b = bounds[i];

            if ((a.center - b.center).sqrMagnitude > epsilon * epsilon)
                return false;

            if ((a.size - b.size).sqrMagnitude > epsilon * epsilon)
                return false;
        }

        return true;
    }

    private void StoreChunkLayout(List<Bounds> bounds)
    {
        _lastChunkBounds.Clear();
        _lastChunkBounds.AddRange(bounds);
    }

    private void QueueAllChunks(int chunkCount)
    {
        _pendingChunkQueue.Clear();
        _pendingChunkSet.Clear();

        for (int i = 0; i < chunkCount; i++)
        {
            _pendingChunkQueue.Enqueue(i);
            _pendingChunkSet.Add(i);
        }
    }

    private void QueueDirtyChunks(List<Bounds> bounds, Bounds dirtyBounds)
    {
        _dirtyChunkScratch.Clear();

        if (_pendingChunkQueue.Count == 0 && _pendingChunkSet.Count == 0)
        {
            for (int i = 0; i < bounds.Count; i++)
            {
                if (!bounds[i].Intersects(dirtyBounds))
                    continue;
                _dirtyChunkScratch.Add(i);
            }
        }
        else
        {
            for (int i = 0; i < bounds.Count; i++)
            {
                if (!bounds[i].Intersects(dirtyBounds))
                    continue;

                if (_pendingChunkSet.Contains(i))
                    continue;

                _dirtyChunkScratch.Add(i);
            }
        }

        if (_dirtyChunkScratch.Count == 0)
            return;

        Vector3 dirtyCenter = dirtyBounds.center;
        _dirtyChunkScratch.Sort((a, b) =>
        {
            float da = (bounds[a].center - dirtyCenter).sqrMagnitude;
            float db = (bounds[b].center - dirtyCenter).sqrMagnitude;
            return da.CompareTo(db);
        });

        for (int i = 0; i < _dirtyChunkScratch.Count; i++)
        {
            int idx = _dirtyChunkScratch[i];
            _pendingChunkQueue.Enqueue(idx);
            _pendingChunkSet.Add(idx);
        }
    }

    private void ExpandQueuedChunks(List<Bounds> bounds, int rings)
    {
        if (_pendingChunkQueue.Count == 0 || bounds == null || bounds.Count == 0)
            return;

        rings = Mathf.Max(1, rings);
        _neighborFrontierScratch.Clear();
        _neighborNextFrontierScratch.Clear();
        List<int> frontier = _neighborFrontierScratch;
        List<int> nextFrontier = _neighborNextFrontierScratch;
        frontier.AddRange(_pendingChunkSet);

        for (int ring = 0; ring < rings; ring++)
        {
            if (frontier.Count == 0)
                break;

            nextFrontier.Clear();

            for (int si = 0; si < frontier.Count; si++)
            {
                int i = frontier[si];

                if (i < 0 || i >= bounds.Count)
                    continue;

                Bounds b = bounds[i];

                for (int j = 0; j < bounds.Count; j++)
                {
                    if (_pendingChunkSet.Contains(j))
                        continue;

                    Bounds n = bounds[j];

                    if (SharesFaceOrEdge(b, n))
                    {
                        _pendingChunkQueue.Enqueue(j);
                        _pendingChunkSet.Add(j);
                        nextFrontier.Add(j);
                    }
                }
            }

            List<int> swap = frontier;
            frontier = nextFrontier;
            nextFrontier = swap;
        }
    }

    private static bool SharesFaceOrEdge(Bounds a, Bounds b)
    {
        const float eps = 1e-4f;

        bool touchX = Mathf.Abs(a.max.x - b.min.x) <= eps || Mathf.Abs(b.max.x - a.min.x) <= eps;
        bool touchY = Mathf.Abs(a.max.y - b.min.y) <= eps || Mathf.Abs(b.max.y - a.min.y) <= eps;
        bool touchZ = Mathf.Abs(a.max.z - b.min.z) <= eps || Mathf.Abs(b.max.z - a.min.z) <= eps;

        bool overlapX = a.min.x <= b.max.x + eps && a.max.x >= b.min.x - eps;
        bool overlapY = a.min.y <= b.max.y + eps && a.max.y >= b.min.y - eps;
        bool overlapZ = a.min.z <= b.max.z + eps && a.max.z >= b.min.z - eps;

        // neighbor in one axis while overlapping in the other two
        if (touchX && overlapY && overlapZ) return true;
        if (touchY && overlapX && overlapZ) return true;
        if (touchZ && overlapX && overlapY) return true;

        return false;
    }

    private float GetDirtyHaloSize(VolumeModel model)
    {
        if (model == null)
            return 0.01f;

        switch (model.dataStructure)
        {
            case VolumeDataStructure.VoxelGrid:
                {
                    VoxelGrid grid = model.voxelGridSampler.Volume;
                    if (grid != null)
                        return Mathf.Max(grid.CellSize.x, Mathf.Max(grid.CellSize.y, grid.CellSize.z)) * model.dirtyHaloMultiplier;
                    break;
                }

            case VolumeDataStructure.Octree:
            case VolumeDataStructure.SparseVoxelOctree:
                {
                    OctreeVolume octree = model.octreeSampler.Volume;
                    if (model.dataStructure == VolumeDataStructure.SparseVoxelOctree)
                        octree = model.GetActiveOctreeVolume();
                    if (octree != null)
                    {
                        float haloMultiplier = model.dirtyHaloMultiplier;
                        if (IsFlatOctreeDualContouring(model))
                            haloMultiplier *= 0.5f;
                        return Mathf.Max(octree.CellSize.x, Mathf.Max(octree.CellSize.y, octree.CellSize.z)) * haloMultiplier;
                    }
                    break;
                }
        }

        return 0.01f;
    }

    private void Update()
    {
        DrainPendingChunks();
    }

#if UNITY_EDITOR
    private void OnDisable()
    {
        UnregisterEditorChunkDrain();
    }

    private void EditorDrainPendingChunks()
    {
        if (this == null || Application.isPlaying)
        {
            UnregisterEditorChunkDrain();
            return;
        }

        DrainPendingChunks();
    }
#endif

    private void DrainPendingChunks()
    {
        if (_pendingChunkQueue.Count == 0)
        {
            UpdateDrainedChunkRenderStats(0, 0d, 0);
            UpdateEditorChunkDrainSubscription();
            return;
        }

        if (_activeChunkModel == null || _activeChunkComposer == null)
        {
            UpdateEditorChunkDrainSubscription();
            return;
        }

        bool previousPreview = _activeChunkModel.SetPreviewRebuildContext(_chunkCycleIsPreview);
        try
        {
            int rebuildBudget = GetChunkRebuildBudget(_activeChunkModel, fullRebuildRequested: false);
            int rebuiltNow = RebuildQueuedChunks(
                rebuildBudget,
                GetChunkRebuildTimeBudgetMs(_activeChunkModel, fullRebuildRequested: false),
                out double passChunkMs);
            UpdateDrainedChunkRenderStats(rebuiltNow, passChunkMs, rebuildBudget);
            RecordChunkCyclePass(rebuiltNow, passChunkMs);
            TryFinalizeChunkCycle(_activeChunkModel);
            UpdateEditorChunkDrainSubscription();
            RequestEditorChunkDrainTick();
        }
        finally
        {
            _activeChunkModel?.RestorePreviewRebuildContext(previousPreview);
        }
    }

    public void DrainPendingChunksImmediately()
    {
        if (_pendingChunkQueue.Count == 0 || _activeChunkModel == null || _activeChunkComposer == null)
        {
            UpdateDrainedChunkRenderStats(0, 0d, 0);
            UpdateEditorChunkDrainSubscription();
            return;
        }

        bool previousPreview = _activeChunkModel.SetPreviewRebuildContext(_chunkCycleIsPreview);
        try
        {
            while (_pendingChunkQueue.Count > 0)
            {
                int rebuildBudget = Mathf.Max(1, _pendingChunkQueue.Count);
                int rebuiltNow = RebuildQueuedChunks(rebuildBudget, 0f, out double passChunkMs);
                UpdateDrainedChunkRenderStats(rebuiltNow, passChunkMs, rebuildBudget);
                RecordChunkCyclePass(rebuiltNow, passChunkMs);
                if (rebuiltNow <= 0)
                    break;
            }

            TryFinalizeChunkCycle(_activeChunkModel);
            UpdateEditorChunkDrainSubscription();
        }
        finally
        {
            _activeChunkModel?.RestorePreviewRebuildContext(previousPreview);
        }
    }

    private void UpdateDrainedChunkRenderStats(int rebuiltNow, double passChunkMs, int rebuildBudget)
    {
        RenderStats previous = LastRenderStats;
        LastRenderStats = new RenderStats(
            previous.totalMs + passChunkMs,
            previous.queueSetupMs,
            previous.chunkRebuildMs + passChunkMs,
            previous.chunkLocalBuildMs + _lastPassChunkLocalBuildMs,
            previous.rebuilt + Mathf.Max(0, rebuiltNow),
            _pendingChunkQueue.Count,
            rebuildBudget,
            previous.chunkLocalBuilds + _lastPassChunkLocalBuilds,
            previous.hadDirtyBounds,
            previous.canDoDirtyRebuild,
            previous.fullRebuildRequested,
            previous.queuedDirtyChunks,
            previous.dirtyBounds);
    }

    private void UpdateEditorChunkDrainSubscription()
    {
#if UNITY_EDITOR
        if (Application.isPlaying || _pendingChunkQueue.Count == 0 || _activeChunkModel == null || _activeChunkComposer == null)
        {
            UnregisterEditorChunkDrain();
            return;
        }

        if (_editorChunkUpdateRegistered)
        {
            RequestEditorChunkDrainTick();
            return;
        }

        EditorApplication.update += EditorDrainPendingChunks;
        _editorChunkUpdateRegistered = true;
        RequestEditorChunkDrainTick();
#endif
    }

#if UNITY_EDITOR
    private static void RequestEditorChunkDrainTick()
    {
        if (Application.isPlaying)
            return;

        EditorApplication.QueuePlayerLoopUpdate();
        SceneView.RepaintAll();
    }
#else
    private static void RequestEditorChunkDrainTick()
    {
    }
#endif

    private void UnregisterEditorChunkDrain()
    {
        if (!_editorChunkUpdateRegistered)
            return;

#if UNITY_EDITOR
        EditorApplication.update -= EditorDrainPendingChunks;
#endif
        _editorChunkUpdateRegistered = false;
    }

    private int GetChunkRebuildBudget(VolumeModel model, bool fullRebuildRequested)
    {
        if (model == null)
            return 1;

        int pending = Mathf.Max(1, _pendingChunkQueue.Count);

        if (Application.isPlaying)
            return fullRebuildRequested ? pending : Mathf.Max(1, model.maxChunksPerRebuild);

        bool isFlatOctree = IsFlatOctreeDualContouring(model);
        bool isOctreeLike = model.dataStructure == VolumeDataStructure.Octree ||
                            model.dataStructure == VolumeDataStructure.SparseVoxelOctree;

        if (isFlatOctree)
        {
            if (fullRebuildRequested)
                return pending;
            if (pending <= 32)
                return pending;
            return Mathf.Min(pending, Mathf.Max(12, model.maxChunksPerRebuild * 3));
        }

        // When backlog is high in editor (e.g. maxDepth 8), drain faster to reduce visible lag.
        if (!Application.isPlaying && isOctreeLike && pending > 32)
            return Mathf.Min(pending, Mathf.Max(16, model.maxChunksPerRebuild * 3));

        return pending;
    }

    private float GetChunkRebuildTimeBudgetMs(VolumeModel model, bool fullRebuildRequested)
    {
        if (model == null || Application.isPlaying)
            return -1f;

        if (model.dataStructure == VolumeDataStructure.VoxelGrid)
            return -1f;

        if (IsFlatOctreeDualContouring(model))
        {
            if (fullRebuildRequested)
                return -1f;
            return 28f;
        }

        bool isOctreeLike = model.dataStructure == VolumeDataStructure.Octree ||
                            model.dataStructure == VolumeDataStructure.SparseVoxelOctree;
        if (!Application.isPlaying && isOctreeLike && _pendingChunkQueue.Count > 32)
            return 36f;

        return 24f;
    }

    private static bool IsFlatOctreeDualContouring(VolumeModel model)
    {
        if (model == null)
            return false;

        return (model.dataStructure == VolumeDataStructure.Octree || model.dataStructure == VolumeDataStructure.SparseVoxelOctree) &&
               model.GetEffectiveStorageMode() == VolumeStorageMode.Flat &&
               model.octreeMesherType == OctreeMesherType.DualContouring;
    }

    private static void WarmupFlatOctreeRuntimeCache(VolumeModel model)
    {
        IFlatAdaptiveVolumeData flatVolume = model.GetActiveFlatAdaptiveVolume();
        FlatOctreeLayout layout = flatVolume?.GetFlatLayout(includeCornerValues: true);
        layout?.EnsureRuntimeCache();
    }

    private int RebuildQueuedChunks(int budget, float timeBudgetMs, out double passChunkMs)
    {
        _lastPassChunkLocalBuildMs = 0d;
        _lastPassChunkLocalBuilds = 0;
        Stopwatch passTimer = Stopwatch.StartNew();
        if (_activeChunkModel == null || _activeChunkComposer == null)
        {
            passChunkMs = passTimer.Elapsed.TotalMilliseconds;
            return 0;
        }

        if (ShouldUseParallelChunkMeshing(_activeChunkModel))
        {
            return RebuildQueuedChunksParallel(budget, timeBudgetMs, passTimer, out passChunkMs);
        }

        int rebuilt = 0;
        Stopwatch timeBudgetWatch = null;
        if (timeBudgetMs > 0f)
            timeBudgetWatch = Stopwatch.StartNew();

        while (rebuilt < budget && _pendingChunkQueue.Count > 0)
        {
            int idx = _pendingChunkQueue.Dequeue();
            _pendingChunkSet.Remove(idx);

            if (idx < 0 || idx >= _chunks.Count || idx >= _activeChunkBounds.Count)
                continue;

            MeshVolumeChunk chunk = _chunks[idx];
            Bounds chunkBounds = _activeChunkBounds[idx];

            chunk.name = $"MeshVolumeChunk_{idx:000}";
            Bounds buildBounds = GetChunkLocalBuildBounds(_activeChunkModel, chunkBounds);
            chunk.coreBounds = chunkBounds;
            chunk.buildBounds = buildBounds;
            if (ShouldUseChunkLocalVolumeBuild(_activeChunkModel) &&
                TryBuildChunkLocalMeshData(_activeChunkModel, _activeChunkComposer, chunkBounds, buildBounds, out MeshData meshData, out double localBuildMs))
            {
                _lastPassChunkLocalBuildMs += localBuildMs;
                _lastPassChunkLocalBuilds++;
                chunk.ApplyMeshData(meshData, _activeChunkModel);
            }
            else
            {
                chunk.Rebuild(_activeChunkModel, _activeChunkComposer, sharedOctreeChunkMesher);
            }
            rebuilt++;

            if (timeBudgetWatch != null && timeBudgetWatch.Elapsed.TotalMilliseconds >= timeBudgetMs)
                break;
        }

        passTimer.Stop();
        passChunkMs = passTimer.Elapsed.TotalMilliseconds;
        return rebuilt;
    }

    private bool ShouldUseParallelChunkMeshing(VolumeModel model)
    {
        if (!enableParallelChunkMeshing || model == null)
            return false;

        if (!IsFlatOctreeDualContouring(model))
            return false;

        // Only parallelize full passes. Partial passes can briefly show old/new chunk
        // boundaries together during visual benchmarks, which reads as geometry spikes.
        int pending = _pendingChunkQueue.Count;
        return pending > 0 &&
               pending <= GetChunkRebuildBudget(model, fullRebuildRequested: false) &&
               pending <= GetMaxParallelChunkMeshingTasks();
    }

    private bool ShouldUseChunkLocalVolumeBuild(VolumeModel model)
    {
        return enableChunkLocalVolumeBuild &&
               model != null &&
               model.dataStructure == VolumeDataStructure.Octree &&
               IsFlatOctreeDualContouring(model);
    }

    public bool CanBuildDirtyChunksLocally(VolumeModel model)
    {
        return ShouldUseChunkLocalVolumeBuild(model);
    }

    private int GetMaxParallelChunkMeshingTasks()
    {
        return maxParallelChunkMeshingTasks > 0
            ? maxParallelChunkMeshingTasks
            : Mathf.Max(1, System.Environment.ProcessorCount - 1);
    }

    private readonly struct ChunkBuildRequest
    {
        public readonly int Index;
        public readonly Bounds Bounds;
        public readonly Bounds BuildBounds;
        public readonly MeshVolumeChunk Chunk;

        public ChunkBuildRequest(int index, Bounds bounds, Bounds buildBounds, MeshVolumeChunk chunk)
        {
            Index = index;
            Bounds = bounds;
            BuildBounds = buildBounds;
            Chunk = chunk;
        }
    }

    private sealed class ChunkBuildResult
    {
        public int Index;
        public Bounds Bounds;
        public MeshData MeshData;
        public bool Success;
        public double LocalBuildMs;
        public bool UsedLocalBuild;
    }

    private int RebuildQueuedChunksParallel(int budget, float timeBudgetMs, Stopwatch passTimer, out double passChunkMs)
    {
        _lastPassChunkLocalBuildMs = 0d;
        _lastPassChunkLocalBuilds = 0;
        int maxTasks = GetMaxParallelChunkMeshingTasks();
        int batchLimit = Mathf.Min(budget, maxTasks);
        if (batchLimit <= 0)
        {
            passChunkMs = passTimer.Elapsed.TotalMilliseconds;
            return 0;
        }

        bool useChunkLocalBuild = ShouldUseChunkLocalVolumeBuild(_activeChunkModel);
        OctreeVolume volume = useChunkLocalBuild ? null : _activeChunkModel.GetActiveOctreeVolume();
        if (volume == null)
        {
            if (!useChunkLocalBuild)
            {
                passChunkMs = passTimer.Elapsed.TotalMilliseconds;
                return 0;
            }
        }

        IFlatAdaptiveVolumeData flatVolume = null;
        if (!useChunkLocalBuild)
        {
            flatVolume = _activeChunkModel.GetActiveFlatAdaptiveVolume() ?? volume;
            flatVolume.GetFlatLayout(includeCornerValues: true)?.EnsureRuntimeCache();
        }
        OctreeChunkMesher.FlatDualContouringChunkSettings meshingSettings = new(_activeChunkModel);

        List<ChunkBuildRequest> requests = new(batchLimit);
        while (requests.Count < batchLimit && _pendingChunkQueue.Count > 0)
        {
            int idx = _pendingChunkQueue.Dequeue();
            _pendingChunkSet.Remove(idx);

            if (idx < 0 || idx >= _chunks.Count || idx >= _activeChunkBounds.Count)
                continue;

            MeshVolumeChunk chunk = _chunks[idx];
            Bounds chunkBounds = _activeChunkBounds[idx];
            Bounds buildBounds = GetChunkLocalBuildBounds(_activeChunkModel, chunkBounds);

            chunk.name = $"MeshVolumeChunk_{idx:000}";
            chunk.coreBounds = chunkBounds;
            chunk.buildBounds = buildBounds;
            requests.Add(new ChunkBuildRequest(idx, chunkBounds, buildBounds, chunk));
        }

        if (requests.Count == 0)
        {
            passChunkMs = passTimer.Elapsed.TotalMilliseconds;
            return 0;
        }

        ChunkBuildResult[] results = new ChunkBuildResult[requests.Count];
        ParallelOptions parallelOptions = new()
        {
            MaxDegreeOfParallelism = Mathf.Max(1, maxTasks)
        };

        Parallel.For(0, requests.Count, parallelOptions, i =>
        {
            ChunkBuildRequest request = requests[i];
            try
            {
                MeshData meshData;
                if (useChunkLocalBuild)
                {
                    if (!TryBuildChunkLocalMeshData(_activeChunkModel, _activeChunkComposer, request.Bounds, request.BuildBounds, out meshData, out double localBuildMs))
                        throw new System.InvalidOperationException("Chunk-local volume build failed.");

                    results[i] = new ChunkBuildResult
                    {
                        Index = request.Index,
                        Bounds = request.Bounds,
                        MeshData = meshData,
                        Success = true,
                        LocalBuildMs = localBuildMs,
                        UsedLocalBuild = true
                    };
                    return;
                }
                else
                {
                    meshData = OctreeChunkMesher.BuildFlatDualContouringChunkData(
                        meshingSettings,
                        flatVolume,
                        request.Bounds);
                }
                results[i] = new ChunkBuildResult
                {
                    Index = request.Index,
                    Bounds = request.Bounds,
                    MeshData = meshData,
                    Success = true
                };
            }
            catch
            {
                results[i] = new ChunkBuildResult
                {
                    Index = request.Index,
                    Bounds = request.Bounds,
                    MeshData = null,
                    Success = false
                };
            }
        });

        int rebuilt = 0;
        for (int i = 0; i < results.Length; i++)
        {
            ChunkBuildResult result = results[i];
            if (result.Index < 0 || result.Index >= _chunks.Count)
                continue;

            MeshVolumeChunk chunk = _chunks[result.Index];
            if (result.Success)
            {
                if (result.UsedLocalBuild)
                {
                    _lastPassChunkLocalBuildMs += result.LocalBuildMs;
                    _lastPassChunkLocalBuilds++;
                }
                chunk.ApplyMeshData(result.MeshData, _activeChunkModel);
            }
            else
                chunk.Rebuild(_activeChunkModel, _activeChunkComposer, sharedOctreeChunkMesher);
            rebuilt++;
        }

        passTimer.Stop();
        passChunkMs = passTimer.Elapsed.TotalMilliseconds;
        return rebuilt;
    }

    private Bounds GetChunkLocalBuildBounds(VolumeModel model, Bounds coreBounds)
    {
        Bounds buildBounds = coreBounds;
        float cellSize = GetActiveOctreeCellSize(model);
        float halo = Mathf.Max(0f, chunkLocalBuildHaloCells) * Mathf.Max(cellSize, 0.0001f);
        if (halo > 0f)
            buildBounds.Expand(halo * 2f);
        return buildBounds;
    }

    private static float GetActiveOctreeCellSize(VolumeModel model)
    {
        OctreeVolume volume = model != null ? model.GetActiveOctreeVolume() : null;
        if (volume == null)
            return 0.01f;

        Vector3 cellSize = volume.CellSize;
        return Mathf.Max(cellSize.x, Mathf.Max(cellSize.y, cellSize.z));
    }

    private readonly struct ChunkLocalBuildGrid
    {
        public readonly Bounds Bounds;
        public readonly int MaxDepth;

        public ChunkLocalBuildGrid(Bounds bounds, int maxDepth)
        {
            Bounds = bounds;
            MaxDepth = maxDepth;
        }
    }

    private static ChunkLocalBuildGrid GetChunkLocalBuildGrid(
        FlatOctreeVolumeBuilder template,
        OctreeVolume globalVolume,
        Bounds requestedBounds)
    {
        if (template == null || globalVolume == null)
            return new ChunkLocalBuildGrid(requestedBounds, template != null ? Mathf.Max(1, template.maxDepth) : 1);

        Vector3 origin = globalVolume.GridOrigin;
        Vector3 cell = globalVolume.CellSize;
        cell.x = Mathf.Max(0.000001f, Mathf.Abs(cell.x));
        cell.y = Mathf.Max(0.000001f, Mathf.Abs(cell.y));
        cell.z = Mathf.Max(0.000001f, Mathf.Abs(cell.z));

        Vector3 min = requestedBounds.min;
        Vector3 max = requestedBounds.max;
        Vector3Int minCoord = new(
            Mathf.FloorToInt((min.x - origin.x) / cell.x),
            Mathf.FloorToInt((min.y - origin.y) / cell.y),
            Mathf.FloorToInt((min.z - origin.z) / cell.z));
        Vector3Int maxCoord = new(
            Mathf.CeilToInt((max.x - origin.x) / cell.x),
            Mathf.CeilToInt((max.y - origin.y) / cell.y),
            Mathf.CeilToInt((max.z - origin.z) / cell.z));

        Vector3Int requestedCells = new(
            Mathf.Max(1, maxCoord.x - minCoord.x),
            Mathf.Max(1, maxCoord.y - minCoord.y),
            Mathf.Max(1, maxCoord.z - minCoord.z));
        int requiredCells = Mathf.Max(requestedCells.x, Mathf.Max(requestedCells.y, requestedCells.z));
        int maxDepth = Mathf.Clamp(Mathf.CeilToInt(Mathf.Log(requiredCells, 2f)), 1, Mathf.Max(1, template.maxDepth));
        int cellCount = 1 << maxDepth;

        Vector3 alignedMin = new(
            origin.x + minCoord.x * cell.x,
            origin.y + minCoord.y * cell.y,
            origin.z + minCoord.z * cell.z);
        Vector3 alignedSize = new(
            cell.x * cellCount,
            cell.y * cellCount,
            cell.z * cellCount);

        return new ChunkLocalBuildGrid(
            new Bounds(alignedMin + alignedSize * 0.5f, alignedSize),
            maxDepth);
    }

    private static bool TryBuildChunkLocalMeshData(
        VolumeModel model,
        IScalarFieldSource source,
        Bounds coreBounds,
        Bounds buildBounds,
        out MeshData meshData,
        out double localBuildMs)
    {
        meshData = null;
        localBuildMs = 0d;
        if (model == null || source == null || model.octreeSampler == null || model.octreeSampler.flatBuilder == null)
            return false;

        FlatOctreeVolumeBuilder template = model.octreeSampler.flatBuilder;
        OctreeVolume globalVolume = model.GetActiveOctreeVolume();
        if (globalVolume == null)
            return false;

        ChunkLocalBuildGrid buildGrid = GetChunkLocalBuildGrid(template, globalVolume, buildBounds);

        FlatOctreeVolumeBuilder builder = new()
        {
            center = buildGrid.Bounds.center,
            size = buildGrid.Bounds.size,
            boundsPadding = 0f,
            maxDepth = buildGrid.MaxDepth,
            minDepth = Mathf.Clamp(template.minDepth, 0, buildGrid.MaxDepth),
            suppressBuildLog = true,
            edgeRefinementSteps = template.edgeRefinementSteps,
            sampleCacheDirtyPaddingCells = 0f,
            profileRecursiveParts = false,
            useBurstPreFill = false,
            useBurstFrontier = false
        };

        Stopwatch buildTimer = Stopwatch.StartNew();
        OctreeVolume localVolume = builder.Build(source);
        buildTimer.Stop();
        localBuildMs = buildTimer.Elapsed.TotalMilliseconds;
        IFlatAdaptiveVolumeData flatVolume = localVolume as IFlatAdaptiveVolumeData;
        if (flatVolume == null)
            return false;

        flatVolume.GetFlatLayout(includeCornerValues: true)?.EnsureRuntimeCache();
        OctreeChunkMesher.FlatDualContouringChunkSettings settings = new(model);
        meshData = OctreeChunkMesher.BuildFlatDualContouringChunkData(settings, flatVolume, coreBounds);
        return true;
    }

    private void StartChunkCycleIfNeeded(int expectedChunks, bool isPreview)
    {
        if (_chunkCycleActive)
            return;

        _chunkCycleActive = true;
        _chunkCycleExpected = Mathf.Max(0, expectedChunks);
        _chunkCyclePasses = 0;
        _chunkCycleRebuiltTotal = 0;
        _chunkCycleChunkMsTotal = 0d;
        _chunkCycleChunkMsMax = 0d;
        _chunkCycleIsPreview = isPreview;
        _chunkCycleTimer.Restart();
    }

    private void ResetChunkCycleState()
    {
        _chunkCycleActive = false;
        _chunkCycleExpected = 0;
        _chunkCyclePasses = 0;
        _chunkCycleRebuiltTotal = 0;
        _chunkCycleChunkMsTotal = 0d;
        _chunkCycleChunkMsMax = 0d;
        _chunkCycleIsPreview = false;
        _chunkCycleTimer.Reset();
    }

    private void RecordChunkCyclePass(int rebuilt, double passChunkMs)
    {
        if (!_chunkCycleActive)
            return;

        _chunkCyclePasses++;
        _chunkCycleRebuiltTotal += Mathf.Max(0, rebuilt);
        _chunkCycleChunkMsTotal += passChunkMs;
        if (passChunkMs > _chunkCycleChunkMsMax)
            _chunkCycleChunkMsMax = passChunkMs;
    }

    private void TryFinalizeChunkCycle(VolumeModel model)
    {
        if (!_chunkCycleActive || _pendingChunkQueue.Count > 0 || model == null || !model.logRebuildDuration)
            return;

        _chunkCycleTimer.Stop();
        _chunkCycleActive = false;
        _chunkCycleExpected = 0;
        _chunkCyclePasses = 0;
        _chunkCycleRebuiltTotal = 0;
        _chunkCycleChunkMsTotal = 0d;
        _chunkCycleChunkMsMax = 0d;
        _chunkCycleIsPreview = false;
    }

    /// <summary>Initializes required components, mesh, and fallback material.</summary>
    private void EnsureSetup()
    {
        if (meshFilter == null)
            meshFilter = GetComponent<MeshFilter>();

        if (meshRenderer == null)
            meshRenderer = GetComponent<MeshRenderer>();

        if (mesh == null || mesh.indexFormat != IndexFormat.UInt32)
        {
            mesh = new Mesh();
            mesh.name = "Volume Mesh";
            mesh.indexFormat = IndexFormat.UInt32;
        }

        // WICHTIG:
        // Nach Clear() kann sharedMesh null sein.
        // Deshalb immer wieder zuweisen.
        if (meshFilter.sharedMesh != mesh)
            meshFilter.sharedMesh = mesh;
    }
}


