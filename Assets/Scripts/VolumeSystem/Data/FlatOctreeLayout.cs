using System.Collections.Generic;
using UnityEngine;

public sealed class FlatOctreeLayout
{
    public Vector3[] Centers;
    public Vector3[] Sizes;
    public Vector3[] SurfaceVertices;
    public Vector3Int[] Coords;
    public Vector3Int[] NodeSizeInCells;
    public float[] CornerValues8;
    public int[] FirstChildIndex;
    public byte[] ChildMask;
    public byte[] Flags;
    public int[] SurfaceLeafIndices { get; private set; }
    public int[] SubtreeSize { get; private set; }
    public int[] ChildIndexByOctant { get; private set; }
    public Dictionary<Vector3Int, int> LeafExactByCoord { get; private set; }
    public Dictionary<Vector3Int, int> ResolvedLeafByCoord { get; private set; }
    public HashSet<Vector3Int> MissingLeafCoords { get; private set; }
    public int Count => Centers != null ? Centers.Length : 0;

    public const byte FlagLeaf = 1 << 0;
    public const byte FlagSurface = 1 << 1;

    public bool IsValid =>
        Centers != null &&
        Sizes != null &&
        SurfaceVertices != null &&
        Coords != null &&
        NodeSizeInCells != null &&
        CornerValues8 != null &&
        FirstChildIndex != null &&
        ChildMask != null &&
        Flags != null &&
        Sizes.Length == Count &&
        SurfaceVertices.Length == Count &&
        Coords.Length == Count &&
        NodeSizeInCells.Length == Count &&
        CornerValues8.Length == Count * 8 &&
        FirstChildIndex.Length == Count &&
        ChildMask.Length == Count &&
        Flags.Length == Count;

    public void EnsureRuntimeCache()
    {
        if (SurfaceLeafIndices != null &&
            SubtreeSize != null &&
            ChildIndexByOctant != null &&
            LeafExactByCoord != null &&
            ResolvedLeafByCoord != null &&
            MissingLeafCoords != null)
            return;

        List<int> surfaceLeaves = new List<int>();
        Dictionary<Vector3Int, int> leafExactByCoord = new Dictionary<Vector3Int, int>();

        for (int i = 0; i < Count; i++)
        {
            if (!IsLeaf(i))
                continue;

            leafExactByCoord[Coords[i]] = i;
            if (IsSurface(i))
                surfaceLeaves.Add(i);
        }

        int[] subtreeSize = new int[Count];
        int[] childIndexByOctant = new int[Count * 8];
        for (int i = 0; i < childIndexByOctant.Length; i++)
            childIndexByOctant[i] = -1;

        ComputeSubtreeSize(0, subtreeSize, childIndexByOctant);

        SurfaceLeafIndices = surfaceLeaves.ToArray();
        SubtreeSize = subtreeSize;
        ChildIndexByOctant = childIndexByOctant;
        LeafExactByCoord = leafExactByCoord;
        ResolvedLeafByCoord = new Dictionary<Vector3Int, int>();
        MissingLeafCoords = new HashSet<Vector3Int>();
    }

    public Vector3Int GetNodeSizeInCells(int nodeIndex)
    {
        if (!IsIndexValid(nodeIndex) || NodeSizeInCells == null || nodeIndex >= NodeSizeInCells.Length)
            return Vector3Int.one;

        Vector3Int size = NodeSizeInCells[nodeIndex];
        return new Vector3Int(
            Mathf.Max(1, size.x),
            Mathf.Max(1, size.y),
            Mathf.Max(1, size.z)
        );
    }

    public bool IsLeaf(int nodeIndex)
    {
        return IsIndexValid(nodeIndex) && Flags != null && nodeIndex < Flags.Length && (Flags[nodeIndex] & FlagLeaf) != 0;
    }

    public bool IsSurface(int nodeIndex)
    {
        return IsIndexValid(nodeIndex) && Flags != null && nodeIndex < Flags.Length && (Flags[nodeIndex] & FlagSurface) != 0;
    }

    public float GetCornerValue(int nodeIndex, int cornerIndex)
    {
        if (!IsIndexValid(nodeIndex) || cornerIndex < 0 || cornerIndex >= 8 || CornerValues8 == null)
            return 0f;

        return CornerValues8[nodeIndex * 8 + cornerIndex];
    }

    public Vector3 GetSurfaceVertexOrCenter(int nodeIndex)
    {
        if (!IsIndexValid(nodeIndex))
            return Vector3.zero;

        return IsSurface(nodeIndex) && SurfaceVertices != null && nodeIndex < SurfaceVertices.Length
            ? SurfaceVertices[nodeIndex]
            : Centers[nodeIndex];
    }

    private bool IsIndexValid(int nodeIndex)
    {
        return nodeIndex >= 0 && nodeIndex < Count;
    }

    private int ComputeSubtreeSize(int nodeIndex, int[] subtreeSize, int[] childIndexByOctant)
    {
        if (!IsIndexValid(nodeIndex))
            return 0;
        if (subtreeSize[nodeIndex] > 0)
            return subtreeSize[nodeIndex];

        if (IsLeaf(nodeIndex))
        {
            subtreeSize[nodeIndex] = 1;
            return 1;
        }

        int first = FirstChildIndex[nodeIndex];
        int mask = ChildMask[nodeIndex];
        int cursor = first;
        int size = 1;

        for (int oct = 0; oct < 8; oct++)
        {
            if ((mask & (1 << oct)) == 0)
                continue;

            childIndexByOctant[nodeIndex * 8 + oct] = cursor;
            int childSize = ComputeSubtreeSize(cursor, subtreeSize, childIndexByOctant);
            size += childSize;
            cursor += childSize;
        }

        subtreeSize[nodeIndex] = size;
        return size;
    }
}
