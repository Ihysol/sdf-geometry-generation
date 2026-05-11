using UnityEngine;

public class OctreeNode
{
    public Bounds Bounds;
    public OctreeNode[] Children;

    public float CenterValue;
    public bool IsLeaf;
    public bool ContainsSurface;

    public bool HasChildren => Children != null && Children.Length > 0;

    public OctreeNode(Bounds bounds)
    {
        Bounds = bounds;
        IsLeaf = true;
    }
}