#if UNITY_EDITOR
using UnityEditor;
#endif

using System.Collections.Generic;
using UnityEngine;

[ExecuteAlways]
public class ChunkedVolumeRenderer : MonoBehaviour, IVolumeRenderer
{
    public Vector3Int chunkCount = new Vector3Int(2, 2, 2);
    public float chunkPadding = 0.1f;

    private readonly List<MeshVolumeChunk> _chunks = new();

    private Transform _chunkRoot;

    private Transform ChunkRoot
    {
        get
        {
            if (_chunkRoot != null)
                return _chunkRoot;

            Transform existing = transform.Find("Volume Mesh");


            if (existing != null)
            {
                _chunkRoot = existing;
                return _chunkRoot;
            }

            GameObject go = new GameObject("Volume Mesh");
            go.transform.SetParent(transform, false);

            _chunkRoot = go.transform;
            return _chunkRoot;
        }
    }

    public void Rebuild(VolumeModel model)
    {
        RebuildChunks(model);
    }

    public void Clear()
    {
        ClearChunks();
    }

    public void RebuildChunks(VolumeModel model)
    {
        if (model == null)
            return;

        VolumeSceneComposer composer = model.GetComponent<VolumeSceneComposer>();

        if (composer == null)
            return;

        composer.RebuildComposition();

        EnsureChunks(model);
        RebuildAll(model, composer);
    }

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

    private void AssignChunkBounds(VolumeModel model)
    {
        Bounds bounds = model.octreeSampler.builder.Bounds;

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

                    // Vector3 paddedSize =
                    //     baseSize + Vector3.one * chunkPadding * 2f;

                    // Bounds build = new Bounds(center, paddedSize);
                    Bounds build = core;

                    MeshVolumeChunk chunk = _chunks[index];

                    chunk.name = $"MeshVolumeChunk_{x}_{y}_{z}";
                    chunk.coreBounds = core;
                    chunk.buildBounds = build;

                    index++;
                }
    }

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
}