using UnityEngine;

public class OctreeNode
{
    public Bounds Bounds;
    public OctreeNode[] Children;

    public bool IsLeaf = true;
    public bool ContainsSurface;

    public float CenterValue;

    public Vector3 SurfaceVertex;
    public int MeshVertexIndex = -1;

    public Vector3Int Coord;
    public float[] CornerValues;

    public int Depth;

    public bool HasChildren =>
        Children != null &&
        Children.Length > 0;

    public OctreeNode(Bounds bounds)
    {
        Bounds = bounds;
    }
}