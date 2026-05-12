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

    public float[] CornerValues;
    public Vector3Int Coord;
    public Vector3Int SizeInCells = Vector3Int.one;
    public int Depth;

    public bool HasChildren =>
        Children != null &&
        Children.Length > 0;

    /// <summary>Creates an octree node for the supplied world-space bounds.</summary>
    public OctreeNode(Bounds bounds)
    {
        Bounds = bounds;
    }
}
