using System.Collections.Generic;
using UnityEngine;

public class ChunkRenderManager
{
    private readonly Dictionary<ChunkCoord, ChunkRenderer> _renderers = new();
    private Transform _parent;
    private Material _material;

    public void Initialize(int totalChunks, Vector3Int gridSize, Transform parent, VolumeLayout layout)
    {
        _parent = parent;
        if (_parent == null) return;

        for (int cz = 0; cz < gridSize.z; cz++)
        {
            for (int cy = 0; cy < gridSize.y; cy++)
            {
                for (int cx = 0; cx < gridSize.x; cx++)
                {
                    ChunkCoord coord = new ChunkCoord(cx, cy, cz);

                    GameObject go = new GameObject($"Chunk_{cx}_{cy}_{cz}");
                    go.transform.SetParent(_parent, false);

                    MeshFilter mf = go.AddComponent<MeshFilter>();
                    MeshRenderer mr = go.AddComponent<MeshRenderer>();
                    ChunkRenderer cr = go.AddComponent<ChunkRenderer>();

                    // Keep chunk GOs at origin — vertices are in world space and transformed to local by ChunkRenderer.
                    go.transform.localPosition = Vector3.zero;
                    go.transform.localScale = Vector3.one;

                    _renderers[coord] = cr;
                }
            }
        }
    }

    public void Apply(ChunkCoord coord, CpuMeshData meshData)
    {
        if (_renderers.TryGetValue(coord, out ChunkRenderer renderer))
            renderer.ApplyMesh(meshData);
    }

    public void SetMaterial(Material material)
    {
        _material = material;
        foreach (var kvp in _renderers)
            kvp.Value.SetMaterial(material);
    }

    public void ClearAll()
    {
        foreach (var kvp in _renderers)
            kvp.Value.Clear();
    }

    public void Dispose()
    {
        if (_parent == null) return;

        while (_parent.childCount > 0)
        {
            GameObject.DestroyImmediate(_parent.GetChild(0).gameObject);
        }

        _renderers.Clear();
    }
}
