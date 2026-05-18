using UnityEngine;
using UnityEngine.Rendering;
using System.Collections.Generic;

[RequireComponent(typeof(MeshFilter))]
[RequireComponent(typeof(MeshRenderer))]
public class VolumeMeshRenderer : MonoBehaviour, IVolumeRenderer
{
    private MeshFilter meshFilter;
    private MeshRenderer meshRenderer;
    private Mesh mesh;
    private Transform _chunkRoot;
    private readonly List<MeshVolumeChunk> _chunks = new();

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

        EnsureChunks(bounds.Count);
        SetSurfaceMaterial(model.surfaceMaterial);

        VolumeSceneComposer composer = model.GetComponent<VolumeSceneComposer>();

        if (composer == null)
            return;

        composer.RebuildComposition();

        for (int i = 0; i < _chunks.Count && i < bounds.Count; i++)
        {
            MeshVolumeChunk chunk = _chunks[i];
            Bounds chunkBounds = bounds[i];

            chunk.name = $"MeshVolumeChunk_{i:000}";
            chunk.coreBounds = chunkBounds;
            chunk.buildBounds = chunkBounds;
            chunk.Rebuild(model, composer);
        }
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
