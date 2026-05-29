using System.Collections.Generic;
using UnityEngine;

public class SparseVoxelOctreeVolume : IVolumeData, IChunkLayoutVolume, IFlatAdaptiveVolumeData
{
    public OctreeNode Root { get; }
    public Bounds Bounds { get; }
    public int MaxDepth { get; }
    public int TotalNodes { get; }
    public int SurfaceLeaves { get; }
    public int SparseLeafCount { get; }
    public IScalarFieldSource Source { get; }
    public Vector3 GridOrigin { get; }
    public Vector3 CellSize { get; }
    private FlatOctreeLayout _flatLayout;

    public SparseVoxelOctreeVolume(
        OctreeNode root,
        Bounds bounds,
        int maxDepth,
        int totalNodes,
        int surfaceLeaves,
        int sparseLeafCount,
        IScalarFieldSource source,
        Vector3 gridOrigin,
        Vector3 cellSize)
    {
        Root = root;
        Bounds = bounds;
        MaxDepth = maxDepth;
        TotalNodes = totalNodes;
        SurfaceLeaves = surfaceLeaves;
        SparseLeafCount = sparseLeafCount;
        Source = source;
        GridOrigin = gridOrigin;
        CellSize = cellSize;
    }

    public OctreeVolume AsOctreeVolume()
    {
        return new OctreeVolume(
            Root,
            Bounds,
            MaxDepth,
            TotalNodes,
            SurfaceLeaves,
            Source,
            GridOrigin,
            CellSize);
    }

    public void BuildChunkBounds(ChunkingSettings settings, List<Bounds> output)
    {
        output.Clear();

        Vector3Int chunkCount = settings.octreeChunkCount;
        chunkCount.x = Mathf.Max(1, chunkCount.x);
        chunkCount.y = Mathf.Max(1, chunkCount.y);
        chunkCount.z = Mathf.Max(1, chunkCount.z);

        Vector3 chunkSize = new Vector3(
            Bounds.size.x / chunkCount.x,
            Bounds.size.y / chunkCount.y,
            Bounds.size.z / chunkCount.z);

        for (int x = 0; x < chunkCount.x; x++)
        for (int y = 0; y < chunkCount.y; y++)
        for (int z = 0; z < chunkCount.z; z++)
        {
            Vector3 center = Bounds.min + new Vector3(
                (x + 0.5f) * chunkSize.x,
                (y + 0.5f) * chunkSize.y,
                (z + 0.5f) * chunkSize.z);
            output.Add(new Bounds(center, chunkSize));
        }
    }

    public FlatOctreeLayout GetFlatLayout(bool includeCornerValues = false)
    {
        if (_flatLayout != null)
            return _flatLayout;
        _flatLayout = AsOctreeVolume()?.GetFlatLayout(includeCornerValues);
        return _flatLayout;
    }
}
