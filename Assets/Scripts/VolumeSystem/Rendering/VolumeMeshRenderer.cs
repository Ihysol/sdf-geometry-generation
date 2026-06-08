using UnityEngine;
using UnityEngine.Rendering;
using System.Collections.Generic;
using System.Diagnostics;
#if UNITY_EDITOR
using UnityEditor;
#endif

[RequireComponent(typeof(MeshFilter))]
[RequireComponent(typeof(MeshRenderer))]
[ExecuteAlways]
public class VolumeMeshRenderer : MonoBehaviour, IVolumeRenderer
{
    private MeshFilter meshFilter;
    private MeshRenderer meshRenderer;
    private Mesh mesh;
    private Transform _chunkRoot;
    private readonly List<MeshVolumeChunk> _chunks = new();
    private readonly List<Bounds> _lastChunkBounds = new();
    private readonly Queue<int> _pendingChunkQueue = new();
    private readonly HashSet<int> _pendingChunkSet = new();
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

#if UNITY_EDITOR
        if (model != null && model.ShouldLogChunkRebuildStats())
            UnityEngine.Debug.Log($"VolumeMeshRenderer: vertex count = {mesh.vertexCount}, indexFormat = {mesh.indexFormat}");
#endif
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
#if UNITY_EDITOR
        Stopwatch phaseTimer = null;
        Stopwatch totalTimer = null;
        double queueSetupMs = 0d;
        double chunkRebuildMs = 0d;
        int rebuiltNow = 0;
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

        if (canDoDirtyRebuild)
        {
            float halo = GetDirtyHaloSize(model);
            expandedDirtyBounds.Expand(Vector3.one * halo * 2f);
        }

        bool fullRebuildRequested = false;

        if (model.forceFullChunkRedraw)
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
            bool isVoxelGrid = model.dataStructure == VolumeDataStructure.VoxelGrid;
            if (!Application.isPlaying && isFlatOctreeDc)
            {
                // Keep editor interaction responsive for flat mode:
                // drop stale pending work and prioritize the latest dirty region.
                _pendingChunkQueue.Clear();
                _pendingChunkSet.Clear();
            }
            else if (!Application.isPlaying && isVoxelGrid)
            {
                // Voxel chunks can be expensive per chunk; avoid stale backlog in editor.
                _pendingChunkQueue.Clear();
                _pendingChunkSet.Clear();
            }

            QueueDirtyChunks(bounds, expandedDirtyBounds);
            if (!isFlatOctreeDc &&
                (model.dataStructure == VolumeDataStructure.Octree || model.dataStructure == VolumeDataStructure.SparseVoxelOctree) &&
                model.octreeExpandDirtyNeighbors)
            {
                ExpandQueuedChunks(bounds, Mathf.Max(1, model.octreeDirtyNeighborRings));
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
            if (model.ShouldLogRebuildDuration())
            {
                UnityEngine.Debug.Log(
                    $"VolumeMeshRenderer Chunked [{model.GetPipelineDebugLabel()}]: total={totalTimer.Elapsed.TotalMilliseconds:F2} ms, queueSetup={queueSetupMs:F2} ms, chunkRebuild={chunkRebuildMs:F2} ms, rebuilt={rebuiltNow}, pending={_pendingChunkQueue.Count}, budget={rebuildBudget}, refinementSteps={model.GetEffectiveEdgeRefinementSteps()}");
            }
        }
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
        List<int> dirtyIndices = new();

        if (_pendingChunkQueue.Count == 0 && _pendingChunkSet.Count == 0)
        {
            for (int i = 0; i < bounds.Count; i++)
            {
                if (!bounds[i].Intersects(dirtyBounds))
                    continue;
                dirtyIndices.Add(i);
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

                dirtyIndices.Add(i);
            }
        }

        if (dirtyIndices.Count == 0)
            return;

