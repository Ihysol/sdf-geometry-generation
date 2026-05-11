using UnityEngine;

public class OctreeVolume : IVolumeData
{
    public OctreeNode Root { get; }
    public Bounds Bounds { get; }
    public int MaxDepth { get; }

    public int TotalNodes { get; }
    public int SurfaceLeaves { get; }

    public IScalarFieldSource Source { get; }

    public OctreeVolume(
        OctreeNode root,
        Bounds bounds,
        int maxDepth,
        int totalNodes,
        int surfaceLeaves,
        IScalarFieldSource source)
    {
        Root = root;
        Bounds = bounds;
        MaxDepth = maxDepth;
        TotalNodes = totalNodes;
        SurfaceLeaves = surfaceLeaves;
        Source = source;
    }
}