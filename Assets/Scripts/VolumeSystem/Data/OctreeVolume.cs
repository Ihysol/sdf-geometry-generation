using UnityEngine;

public class OctreeVolume : IVolumeData
{
    public OctreeNode Root { get; private set; }
    public Bounds Bounds { get; private set; }
    public int MaxDepth { get; private set; }

    public OctreeVolume(OctreeNode root, Bounds bounds, int maxDepth)
    {
        Root = root;
        Bounds = bounds;
        MaxDepth = maxDepth;
    }
}