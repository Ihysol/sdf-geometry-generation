using UnityEngine;
using UnityEngine.Rendering;
using System.Collections.Generic;

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

    private readonly DualContouringVoxelMesher voxelMesher = new();
    private readonly DualContouringOctreeMesher octreeMesher = new();

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
                {
                    MeshData meshData = voxelMesher.BuildMeshData(
                        model.voxelGridSampler.Volume,
                        model.isoLevel
                    );

                    ApplyMeshData(meshData, model);
                    break;
                }

            case VolumeDataStructure.Octree:
                {
                    octreeMesher.isoLevel = model.isoLevel;
                    octreeMesher.useQefVertices = model.useQefVertices;
                    octreeMesher.qefVertexMode = model.qefVertexMode;
                    octreeMesher.qefBlendFactor = model.qefBlendFactor;
                    octreeMesher.qefSnapEpsilon = model.qefSnapEpsilon;
                    octreeMesher.qefMaxOffsetCells = model.qefMaxOffsetCells;
                    octreeMesher.qefAxisSnapStrength = model.qefAxisSnapStrength;
                    octreeMesher.BuildMesh(model.octreeSampler.Volume, mesh);

                    break;
                }
        }

        Debug.Log($"VolumeMeshRenderer: vertex count = {mesh.vertexCount}, indexFormat = {mesh.indexFormat}");
    }

    public void RebuildChunked(VolumeModel model)
    {
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

        if (model.forceFullChunkRedraw)
            QueueAllChunks(bounds.Count);
        else if (volumeDataChanged)
            QueueAllChunks(bounds.Count);
        else if (!hasSameLayout)
            QueueAllChunks(bounds.Count);
        else if (canDoDirtyRebuild)
        {
            QueueDirtyChunks(bounds, expandedDirtyBounds);
            if (model.dataStructure == VolumeDataStructure.Octree && model.octreeExpandDirtyNeighbors)
                ExpandQueuedChunks(bounds, Mathf.Max(1, model.octreeDirtyNeighborRings));
        }

        for (int i = 0; i < _chunks.Count && i < bounds.Count; i++)
        {
            MeshVolumeChunk chunk = _chunks[i];
            Bounds chunkBounds = bounds[i];

            chunk.name = $"MeshVolumeChunk_{i:000}";
            chunk.coreBounds = chunkBounds;
            chunk.buildBounds = chunkBounds;

        }

        RebuildQueuedChunks(Mathf.Max(1, model.maxChunksPerRebuild));

        _lastActiveVolumeData = activeVolume;
        StoreChunkLayout(bounds);
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
        if (_pendingChunkQueue.Count == 0 && _pendingChunkSet.Count == 0)
        {
            for (int i = 0; i < bounds.Count; i++)
            {
                if (!bounds[i].Intersects(dirtyBounds))
                    continue;

                _pendingChunkQueue.Enqueue(i);
                _pendingChunkSet.Add(i);
            }

            return;
        }

        for (int i = 0; i < bounds.Count; i++)
        {
            if (!bounds[i].Intersects(dirtyBounds))
                continue;

            if (_pendingChunkSet.Contains(i))
                continue;

            _pendingChunkQueue.Enqueue(i);
            _pendingChunkSet.Add(i);
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
                {
                    OctreeVolume octree = model.octreeSampler.Volume;
                    if (octree != null)
                        return Mathf.Max(octree.CellSize.x, Mathf.Max(octree.CellSize.y, octree.CellSize.z)) * model.dirtyHaloMultiplier;
                    break;
                }
        }

        return 0.01f;
    }

    private void Update()
    {
        if (_pendingChunkQueue.Count == 0)
            return;

        if (_activeChunkModel == null || _activeChunkComposer == null)
            return;

        RebuildQueuedChunks(Mathf.Max(1, _activeChunkModel.maxChunksPerRebuild));
    }

    private void RebuildQueuedChunks(int budget)
    {
        if (_activeChunkModel == null || _activeChunkComposer == null)
            return;

        int rebuilt = 0;

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
            chunk.Rebuild(_activeChunkModel, _activeChunkComposer);
            rebuilt++;
        }

#if UNITY_EDITOR
        if (_activeChunkModel != null && _activeChunkModel.logChunkRebuildStats)
            Debug.Log($"Chunk rebuild: rebuilt={rebuilt}, pending={_pendingChunkQueue.Count}, budget={budget}");
#endif
    }

    /// <summary>Copies generated mesh buffers into the Unity mesh.</summary>
    private void ApplyMeshData(MeshData meshData, VolumeModel model)
    {
        mesh.indexFormat = IndexFormat.UInt32;
        mesh.Clear();

        if (meshData == null)
            return;

        mesh.SetVertices(meshData.Vertices);
        mesh.SetTriangles(meshData.Triangles, 0);

        if (meshData.Bounds.size != Vector3.zero)
            mesh.bounds = meshData.Bounds;

        if (model.recalculateNormals)
            mesh.RecalculateNormals();

        if (model.recalculateBounds)
            mesh.RecalculateBounds();
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


