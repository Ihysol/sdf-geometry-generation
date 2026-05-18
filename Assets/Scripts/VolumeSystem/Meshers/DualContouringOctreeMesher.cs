using System.Collections.Generic;
using UnityEngine;

public class DualContouringOctreeMesher
{
    public float isoLevel = 0f;

    private readonly List<Vector3> _vertices = new();
    private readonly List<int> _triangles = new();

    private readonly Dictionary<Vector3Int, OctreeNode> _leafMap = new();
    private readonly HashSet<EdgeKey> _processedEdges = new();
    private readonly HashSet<Vector3Int> _missingLeafCoords = new();

    private int _skippedNullQuads;
    private int _skippedInvalidQuads;

    private OctreeVolume _volume;
    private int _maxDepth;

    public Bounds? ownedBounds = null;

    private Vector3 _origin;
    private Vector3 _cellSize;
    private Vector3Int _gridMin;
    private Vector3Int _gridMax;
    public System.Collections.Generic.List<Bounds> ownedBoundsList = null;

    private enum Axis
    {
        X,
        Y,
        Z
    }

    private readonly struct CellEdge
    {
        public readonly int A;
        public readonly int B;
        public readonly Axis Axis;
        public readonly Vector3Int GridStart;

        /// <summary>Describes one cube edge and its start coordinate in grid space.</summary>
        public CellEdge(int a, int b, Axis axis, Vector3Int gridStart)
        {
            A = a;
            B = b;
            Axis = axis;
            GridStart = gridStart;
        }
    }

    private readonly struct Edge
    {
        public readonly int A;
        public readonly int B;

        /// <summary>Stores the two corner indices for one cell edge.</summary>
        public Edge(int a, int b)
        {
            A = a;
            B = b;
        }
    }

    private readonly struct EdgeKey
    {
        public readonly Vector3Int Start;
        public readonly Axis Axis;

        /// <summary>Identifies one finest-grid edge for duplicate suppression.</summary>
        public EdgeKey(Vector3Int start, Axis axis)
        {
            Start = start;
            Axis = axis;
        }

        /// <summary>Hashes the grid edge key for the processed-edge set.</summary>
        public override int GetHashCode()
        {
            return Start.GetHashCode() ^ ((int)Axis * 397);
        }

        /// <summary>Compares two grid edge keys by start coordinate and axis.</summary>
        public override bool Equals(object obj)
        {
            if (obj is not EdgeKey other)
                return false;

            return Start == other.Start && Axis == other.Axis;
        }
    }

    private static readonly Edge[] SurfaceEdges =
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

    private static readonly CellEdge[] CellEdges =
    {
        // X
        new CellEdge(0, 1, Axis.X, new Vector3Int(0, 0, 0)),
        new CellEdge(3, 2, Axis.X, new Vector3Int(0, 1, 0)),
        new CellEdge(4, 5, Axis.X, new Vector3Int(0, 0, 1)),
        new CellEdge(7, 6, Axis.X, new Vector3Int(0, 1, 1)),

        // Y
        new CellEdge(0, 3, Axis.Y, new Vector3Int(0, 0, 0)),
        new CellEdge(1, 2, Axis.Y, new Vector3Int(1, 0, 0)),
        new CellEdge(4, 7, Axis.Y, new Vector3Int(0, 0, 1)),
        new CellEdge(5, 6, Axis.Y, new Vector3Int(1, 0, 1)),

        // Z
        new CellEdge(0, 4, Axis.Z, new Vector3Int(0, 0, 0)),
        new CellEdge(1, 5, Axis.Z, new Vector3Int(1, 0, 0)),
        new CellEdge(3, 7, Axis.Z, new Vector3Int(0, 1, 0)),
        new CellEdge(2, 6, Axis.Z, new Vector3Int(1, 1, 0)),
    };

    /// <summary>Builds a Unity mesh from an octree using dual contouring.</summary>
    public void BuildMesh(OctreeVolume volume, Mesh mesh)
    {
        mesh.Clear();

        _vertices.Clear();
        _triangles.Clear();

        _leafMap.Clear();
        _processedEdges.Clear();
        _missingLeafCoords.Clear();

        _skippedNullQuads = 0;
        _skippedInvalidQuads = 0;

        if (volume == null || volume.Root == null)
            return;

        _volume = volume;
        _maxDepth = volume.MaxDepth;

        _origin = volume.GridOrigin;
        _cellSize = volume.CellSize;
        _gridMin = Vector3Int.zero;
        int resolution = 1 << _maxDepth;
        _gridMax = new Vector3Int(
            Mathf.Max(0, resolution - 1),
            Mathf.Max(0, resolution - 1),
            Mathf.Max(0, resolution - 1)
        );

        CollectLeaves(volume.Root);

        CreateVertices();

        BuildEdgeQuads();

        mesh.SetVertices(_vertices);
        mesh.SetTriangles(_triangles, 0);

        mesh.RecalculateNormals();
        mesh.RecalculateBounds();

        Debug.Log(
            $"Octree DC: leaves={_leafMap.Count}, vertices={_vertices.Count}, triangles={_triangles.Count}, nullQuads={_skippedNullQuads}, invalidQuads={_skippedInvalidQuads}"
        );
    }

