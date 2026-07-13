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

                    // Position at chunk center for gizmos/debugging
                    VolumeChunk chunk = GetChunkBounds(cx, cy, cz, layout.ChunkSize, layout.Resolution);
                    Vector3 worldCenter = layout.Origin + 
                        new Vector3(
                            (chunk.CellBounds.position.x + chunk.CellBounds.size.x * 0.5f) * layout.CellSize,
                            (chunk.CellBounds.position.y + chunk.CellBounds.size.y * 0.5f) * layout.CellSize,
                            (chunk.CellBounds.position.z + chunk.CellBounds.size.z * 0.5f) * layout.CellSize);
                    go.transform.localPosition = worldCenter;

                    _renderers[coord] = cr;
                }
            }
        }
    }

    private static VolumeChunk GetChunkBounds(int cx, int cy, int cz, int chunkSize, Vector3Int resolution)
    {
        int minX = cx * chunkSize;
        int minY = cy * chunkSize;
        int minZ = cz * chunkSize;
        int maxX = Mathf.Min((cx + 1) * chunkSize, resolution.x);
        int maxY = Mathf.Min((cy + 1) * chunkSize, resolution.y);
        int maxZ = Mathf.Min((cz + 1) * chunkSize, resolution.z);

        return new VolumeChunk
        {
            ChunkIndex = new Vector3Int(cx, cy, cz),
            CellBounds = new BoundsInt(minX, minY, minZ, maxX - minX, maxY - minY, maxZ - minZ),
            Version = 0
        };
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
