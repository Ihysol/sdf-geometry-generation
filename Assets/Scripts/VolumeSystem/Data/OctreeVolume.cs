using UnityEngine;
using System.Collections.Generic;

public readonly struct OctreeHermiteSample
{
    public readonly Vector3 Point;
    public readonly Vector3 Normal;
    public readonly float Weight;

    public OctreeHermiteSample(Vector3 point, Vector3 normal, float weight)
    {
        Point = point;
        Normal = normal;
        Weight = weight;
    }
}

public readonly struct OctreeHermiteEdgeKey
{
    public readonly Vector3Int A;
    public readonly Vector3Int B;

    public OctreeHermiteEdgeKey(Vector3Int a, Vector3Int b)
    {
        if (LexicographicLessOrEqual(a, b))
        {
            A = a;
            B = b;
        }
        else
        {
            A = b;
            B = a;
        }
    }

    public override int GetHashCode()
    {
        return (A.GetHashCode() * 397) ^ B.GetHashCode();
    }

    public override bool Equals(object obj)
    {
        return obj is OctreeHermiteEdgeKey other && A == other.A && B == other.B;
    }

    private static bool LexicographicLessOrEqual(Vector3Int x, Vector3Int y)
    {
        if (x.x != y.x) return x.x < y.x;
        if (x.y != y.y) return x.y < y.y;
        return x.z <= y.z;
    }
}

public class OctreeVolume : IVolumeData, IChunkLayoutVolume, IFlatAdaptiveVolumeData
{
    public OctreeNode Root { get; }
    public Bounds Bounds { get; }
    public int MaxDepth { get; }

    public int TotalNodes { get; }
    public int SurfaceLeaves { get; }

    public IScalarFieldSource Source { get; }

    public Vector3 GridOrigin { get; }
    public Vector3 CellSize { get; }
    public int CachedHermiteSampleCount => _hermiteSamples?.Count ?? 0;
    private FlatOctreeLayout _flatLayout;
    private readonly Dictionary<OctreeHermiteEdgeKey, OctreeHermiteSample> _hermiteSamples;
    private readonly int _hermiteSampleRefinementSteps;
    private readonly float _hermiteSampleIsoLevel;

    /// <summary>Stores a built octree volume and its global grid metadata.</summary>
    public OctreeVolume(
        OctreeNode root,
        Bounds bounds,
        int maxDepth,
        int totalNodes,
        int surfaceLeaves,
        IScalarFieldSource source,
        Vector3 gridOrigin,
        Vector3 cellSize,
        Dictionary<OctreeHermiteEdgeKey, OctreeHermiteSample> hermiteSamples = null,
        int hermiteSampleRefinementSteps = 0,
        float hermiteSampleIsoLevel = 0f,
        FlatOctreeLayout flatLayout = null)
    {
        Root = root;
        Bounds = bounds;
        MaxDepth = maxDepth;
        TotalNodes = totalNodes;
        SurfaceLeaves = surfaceLeaves;
        Source = source;
        GridOrigin = gridOrigin;
        CellSize = cellSize;
        _hermiteSamples = hermiteSamples;
        _hermiteSampleRefinementSteps = hermiteSampleRefinementSteps;
        _hermiteSampleIsoLevel = hermiteSampleIsoLevel;
        _flatLayout = flatLayout;
    }

    public bool TryGetHermiteSample(
        Vector3Int a,
        Vector3Int b,
        int refinementSteps,
        float isoLevel,
        out OctreeHermiteSample sample)
    {
        sample = default;
        return _hermiteSamples != null &&
               _hermiteSampleRefinementSteps == refinementSteps &&
               _hermiteSampleIsoLevel == isoLevel &&
               _hermiteSamples.TryGetValue(new OctreeHermiteEdgeKey(a, b), out sample);
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

    public FlatOctreeLayout GetFlatLayout(bool includeCornerValues = false)
    {
        if (_flatLayout != null)
            return _flatLayout;

        if (Root == null)
            return null;

        int count = Mathf.Max(1, TotalNodes);
        Vector3[] centers = new Vector3[count];
        Vector3[] sizes = new Vector3[count];
        Vector3[] surfaceVertices = new Vector3[count];
        Vector3Int[] coords = new Vector3Int[count];
        Vector3Int[] nodeSizeInCells = new Vector3Int[count];
        float[] cornerValues8 = new float[count * 8];
        int[] firstChildIndex = new int[count];
        byte[] childMask = new byte[count];
        byte[] flags = new byte[count];

        for (int i = 0; i < count; i++)
            firstChildIndex[i] = -1;

        int write = 0;
        FlattenNode(Root, centers, sizes, surfaceVertices, coords, nodeSizeInCells, cornerValues8, firstChildIndex, childMask, flags, ref write);

        if (write != count)
        {
            System.Array.Resize(ref centers, write);
            System.Array.Resize(ref sizes, write);
            System.Array.Resize(ref surfaceVertices, write);
            System.Array.Resize(ref coords, write);
            System.Array.Resize(ref nodeSizeInCells, write);
            System.Array.Resize(ref cornerValues8, write * 8);
            System.Array.Resize(ref firstChildIndex, write);
            System.Array.Resize(ref childMask, write);
            System.Array.Resize(ref flags, write);
        }

        _flatLayout = new FlatOctreeLayout
        {
            Centers = centers,
            Sizes = sizes,
            SurfaceVertices = surfaceVertices,
            Coords = coords,
            NodeSizeInCells = nodeSizeInCells,
            CornerValues8 = cornerValues8,
            FirstChildIndex = firstChildIndex,
            ChildMask = childMask,
            Flags = flags
        };
        _flatLayout.SetCount(write);

        return _flatLayout;
    }

    private static int FlattenNode(
        OctreeNode node,
        Vector3[] centers,
        Vector3[] sizes,
        Vector3[] surfaceVertices,
        Vector3Int[] coords,
        Vector3Int[] nodeSizeInCells,
        float[] cornerValues8,
        int[] firstChildIndex,
        byte[] childMask,
        byte[] flags,
        ref int write)
    {
        int my = write++;
        centers[my] = node.Bounds.center;
        sizes[my] = node.Bounds.size;
        surfaceVertices[my] = node.SurfaceVertex;
        coords[my] = node.Coord;
        nodeSizeInCells[my] = node.SizeInCells;

        int cornerBase = my * 8;
        if (node.CornerValues != null)
        {
            int copyCount = Mathf.Min(8, node.CornerValues.Length);
            for (int i = 0; i < copyCount; i++)
                cornerValues8[cornerBase + i] = node.CornerValues[i];
        }

        byte f = 0;
        if (node.IsLeaf) f |= FlatOctreeLayout.FlagLeaf;
        if (node.ContainsSurface) f |= FlatOctreeLayout.FlagSurface;
        flags[my] = f;

        if (node.Children == null || node.Children.Length == 0)
            return my;

        int first = -1;
        byte mask = 0;
        for (int i = 0; i < node.Children.Length && i < 8; i++)
        {
            OctreeNode child = node.Children[i];
            if (child == null)
                continue;
            if (first < 0)
                first = write;
            mask |= (byte)(1 << i);
            FlattenNode(child, centers, sizes, surfaceVertices, coords, nodeSizeInCells, cornerValues8, firstChildIndex, childMask, flags, ref write);
        }

        firstChildIndex[my] = first;
        childMask[my] = mask;
        return my;
    }
}
