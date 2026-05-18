using UnityEngine;

[System.Serializable]
public class OctreeVolumeBuilder : IVolumeBuilder<OctreeVolume>
{
    private const float GridSnapEpsilonFactor = 0.03f;
    [Header("Bounds")]
    public Vector3 center = Vector3.zero;
    public Vector3 size = new Vector3(4f, 4f, 4f);

    [Header("Padding")]
    public float boundsPadding = 0.25f;

    [Header("Octree")]
    public int maxDepth = 6;
    public int minDepth = 3;

    private int _totalNodes;
    private int _surfaceLeaves;

    public Bounds Bounds
    {
        get
        {
            Vector3 paddedSize = size + Vector3.one * boundsPadding * 2f;
            return new Bounds(center, paddedSize);
        }
    }

    private readonly struct Edge
    {
        public readonly int A;
        public readonly int B;

        public Edge(int a, int b)
        {
            A = a;
            B = b;
        }
    }

    private static readonly Edge[] Edges =
    {
        new Edge(0, 1),
        new Edge(1, 2),
        new Edge(2, 3),
        new Edge(3, 0),

        new Edge(4, 5),
        new Edge(5, 6),
        new Edge(6, 7),
        new Edge(7, 4),

        new Edge(0, 4),
        new Edge(1, 5),
        new Edge(2, 6),
        new Edge(3, 7)
    };

    /// <summary>Builds an adaptive octree by recursively sampling the scalar field.</summary>
    public OctreeVolume Build(IScalarFieldSource source)
    {
        _totalNodes = 0;
        _surfaceLeaves = 0;

        Bounds buildBounds = Bounds;

        Vector3 origin = buildBounds.min;
        Vector3 cellSize = buildBounds.size / (1 << maxDepth);

        OctreeNode root = BuildNode(
            source,
            buildBounds,
            0,
            origin,
            cellSize
        );

        Debug.Log(
            $"Octree Build: nodes={_totalNodes}, surfaceLeaves={_surfaceLeaves}, bounds={buildBounds}"
        );

        return new OctreeVolume(
            root,
            buildBounds,
            maxDepth,
            _totalNodes,
            _surfaceLeaves,
            source,
            origin,
            cellSize
        );
    }

    public bool RebuildRegion(OctreeVolume existing, IScalarFieldSource source, Bounds dirtyBounds, out OctreeVolume rebuilt)
    {
        rebuilt = null;

        if (source == null || existing == null || existing.Root == null)
            return false;

        Bounds buildBounds = Bounds;
        Vector3 origin = buildBounds.min;
        Vector3 cellSize = buildBounds.size / (1 << maxDepth);

        if (existing.Bounds.center != buildBounds.center ||
            existing.Bounds.size != buildBounds.size ||
            existing.MaxDepth != maxDepth ||
            existing.GridOrigin != origin ||
            existing.CellSize != cellSize)
        {
            return false;
        }

        Vector3 eps = cellSize;
        Bounds expandedDirty = dirtyBounds;
        expandedDirty.Expand(eps * 2f);

        OctreeNode root = RebuildNodeRegion(
            existing.Root,
            source,
            expandedDirty,
            0,
            origin,
            cellSize
        );

        int totalNodes = 0;
        int surfaceLeaves = 0;
        CountStats(root, ref totalNodes, ref surfaceLeaves);

        rebuilt = new OctreeVolume(
            root,
            buildBounds,
            maxDepth,
            totalNodes,
            surfaceLeaves,
            source,
            origin,
            cellSize
        );

        return true;
    }

    private OctreeNode RebuildNodeRegion(
        OctreeNode existingNode,
        IScalarFieldSource source,
        Bounds dirtyBounds,
        int depth,
        Vector3 origin,
        Vector3 cellSize)
    {
        if (existingNode == null)
            return null;

        if (!existingNode.Bounds.Intersects(dirtyBounds))
            return existingNode;

        if (existingNode.IsLeaf || existingNode.Children == null || existingNode.Children.Length == 0)
            return BuildNode(source, existingNode.Bounds, depth, origin, cellSize);

        OctreeNode node = new OctreeNode(existingNode.Bounds)
        {
            IsLeaf = false,
            Depth = depth,
            Children = new OctreeNode[8],
            Coord = existingNode.Coord,
            SizeInCells = existingNode.SizeInCells
        };

        for (int i = 0; i < 8; i++)
        {
            OctreeNode child = i < existingNode.Children.Length ? existingNode.Children[i] : null;
            node.Children[i] = RebuildNodeRegion(
                child,
                source,
                dirtyBounds,
                depth + 1,
                origin,
                cellSize
            );
        }

        node.ContainsSurface = AnyChildContainsSurface(node.Children);
        return node;
    }

