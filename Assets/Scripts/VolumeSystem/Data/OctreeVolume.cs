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

        if (Root == null)
        {
            output.Add(Bounds);
            return;
        }

        int estimatedTrianglesPerLeaf = Mathf.Max(1, settings.octreeEstimatedTrianglesPerLeaf);
        int targetTriangles = Mathf.Max(1, settings.octreeTargetTrianglesPerChunk);
        int maxLeafNodes = Mathf.Max(1, settings.octreeMaxLeafNodesPerChunk);
        int targetLeavesPerChunk = Mathf.Max(1, Mathf.Min(maxLeafNodes, targetTriangles / estimatedTrianglesPerLeaf));
        Dictionary<OctreeNode, int> leafCountCache = new Dictionary<OctreeNode, int>(128);
        BuildDisjointChunkBounds(Root, targetLeavesPerChunk, output, leafCountCache);

        if (output.Count == 0)
            output.Add(Bounds);
    }

    private static int BuildDisjointChunkBounds(
        OctreeNode node,
        int targetLeavesPerChunk,
        List<Bounds> output,
        Dictionary<OctreeNode, int> leafCountCache)
    {
        if (node == null)
            return 0;

        if (node.IsLeaf)
        {
            if (node.ContainsSurface)
            {
                output.Add(node.Bounds);
                return 1;
            }

            return 0;
        }

        if (node.Children == null)
            return 0;

        int surfaceLeafCount = CountSurfaceLeaves(node, leafCountCache);

        if (surfaceLeafCount <= 0)
            return 0;

        if (surfaceLeafCount <= targetLeavesPerChunk)
        {
            output.Add(node.Bounds);
            return surfaceLeafCount;
        }

        int accumulated = 0;

        for (int i = 0; i < node.Children.Length; i++)
            accumulated += BuildDisjointChunkBounds(node.Children[i], targetLeavesPerChunk, output, leafCountCache);

        return accumulated;
    }

    private static int CountSurfaceLeaves(OctreeNode node, Dictionary<OctreeNode, int> leafCountCache)
    {
        if (node == null)
            return 0;

        if (leafCountCache.TryGetValue(node, out int cached))
            return cached;

        if (node.IsLeaf)
        {
            int leafValue = node.ContainsSurface ? 1 : 0;
            leafCountCache[node] = leafValue;
            return leafValue;
        }

        if (node.Children == null)
            return 0;

        int count = 0;

        for (int i = 0; i < node.Children.Length; i++)
            count += CountSurfaceLeaves(node.Children[i], leafCountCache);

        leafCountCache[node] = count;
        return count;
    }
}