    /// <summary>Collects surface leaves and resets cached mesh vertex indices.</summary>
    private void CollectLeaves(OctreeNode node)
    {
        if (node == null)
            return;

        if (node.IsLeaf)
        {
            node.MeshVertexIndex = -1;

            if (node.ContainsSurface)
                _leafMap[node.Coord] = node;

            return;
        }

        if (node.Children == null)
            return;

        foreach (OctreeNode child in node.Children)
            CollectLeaves(child);
    }

    /// <summary>Creates initial mesh vertices for all surface leaves.</summary>
    private void CreateVertices()
    {
        foreach (KeyValuePair<Vector3Int, OctreeNode> pair in _leafMap)
        {
            OctreeNode node = pair.Value;

            if (!node.ContainsSurface)
            {
                node.MeshVertexIndex = -1;
                continue;
            }

            node.MeshVertexIndex = _vertices.Count;
            _vertices.Add(node.SurfaceVertex);
        }
    }

    /// <summary>Creates quads around crossed grid edges owned by this mesher pass.</summary>
    private void BuildEdgeQuads()
    {
        List<KeyValuePair<Vector3Int, OctreeNode>> entries =
            new List<KeyValuePair<Vector3Int, OctreeNode>>(_leafMap);

        foreach (KeyValuePair<Vector3Int, OctreeNode> pair in entries)
        {
            Vector3Int cellCoord = pair.Key;
            OctreeNode node = pair.Value;

            if (!node.ContainsSurface)
                continue;

            if (node.CornerValues == null)
                continue;

            for (int i = 0; i < CellEdges.Length; i++)
            {
                CellEdge edge = CellEdges[i];

                float a = node.CornerValues[edge.A];
                float b = node.CornerValues[edge.B];

                if (!HasCrossing(a, b))
                    continue;

                Vector3Int gridEdgeStart = cellCoord + edge.GridStart;

                EdgeKey key = new EdgeKey(
                    gridEdgeStart,
                    edge.Axis
                );

                if (_processedEdges.Contains(key))
                    continue;

                if (ownedBounds.HasValue || ownedBoundsList != null)
                {
                    if (!IsOwnedGridEdgeAny(gridEdgeStart, edge.Axis))
                        continue;
                }

                // Erst NACH Ownership-Test als processed markieren.
                _processedEdges.Add(key);

                BuildQuadForEdge(
                    gridEdgeStart,
                    edge.Axis,
                    a
                );
            }
        }
    }

    /// <summary>Checks whether a grid edge belongs to any active ownership bound.</summary>
    private bool IsOwnedGridEdgeAny(Vector3Int g, Axis axis)
    {
        if (ownedBounds.HasValue && IsOwnedGridEdge(g, axis, ownedBounds.Value))
            return true;

        if (ownedBoundsList != null)
        {
            for (int i = 0; i < ownedBoundsList.Count; i++)
            {
                if (IsOwnedGridEdge(g, axis, ownedBoundsList[i]))
                    return true;
            }
        }

        return false;
    }

    /// <summary>Tests grid-edge ownership against one half-open world-space bound.</summary>
    private bool IsOwnedGridEdge(Vector3Int g, Axis axis, Bounds bounds)
    {
        Vector3Int min = WorldToGridCoord(bounds.min);
        Vector3Int max = WorldToGridCoord(bounds.max);

        int gx2 = g.x * 2 + (axis == Axis.X ? 1 : 0);
        int gy2 = g.y * 2 + (axis == Axis.Y ? 1 : 0);
        int gz2 = g.z * 2 + (axis == Axis.Z ? 1 : 0);

        int minX2 = min.x * 2;
        int minY2 = min.y * 2;
        int minZ2 = min.z * 2;

        int maxX2 = max.x * 2;
        int maxY2 = max.y * 2;
        int maxZ2 = max.z * 2;

        return
            gx2 >= minX2 &&
            gy2 >= minY2 &&
            gz2 >= minZ2 &&
            gx2 < maxX2 &&
            gy2 < maxY2 &&
            gz2 < maxZ2;
    }

