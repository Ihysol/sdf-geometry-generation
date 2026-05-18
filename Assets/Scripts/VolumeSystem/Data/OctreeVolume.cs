using UnityEngine;
using System.Collections.Generic;

public class OctreeVolume : IVolumeData, IChunkLayoutVolume
{
    public OctreeNode Root { get; }
    public Bounds Bounds { get; }
    public int MaxDepth { get; }

    public int TotalNodes { get; }
    public int SurfaceLeaves { get; }

    public IScalarFieldSource Source { get; }

    public Vector3 GridOrigin { get; }
    public Vector3 CellSize { get; }

    /// <summary>Stores a built octree volume and its global grid metadata.</summary>
    public OctreeVolume(
        OctreeNode root,
        Bounds bounds,
        int maxDepth,
        int totalNodes,
        int surfaceLeaves,
        IScalarFieldSource source,
        Vector3 gridOrigin,
        Vector3 cellSize)
    {
        Root = root;
        Bounds = bounds;
        MaxDepth = maxDepth;
        TotalNodes = totalNodes;
        SurfaceLeaves = surfaceLeaves;
        Source = source;
        GridOrigin = gridOrigin;
        CellSize = cellSize;
    }

    public void BuildChunkBounds(ChunkingSettings settings, List<Bounds> output)
    {
        output.Clear();

        Vector3Int chunkCount = settings.octreeChunkCount;
        chunkCount.x = Mathf.Max(1, chunkCount.x);
        chunkCount.y = Mathf.Max(1, chunkCount.y);
        chunkCount.z = Mathf.Max(1, chunkCount.z);

        Bounds bounds = Bounds;
        Vector3 chunkSize = new Vector3(
            bounds.size.x / chunkCount.x,
            bounds.size.y / chunkCount.y,
            bounds.size.z / chunkCount.z
        );

        for (int x = 0; x < chunkCount.x; x++)
            for (int y = 0; y < chunkCount.y; y++)
                for (int z = 0; z < chunkCount.z; z++)
                {
                    Vector3 center = bounds.min + new Vector3(
                        (x + 0.5f) * chunkSize.x,
                        (y + 0.5f) * chunkSize.y,
                        (z + 0.5f) * chunkSize.z
                    );

                    output.Add(new Bounds(center, chunkSize));
                }
    }
}
