#if UNITY_EDITOR
using UnityEditor;
#endif

using System.Collections.Generic;
using UnityEngine;

[ExecuteAlways]
public class ChunkedVolumeRenderer : MonoBehaviour, IVolumeRenderer
{
    public Vector3Int chunkCount = new Vector3Int(2, 2, 2);
    public bool buildSeamMesh = false;

    private readonly IChunkSeamStitcher[] _seamStitchers =
    {
        new ChunkSeamStitcher(),
        new VoxelGridChunkSeamStitcher()
    };

    private VolumeSeamMesh _seamMesh;

    private readonly List<MeshVolumeChunk> _chunks = new();

    private Transform _chunkRoot;

    private Transform SeamRoot
    {
        get
        {
            Transform existing = transform.Find("Seams");

            if (existing != null)
                return existing;

            GameObject go = new GameObject("Seams");
            go.transform.SetParent(transform, false);
            return go.transform;
        }
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

    /// <summary>Regenerates all chunk meshes for the model.</summary>
    public void Rebuild(VolumeModel model)
    {
        RebuildChunks(model);
    }

    /// <summary>Clears all generated chunk meshes.</summary>
    public void Clear()
    {
        ClearChunks();
    }

    /// <summary>Ensures chunk objects exist and rebuilds each chunk from the current composition.</summary>
    public void RebuildChunks(VolumeModel model)
    {
        if (model == null)
            return;

        VolumeSceneComposer composer = model.GetComponent<VolumeSceneComposer>();

        if (composer == null)
            return;

        composer.RebuildComposition();

        EnsureChunks(model);
        SetSurfaceMaterial(model.surfaceMaterial);
        RebuildAll(model, composer);

        if (buildSeamMesh)
            RebuildSeams(model, composer);
        else
            ClearSeams();
    }

    /// <summary>Builds the optional seam mesh used for debugging or legacy stitching.</summary>
    private void RebuildSeams(VolumeModel model, IScalarFieldSource source)
    {
        if (_seamMesh == null)
        {
            Transform root = SeamRoot;

            Transform existing = root.Find("ChunkSeamMesh");

            if (existing != null)
                _seamMesh = existing.GetComponent<VolumeSeamMesh>();

            if (_seamMesh == null)
            {
                GameObject go = new GameObject("ChunkSeamMesh");
                go.transform.SetParent(root, false);
                _seamMesh = go.AddComponent<VolumeSeamMesh>();
            }
        }

        IVolumeData activeVolume = model.GetActiveVolume();

        if (activeVolume == null)
            return;

        IChunkSeamStitcher seamStitcher = ResolveSeamStitcher(model, activeVolume);

        if (seamStitcher == null)
        {
            ClearSeams();
            return;
        }

        Bounds globalBounds = activeVolume.Bounds;

        seamStitcher.RebuildSeams(
            model,
            source,
            globalBounds,
            chunkCount,
            _seamMesh.Mesh
        );

        _seamMesh.ApplyMaterial(model.surfaceMaterial);
    }

    /// <summary>Clears any existing seam mesh when seam stitching is disabled.</summary>
    private void ClearSeams()
    {
        if (_seamMesh != null)
        {
            _seamMesh.Clear();
            return;
        }

        Transform root = transform.Find("Seams");

        if (root == null)
            return;

        Transform existing = root.Find("ChunkSeamMesh");

        if (existing == null)
            return;

        _seamMesh = existing.GetComponent<VolumeSeamMesh>();

        if (_seamMesh != null)
            _seamMesh.Clear();
    }

    /// <summary>Destroys generated chunk GameObjects under the chunk root.</summary>
    public void ClearChunks()
    {
        _chunks.Clear();

        Transform root = ChunkRoot;

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

    /// <summary>Creates or removes chunk objects to match the requested chunk count.</summary>
    private void EnsureChunks(VolumeModel model)
    {
        chunkCount.x = Mathf.Max(1, chunkCount.x);
        chunkCount.y = Mathf.Max(1, chunkCount.y);
        chunkCount.z = Mathf.Max(1, chunkCount.z);

        int needed = chunkCount.x * chunkCount.y * chunkCount.z;

        _chunks.Clear();

        Transform root = ChunkRoot;

        for (int i = 0; i < root.childCount; i++)
        {
            MeshVolumeChunk chunk =
                root.GetChild(i).GetComponent<MeshVolumeChunk>();

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

        AssignChunkBounds(model);
    }

    /// <summary>Assigns half-open ownership bounds to each chunk.</summary>
    private void AssignChunkBounds(VolumeModel model)
    {
        IVolumeData activeVolume = model.GetActiveVolume();

        if (activeVolume == null)
            return;

        Bounds bounds = activeVolume.Bounds;

        Vector3 baseSize = new Vector3(
            bounds.size.x / chunkCount.x,
            bounds.size.y / chunkCount.y,
            bounds.size.z / chunkCount.z
        );

        int index = 0;

        for (int x = 0; x < chunkCount.x; x++)
            for (int y = 0; y < chunkCount.y; y++)
                for (int z = 0; z < chunkCount.z; z++)
                {
                    Vector3 center = bounds.min + new Vector3(
                        (x + 0.5f) * baseSize.x,
                        (y + 0.5f) * baseSize.y,
                        (z + 0.5f) * baseSize.z
                    );

                    Bounds core = new Bounds(center, baseSize);

                    MeshVolumeChunk chunk = _chunks[index];

                    chunk.name = $"MeshVolumeChunk_{x}_{y}_{z}";
                    chunk.coreBounds = core;
                    chunk.buildBounds = core;

                    index++;
                }
    }

    /// <summary>Rebuilds every chunk mesh from the shared model volume.</summary>
    private void RebuildAll(
        VolumeModel model,
        IScalarFieldSource source)
    {
        for (int i = 0; i < _chunks.Count; i++)
        {
            _chunks[i].Rebuild(
                model,
                source
            );
        }
    }

    public void SetSurfaceMaterial(Material material)
    {
        for (int i = 0; i < _chunks.Count; i++)
            _chunks[i].SetSurfaceMaterial(material);

        if (_seamMesh != null)
            _seamMesh.ApplyMaterial(material);
    }

    private IChunkSeamStitcher ResolveSeamStitcher(VolumeModel model, IVolumeData activeVolume)
    {
        for (int i = 0; i < _seamStitchers.Length; i++)
        {
            IChunkSeamStitcher stitcher = _seamStitchers[i];

            if (stitcher.CanHandle(model, activeVolume))
                return stitcher;
        }

        return null;
    }
}