    /// <summary>Converts a world position to the nearest finest-grid coordinate.</summary>
    private Vector3Int WorldToGridCoord(Vector3 p)
    {
        Vector3 local = p - _origin;

        return new Vector3Int(
            Mathf.RoundToInt(local.x / _cellSize.x),
            Mathf.RoundToInt(local.y / _cellSize.y),
            Mathf.RoundToInt(local.z / _cellSize.z)
        );
    }

    /// <summary>Finds the four surrounding cells for a crossed edge and emits its quad.</summary>
    private void BuildQuadForEdge(
        Vector3Int g,
        Axis axis,
        float startValue)
    {
        OctreeNode v0;
        OctreeNode v1;
        OctreeNode v2;
        OctreeNode v3;

        bool flip;

        switch (axis)
        {
            case Axis.X:
                {
                    v0 = Get(new Vector3Int(g.x, g.y - 1, g.z - 1));
                    v1 = Get(new Vector3Int(g.x, g.y, g.z - 1));
                    v2 = Get(new Vector3Int(g.x, g.y, g.z));
                    v3 = Get(new Vector3Int(g.x, g.y - 1, g.z));

                    flip = startValue < isoLevel;

                    TryAddQuad(v0, v1, v2, v3, flip);
                    break;
                }

            case Axis.Y:
                {
                    v0 = Get(new Vector3Int(g.x - 1, g.y, g.z - 1));
                    v1 = Get(new Vector3Int(g.x, g.y, g.z - 1));
                    v2 = Get(new Vector3Int(g.x, g.y, g.z));
                    v3 = Get(new Vector3Int(g.x - 1, g.y, g.z));

                    flip = startValue > isoLevel;

                    TryAddQuad(v0, v1, v2, v3, flip);
                    break;
                }

            case Axis.Z:
                {
                    v0 = Get(new Vector3Int(g.x - 1, g.y - 1, g.z));
                    v1 = Get(new Vector3Int(g.x, g.y - 1, g.z));
                    v2 = Get(new Vector3Int(g.x, g.y, g.z));
                    v3 = Get(new Vector3Int(g.x - 1, g.y, g.z));

                    flip = startValue < isoLevel;

                    TryAddQuad(v0, v1, v2, v3, flip);
                    break;
                }
        }
    }

    /// <summary>Adds a quad if all four octree cells resolve to distinct vertices.</summary>
    private bool TryAddQuad(OctreeNode a, OctreeNode b, OctreeNode c, OctreeNode d, bool flip)
    {
        if (a == null || b == null || c == null || d == null)
        {
            _skippedNullQuads++;
            return false;
        }

        if (a == b || a == c || a == d || b == c || b == d || c == d)
        {
            _skippedInvalidQuads++;
            return false;
        }

        EnsureVertex(a);
        EnsureVertex(b);
        EnsureVertex(c);
        EnsureVertex(d);

        int v0 = a.MeshVertexIndex;
        int v1 = b.MeshVertexIndex;
        int v2 = c.MeshVertexIndex;
        int v3 = d.MeshVertexIndex;

        if (v0 < 0 || v1 < 0 || v2 < 0 || v3 < 0)
        {
            _skippedInvalidQuads++;
            return false;
        }

        if (flip)
        {
            _triangles.Add(v0);
            _triangles.Add(v1);
            _triangles.Add(v2);

            _triangles.Add(v0);
            _triangles.Add(v2);
            _triangles.Add(v3);
        }
        else
        {
            _triangles.Add(v0);
            _triangles.Add(v2);
            _triangles.Add(v1);

            _triangles.Add(v0);
            _triangles.Add(v3);
            _triangles.Add(v2);
        }

        return true;
    }

    /// <summary>Creates a fallback mesh vertex for a node when one is not cached yet.</summary>
    private void EnsureVertex(OctreeNode node)
    {
        if (node == null)
            return;

        if (node.MeshVertexIndex >= 0)
            return;

        node.MeshVertexIndex = _vertices.Count;

        if (node.ContainsSurface)
            _vertices.Add(node.SurfaceVertex);
        else
            _vertices.Add(node.Bounds.center);
    }

    /// <summary>Resolves the leaf covering a finest-grid cell, creating a ghost leaf if needed.</summary>
    private OctreeNode Get(Vector3Int coord)
    {
        if (_leafMap.TryGetValue(coord, out OctreeNode node))
            return node;

        if (_missingLeafCoords.Contains(coord))
            return null;

        if (!IsCoordInsideVolumeGrid(coord))
        {
            _missingLeafCoords.Add(coord);
            return null;
        }

        OctreeNode containingLeaf = FindLeafContainingCoord(coord);

        if (containingLeaf != null)
        {
            _leafMap[coord] = containingLeaf;
            return containingLeaf;
        }

        OctreeNode ghost = CreateGhostLeaf(coord);

        if (ghost == null)
            _missingLeafCoords.Add(coord);

        return ghost;
    }

