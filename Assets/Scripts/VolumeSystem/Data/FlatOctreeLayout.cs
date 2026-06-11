using System.Collections.Generic;
using UnityEngine;

public sealed class FlatOctreeLayout
{
    public Vector3[] Centers;
    public Vector3[] Sizes;
    public Vector3[] SurfaceVertices;
    public Vector3[] SurfaceNormals;
    public Vector3Int[] Coords;
    public Vector3Int[] NodeSizeInCells;
    public float[] CornerValues8;
    public int[] FirstChildIndex;
    public byte[] ChildMask;
    public byte[] Flags;
    public int[] SurfaceLeafIndices { get; private set; }
    public int SurfaceLeafCount { get; private set; }
    public int[] SubtreeSize { get; private set; }
    public int[] ChildIndexByOctant { get; private set; }
    public int[] LeafByCellCoord { get; private set; }
    public Vector3Int LeafLookupGridSize { get; private set; }
    public Dictionary<Vector3Int, int> LeafExactByCoord { get; private set; }
    public Dictionary<Vector3Int, int> ResolvedLeafByCoord { get; private set; }
    public HashSet<Vector3Int> MissingLeafCoords { get; private set; }
    private bool _runtimeCacheReady;
    public int Count => Centers != null ? Centers.Length : 0;

    public const byte FlagLeaf = 1 << 0;
    public const byte FlagSurface = 1 << 1;
    private const int MaxDenseLeafLookupEntries = 8 * 1024 * 1024;

    public bool IsValid =>
        Centers != null &&
        Sizes != null &&
        SurfaceVertices != null &&
        (SurfaceNormals == null || SurfaceNormals.Length == Count) &&
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
        if (_runtimeCacheReady)
            return;

        int leafCount = 0;
        int surfaceLeafCount = 0;
        for (int i = 0; i < Count; i++)
        {
            if (!IsLeaf(i))
                continue;

            leafCount++;
            if (IsSurface(i))
                surfaceLeafCount++;
        }

        int[] surfaceLeaves = EnsureIntArray(SurfaceLeafIndices, surfaceLeafCount);
        int surfaceLeafWrite = 0;

        int[] subtreeSize = EnsureIntArray(SubtreeSize, Count);
        System.Array.Clear(subtreeSize, 0, Count);

        int childIndexCount = Count * 8;
        int[] childIndexByOctant = EnsureIntArray(ChildIndexByOctant, childIndexCount);
        for (int i = 0; i < childIndexCount; i++)
            childIndexByOctant[i] = -1;

        ComputeSubtreeSize(0, subtreeSize, childIndexByOctant);
        bool hasDenseLeafLookup = BuildDenseLeafLookup(out int[] leafByCellCoord, out Vector3Int leafLookupGridSize);
        Dictionary<Vector3Int, int> leafExactByCoord = LeafExactByCoord ?? new Dictionary<Vector3Int, int>(leafCount);
        leafExactByCoord.Clear();

        for (int i = 0; i < Count; i++)
        {
            if (!IsLeaf(i))
                continue;

            if (!hasDenseLeafLookup)
                leafExactByCoord[Coords[i]] = i;
            if (IsSurface(i))
                surfaceLeaves[surfaceLeafWrite++] = i;
        }

        SurfaceLeafIndices = surfaceLeaves;
        SurfaceLeafCount = surfaceLeafCount;
        SubtreeSize = subtreeSize;
        ChildIndexByOctant = childIndexByOctant;
        LeafByCellCoord = leafByCellCoord;
        LeafLookupGridSize = leafLookupGridSize;
        LeafExactByCoord = leafExactByCoord;
        ResolvedLeafByCoord ??= new Dictionary<Vector3Int, int>();
        ResolvedLeafByCoord.Clear();
        MissingLeafCoords ??= new HashSet<Vector3Int>();
        MissingLeafCoords.Clear();
        _runtimeCacheReady = true;
    }

    public void InvalidateRuntimeCache()
    {
        _runtimeCacheReady = false;
        SurfaceLeafCount = 0;
    }

    public bool TryGetContainingLeafIndex(Vector3Int coord, out int nodeIndex)
    {
        if (LeafByCellCoord != null &&
            coord.x >= 0 && coord.x < LeafLookupGridSize.x &&
            coord.y >= 0 && coord.y < LeafLookupGridSize.y &&
            coord.z >= 0 && coord.z < LeafLookupGridSize.z)
        {
            int index = coord.x + LeafLookupGridSize.x * (coord.y + LeafLookupGridSize.y * coord.z);
            nodeIndex = LeafByCellCoord[index];
            return nodeIndex >= 0;
        }

        nodeIndex = -1;
        return false;
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

    public Vector3 GetSurfaceNormalOrDefault(int nodeIndex)
    {
        if (!IsIndexValid(nodeIndex) || SurfaceNormals == null || nodeIndex >= SurfaceNormals.Length)
            return Vector3.up;

        Vector3 normal = SurfaceNormals[nodeIndex];
        return normal.sqrMagnitude > 1e-12f ? normal : Vector3.up;
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

    private bool BuildDenseLeafLookup(out int[] leafByCellCoord, out Vector3Int gridSize)
    {
        leafByCellCoord = null;
        gridSize = Vector3Int.zero;

        if (Count == 0 || NodeSizeInCells == null)
            return false;

        gridSize = GetNodeSizeInCells(0);
        long entryCount = (long)gridSize.x * gridSize.y * gridSize.z;
        if (entryCount <= 0 || entryCount > MaxDenseLeafLookupEntries)
        {
            gridSize = Vector3Int.zero;
            return false;
        }

        int required = (int)entryCount;
        leafByCellCoord = EnsureIntArray(LeafByCellCoord, required);
        for (int i = 0; i < required; i++)
            leafByCellCoord[i] = -1;

        for (int i = 0; i < Count; i++)
        {
            if (!IsLeaf(i))
                continue;

            Vector3Int coord = Coords[i];
            Vector3Int size = GetNodeSizeInCells(i);
            int xMax = Mathf.Min(gridSize.x, coord.x + size.x);
            int yMax = Mathf.Min(gridSize.y, coord.y + size.y);
            int zMax = Mathf.Min(gridSize.z, coord.z + size.z);

            for (int z = Mathf.Max(0, coord.z); z < zMax; z++)
            for (int y = Mathf.Max(0, coord.y); y < yMax; y++)
            for (int x = Mathf.Max(0, coord.x); x < xMax; x++)
            {
                int index = x + gridSize.x * (y + gridSize.y * z);
                leafByCellCoord[index] = i;
            }
        }

        return true;
    }

    private static int[] EnsureIntArray(int[] array, int required)
    {
        if (required <= 0)
            return System.Array.Empty<int>();

        return array == null || array.Length < required
            ? new int[required]
            : array;
    }
}
