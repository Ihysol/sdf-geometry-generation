using System.Collections.Generic;
using UnityEngine;

public class DualContouringOctreeMesher
{
    public float isoLevel = 0f;

    private readonly List<Vector3> _vertices = new();
    private readonly List<int> _triangles = new();

    private readonly Dictionary<Vector3Int, OctreeNode> _leafMap = new();
    private readonly HashSet<EdgeKey> _processedEdges = new();

    private int _skippedNullQuads;
    private int _skippedInvalidQuads;

    private OctreeVolume _volume;
    private int _maxDepth;

    private Vector3 _origin;
    private Vector3 _cellSize;
    private int _resolution;

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

        public EdgeKey(Vector3Int start, Axis axis)
        {
            Start = start;
            Axis = axis;
        }

        public override int GetHashCode()
        {
            return Start.GetHashCode() ^ ((int)Axis * 397);
        }

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

    public void BuildMesh(OctreeVolume volume, Mesh mesh)
    {
        mesh.Clear();

        _vertices.Clear();
        _triangles.Clear();

        _leafMap.Clear();
        _processedEdges.Clear();

        _skippedNullQuads = 0;
        _skippedInvalidQuads = 0;

        if (volume == null || volume.Root == null)
            return;

        _volume = volume;
        _maxDepth = volume.MaxDepth;

        _resolution = 1 << _maxDepth;

        _origin = volume.Bounds.min;
        _cellSize = volume.Bounds.size / _resolution;

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

    private void BuildEdgeQuads()
    {
        foreach (KeyValuePair<Vector3Int, OctreeNode> pair in _leafMap)
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

                _processedEdges.Add(key);

                BuildQuadForEdge(
                    gridEdgeStart,
                    edge.Axis,
                    a
                );
            }
        }
    }

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

    private OctreeNode Get(Vector3Int coord)
    {
        if (_leafMap.TryGetValue(coord, out OctreeNode node))
            return node;

        OctreeNode containingLeaf = FindLeafContainingCoord(coord);

        if (containingLeaf != null)
        {
            _leafMap[coord] = containingLeaf;
            return containingLeaf;
        }

        return CreateGhostLeaf(coord);
    }


    private OctreeNode FindLeafContainingCoord(Vector3Int coord)
    {
        if (coord.x < 0 || coord.y < 0 || coord.z < 0 ||
            coord.x >= _resolution ||
            coord.y >= _resolution ||
            coord.z >= _resolution)
            return null;

        Vector3 point = _origin + new Vector3(
            (coord.x + 0.5f) * _cellSize.x,
            (coord.y + 0.5f) * _cellSize.y,
            (coord.z + 0.5f) * _cellSize.z
        );

        return FindLeafContainingPoint(_volume.Root, point);
    }

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

    private OctreeNode CreateGhostLeaf(Vector3Int coord)
    {
        if (_volume == null || _volume.Source == null)
            return null;

        if (coord.x < 0 || coord.y < 0 || coord.z < 0 ||
            coord.x >= _resolution ||
            coord.y >= _resolution ||
            coord.z >= _resolution)
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

        if (ghost.ContainsSurface)
        {
            ghost.SurfaceVertex = EstimateSurfaceVertex(
                bounds,
                corners
            );
        }
        else
        {
            ghost.SurfaceVertex = bounds.center;
        }

        _leafMap[coord] = ghost;

        return ghost;
    }

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

    private bool HasCrossing(float a, float b)
    {
        float da = a - isoLevel;
        float db = b - isoLevel;

        return (da <= 0f && db > 0f)
            || (da > 0f && db <= 0f);
    }
}