    private static bool AnyChildContainsSurface(OctreeNode[] children)
    {
        if (children == null)
            return false;

        for (int i = 0; i < children.Length; i++)
        {
            OctreeNode c = children[i];

            if (c == null)
                continue;

            if (c.IsLeaf)
            {
                if (c.ContainsSurface)
                    return true;
            }
            else if (AnyChildContainsSurface(c.Children))
            {
                return true;
            }
        }

        return false;
    }

    private static void CountStats(OctreeNode node, ref int totalNodes, ref int surfaceLeaves)
    {
        if (node == null)
            return;

        totalNodes++;

        if (node.IsLeaf)
        {
            if (node.ContainsSurface)
                surfaceLeaves++;

            return;
        }

        if (node.Children == null)
            return;

        for (int i = 0; i < node.Children.Length; i++)
            CountStats(node.Children[i], ref totalNodes, ref surfaceLeaves);
    }

    /// <summary>Builds one octree node and subdivides it when it may contain surface detail.</summary>
    private OctreeNode BuildNode(
     IScalarFieldSource source,
     Bounds bounds,
     int depth,
     Vector3 origin,
     Vector3 cellSize)
    {
        _totalNodes++;

        OctreeNode node = new OctreeNode(bounds);
        node.Depth = depth;

        float[] corners = SampleCorners(source, bounds);
        float centerValue = source.Evaluate(bounds.center);

        node.CornerValues = corners;
        node.Coord = GetCoord(bounds, origin, cellSize);
        node.SizeInCells = GetSizeInCells(bounds, cellSize);
        node.CenterValue = centerValue;

        bool cornerHasNegative = false;
        bool cornerHasPositive = false;

        for (int i = 0; i < corners.Length; i++)
        {
            if (corners[i] < 0f)
                cornerHasNegative = true;
            else
                cornerHasPositive = true;
        }

        bool cornerContainsSurface = cornerHasNegative && cornerHasPositive;

        bool centerDiffersFromCorners =
            (centerValue < 0f && cornerHasPositive) ||
            (centerValue >= 0f && cornerHasNegative);

        bool couldContainSurface =
            Mathf.Abs(centerValue - 0f) <= bounds.extents.magnitude;

        bool shouldSubdivide =
            depth < minDepth ||
            cornerContainsSurface ||
            centerDiffersFromCorners ||
            couldContainSurface;
        node.ContainsSurface = cornerContainsSurface;

        // Adaptive pruning:
        // Wenn weder Corner-Crossing noch Center-Hinweis vorhanden ist,
        // und minDepth erreicht wurde, stoppen wir früh.
        if (!shouldSubdivide)
        {
            node.IsLeaf = true;
            node.ContainsSurface = false;
            return node;
        }

        // Max depth:
        // Nur echte Corner-Crossing-Zellen werden Surface-Leaves.
        if (depth >= maxDepth)
        {
            node.IsLeaf = true;
            node.ContainsSurface = cornerContainsSurface;

            if (cornerContainsSurface)
            {
                node.SurfaceVertex = EstimateSurfaceVertex(
                    source,
                    bounds,
                    corners,
                    origin,
                    cellSize
                );

                _surfaceLeaves++;
            }

            return node;
        }

        node.IsLeaf = false;
        node.Children = new OctreeNode[8];

        Vector3 childSize = bounds.size * 0.5f;
        Vector3 min = bounds.min;

        int childIndex = 0;

        for (int x = 0; x < 2; x++)
            for (int y = 0; y < 2; y++)
                for (int z = 0; z < 2; z++)
                {
                    Vector3 childCenter = min + new Vector3(
                        (x + 0.5f) * childSize.x,
                        (y + 0.5f) * childSize.y,
                        (z + 0.5f) * childSize.z
                    );

                    Bounds childBounds = new Bounds(childCenter, childSize);

                    node.Children[childIndex++] = BuildNode(
                        source,
                        childBounds,
                        depth + 1,
                        origin,
                        cellSize
                    );
                }

        return node;
    }

    /// <summary>Maps a node bound to its integer coordinate on the global finest grid.</summary>
    private Vector3Int GetCoord(Bounds bounds, Vector3 origin, Vector3 cellSize)
    {
        Vector3 local = bounds.center - origin;

        return new Vector3Int(
            Mathf.RoundToInt(local.x / cellSize.x - 0.5f),
            Mathf.RoundToInt(local.y / cellSize.y - 0.5f),
            Mathf.RoundToInt(local.z / cellSize.z - 0.5f)
        );
    }