        Vector3 dirtyCenter = dirtyBounds.center;
        dirtyIndices.Sort((a, b) =>
        {
            float da = (bounds[a].center - dirtyCenter).sqrMagnitude;
            float db = (bounds[b].center - dirtyCenter).sqrMagnitude;
            return da.CompareTo(db);
        });

        for (int i = 0; i < dirtyIndices.Count; i++)
        {
            int idx = dirtyIndices[i];
            _pendingChunkQueue.Enqueue(idx);
            _pendingChunkSet.Add(idx);
        }
    }

    private void ExpandQueuedChunks(List<Bounds> bounds, int rings)
    {
        if (_pendingChunkQueue.Count == 0 || bounds == null || bounds.Count == 0)
            return;

        rings = Mathf.Max(1, rings);

        for (int ring = 0; ring < rings; ring++)
        {
            List<int> seed = new List<int>(_pendingChunkSet);

            for (int si = 0; si < seed.Count; si++)
            {
                int i = seed[si];

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
                    }
                }
            }
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
            int rebuiltNow = RebuildQueuedChunks(
                GetChunkRebuildBudget(_activeChunkModel, fullRebuildRequested: false),
                GetChunkRebuildTimeBudgetMs(_activeChunkModel, fullRebuildRequested: false),
                out double passChunkMs);
            RecordChunkCyclePass(rebuiltNow, passChunkMs);
            TryFinalizeChunkCycle(_activeChunkModel);
            UpdateEditorChunkDrainSubscription();
        }
        finally
        {
            _activeChunkModel?.RestorePreviewRebuildContext(previousPreview);
        }
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
            return;

        EditorApplication.update += EditorDrainPendingChunks;
        _editorChunkUpdateRegistered = true;
#endif
    }

#if UNITY_EDITOR
    private void UnregisterEditorChunkDrain()
    {
        if (!_editorChunkUpdateRegistered)
            return;

        EditorApplication.update -= EditorDrainPendingChunks;
        _editorChunkUpdateRegistered = false;
    }
#endif

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
        Stopwatch passTimer = Stopwatch.StartNew();
        if (_activeChunkModel == null || _activeChunkComposer == null)
        {
            passChunkMs = passTimer.Elapsed.TotalMilliseconds;
            return 0;
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
            chunk.coreBounds = chunkBounds;
            chunk.buildBounds = chunkBounds;
            chunk.Rebuild(_activeChunkModel, _activeChunkComposer, sharedOctreeChunkMesher);
            rebuilt++;

            if (timeBudgetWatch != null && timeBudgetWatch.Elapsed.TotalMilliseconds >= timeBudgetMs)
                break;
        }

#if UNITY_EDITOR
        if (_activeChunkModel != null && _activeChunkModel.ShouldLogChunkRebuildStats())
            UnityEngine.Debug.Log($"Chunk rebuild: rebuilt={rebuilt}, pending={_pendingChunkQueue.Count}, budget={budget}");
#endif
        passTimer.Stop();
        passChunkMs = passTimer.Elapsed.TotalMilliseconds;
        return rebuilt;
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
        double avg = _chunkCyclePasses > 0 ? _chunkCycleChunkMsTotal / _chunkCyclePasses : 0d;
        if (!_chunkCycleIsPreview && model.ShouldLogRebuildDuration())
        {
            UnityEngine.Debug.Log(
                $"VolumeMeshRenderer Chunked Final [{model.GetPipelineDebugLabel()}] | work(expected={_chunkCycleExpected}, rebuilt={_chunkCycleRebuiltTotal}, refinementSteps={model.GetEffectiveEdgeRefinementSteps()})\n" +
                $"timing(total={_chunkCycleTimer.Elapsed.TotalMilliseconds:F2} ms, chunk={_chunkCycleChunkMsTotal:F2} ms, passes={_chunkCyclePasses}, avg={avg:F2} ms, max={_chunkCycleChunkMsMax:F2} ms)");
        }

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