    private bool IsCoordInsideVolumeGrid(Vector3Int coord)
    {
        return
            coord.x >= _gridMin.x && coord.x <= _gridMax.x &&
            coord.y >= _gridMin.y && coord.y <= _gridMax.y &&
            coord.z >= _gridMin.z && coord.z <= _gridMax.z;
    }



    /// <summary>Finds the adaptive octree leaf that contains a finest-grid coordinate.</summary>
    private OctreeNode FindLeafContainingCoord(Vector3Int coord)
    {
        Vector3 point = _origin + new Vector3(
            (coord.x + 0.5f) * _cellSize.x,
            (coord.y + 0.5f) * _cellSize.y,
            (coord.z + 0.5f) * _cellSize.z
        );

        if (!IsInsideVolumeBounds(point))
            return null;

        return FindLeafContainingPoint(_volume.Root, point);
    }
    /// <summary>Checks whether a point is inside the source volume bounds with a cell-sized tolerance.</summary>
    private bool IsInsideVolumeBounds(Vector3 p)
    {
        Bounds b = _volume.Bounds;

        float eps = Mathf.Max(
            _cellSize.x,
            Mathf.Max(_cellSize.y, _cellSize.z)
        ) * 0.5f;

        return
            p.x >= b.min.x - eps &&
            p.y >= b.min.y - eps &&
            p.z >= b.min.z - eps &&
            p.x <= b.max.x + eps &&
            p.y <= b.max.y + eps &&
            p.z <= b.max.z + eps;
    }

    /// <summary>Walks down the octree to find the leaf containing a point.</summary>
    private OctreeNode FindLeafContainingPoint(OctreeNode node, Vector3 point)
    {
        while (node != null && !node.IsLeaf)
        {
            if (node.Children == null)
                return node;

            Vector3 c = node.Bounds.center;

            int index = 0;

            if (point.x >= c.x)
                index += 4;

            if (point.y >= c.y)
                index += 2;

            if (point.z >= c.z)
                index += 1;

            OctreeNode child = node.Children[index];

            if (child == null)
                return node;

            node = child;
        }

        return node;
    }

    /// <summary>Samples a missing finest-grid leaf so boundary quads can be completed.</summary>
    private OctreeNode CreateGhostLeaf(Vector3Int coord)
    {
        if (_volume == null || _volume.Source == null)
            return null;

        Vector3 min = _origin + new Vector3(
            coord.x * _cellSize.x,
            coord.y * _cellSize.y,
            coord.z * _cellSize.z
        );

        Bounds bounds = new Bounds(
            min + _cellSize * 0.5f,
            _cellSize
        );

        if (!IsInsideVolumeBounds(bounds.center))
            return null;

        OctreeNode ghost = new OctreeNode(bounds);

        ghost.IsLeaf = true;
        ghost.Depth = _maxDepth;
        ghost.Coord = coord;

        float[] corners = SampleCorners(
            _volume.Source,
            bounds
        );

        ghost.CornerValues = corners;

        bool hasNegative = false;
        bool hasPositive = false;

        for (int i = 0; i < corners.Length; i++)
        {
            if (corners[i] < isoLevel)
                hasNegative = true;
            else
                hasPositive = true;
        }

        ghost.ContainsSurface = hasNegative && hasPositive;

        ghost.SurfaceVertex = ghost.ContainsSurface
            ? EstimateSurfaceVertex(bounds, corners)
            : bounds.center;

        _leafMap[coord] = ghost;

        return ghost;
    }

    /// <summary>Samples all eight corners of a finest-grid leaf.</summary>
    private float[] SampleCorners(
        IScalarFieldSource source,
        Bounds bounds)
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
        Bounds bounds,
        float[] cornerValues)
    {
        Vector3[] positions = GetCornerPositions(bounds);

        Vector3 sum = Vector3.zero;
        int count = 0;

        for (int i = 0; i < SurfaceEdges.Length; i++)
        {
            Edge edge = SurfaceEdges[i];

            float va = cornerValues[edge.A];
            float vb = cornerValues[edge.B];

            if (!HasCrossing(va, vb))
                continue;

            Vector3 pa = positions[edge.A];
            Vector3 pb = positions[edge.B];

            float t = (isoLevel - va) / (vb - va);
            t = Mathf.Clamp01(t);

            Vector3 p = Vector3.Lerp(pa, pb, t);

            sum += p;
            count++;
        }

        if (count == 0)
            return bounds.center;

        return sum / count;
    }

    /// <summary>Checks whether two scalar samples cross the active iso level.</summary>
    private bool HasCrossing(float a, float b)
    {
        float da = a - isoLevel;
        float db = b - isoLevel;

        return (da <= 0f && db > 0f)
            || (da > 0f && db <= 0f);
    }
}