    /// <summary>Samples all eight corners of a node bound.</summary>
    private float[] SampleCorners(IScalarFieldSource source, Bounds bounds)
    {
        Vector3[] positions = GetCornerPositions(bounds);

        return new float[]
        {
            source.Evaluate(positions[0]),
            source.Evaluate(positions[1]),
            source.Evaluate(positions[2]),
            source.Evaluate(positions[3]),
            source.Evaluate(positions[4]),
            source.Evaluate(positions[5]),
            source.Evaluate(positions[6]),
            source.Evaluate(positions[7])
        };
    }

    /// <summary>Returns the eight corner positions for a bound.</summary>
    private Vector3[] GetCornerPositions(Bounds bounds)
    {
        Vector3 min = bounds.min;
        Vector3 max = bounds.max;

        return new Vector3[]
        {
            new Vector3(min.x, min.y, min.z),
            new Vector3(max.x, min.y, min.z),
            new Vector3(max.x, max.y, min.z),
            new Vector3(min.x, max.y, min.z),

            new Vector3(min.x, min.y, max.z),
            new Vector3(max.x, min.y, max.z),
            new Vector3(max.x, max.y, max.z),
            new Vector3(min.x, max.y, max.z)
        };
    }

    /// <summary>Estimates a dual-contouring vertex from edge crossing positions.</summary>
    private Vector3 EstimateSurfaceVertex(
        IScalarFieldSource source,
        Bounds bounds,
        float[] cornerValues,
        Vector3 origin,
        Vector3 cellSize)
    {
        Vector3[] cornerPositions = GetCornerPositions(bounds);

        Vector3 sum = Vector3.zero;
        int count = 0;

        for (int i = 0; i < Edges.Length; i++)
        {
            Edge edge = Edges[i];

            float va = cornerValues[edge.A];
            float vb = cornerValues[edge.B];

            if (!HasCrossing(va, vb))
                continue;

            Vector3 pa = cornerPositions[edge.A];
            Vector3 pb = cornerPositions[edge.B];

            float t = va / (va - vb);
            t = Mathf.Clamp01(t);

            Vector3 intersection = Vector3.Lerp(pa, pb, t);

            sum += intersection;
            count++;
        }

        if (count == 0)
            return SnapToGridNearBoundary(bounds.center, origin, cellSize);

        return SnapToGridNearBoundary(sum / count, origin, cellSize);
    }

    /// <summary>Checks whether two scalar samples cross the zero iso surface.</summary>
    private bool HasCrossing(float a, float b)
    {
        return (a <= 0f && b > 0f)
            || (a > 0f && b <= 0f);
    }

    /// <summary>Converts node size into finest-grid cell counts.</summary>
    private Vector3Int GetSizeInCells(Bounds bounds, Vector3 cellSize)
    {
        return new Vector3Int(
            Mathf.Max(1, Mathf.RoundToInt(bounds.size.x / cellSize.x)),
            Mathf.Max(1, Mathf.RoundToInt(bounds.size.y / cellSize.y)),
            Mathf.Max(1, Mathf.RoundToInt(bounds.size.z / cellSize.z))
        );
    }

    private Vector3 SnapToGridNearBoundary(Vector3 p, Vector3 origin, Vector3 cellSize)
    {
        float ex = Mathf.Abs(cellSize.x) * GridSnapEpsilonFactor;
        float ey = Mathf.Abs(cellSize.y) * GridSnapEpsilonFactor;
        float ez = Mathf.Abs(cellSize.z) * GridSnapEpsilonFactor;

        float gx = (p.x - origin.x) / cellSize.x;
        float gy = (p.y - origin.y) / cellSize.y;
        float gz = (p.z - origin.z) / cellSize.z;

        float rx = Mathf.Round(gx);
        float ry = Mathf.Round(gy);
        float rz = Mathf.Round(gz);

        if (Mathf.Abs(gx - rx) <= ex / Mathf.Abs(cellSize.x))
            p.x = origin.x + rx * cellSize.x;

        if (Mathf.Abs(gy - ry) <= ey / Mathf.Abs(cellSize.y))
            p.y = origin.y + ry * cellSize.y;

        if (Mathf.Abs(gz - rz) <= ez / Mathf.Abs(cellSize.z))
            p.z = origin.z + rz * cellSize.z;

        return p;
    }
}
