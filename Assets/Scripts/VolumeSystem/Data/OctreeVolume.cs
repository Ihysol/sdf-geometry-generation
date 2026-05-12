using UnityEngine;

public class OctreeVolume : IVolumeData
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
}
