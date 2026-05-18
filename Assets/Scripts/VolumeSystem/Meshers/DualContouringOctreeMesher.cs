using System.Collections.Generic;
using UnityEngine;

public class DualContouringOctreeMesher : IVolumeMesher<OctreeVolume>
{
    public bool useQefVertices = true;
    public QefVertexMode qefVertexMode = QefVertexMode.QefAxisSnap;
    public float qefBlendFactor = 0.5f;
    public float qefSnapEpsilon = 0.015f;
    public float qefMaxOffsetCells = 0.75f;
    public float qefAxisSnapStrength = 2.5f;
    public bool qefEnableMultiHermite = false;
    public int qefHermiteSamplesPerEdge = 3;
    public float isoLevel = 0f;

    private readonly List<Vector3> _vertices = new();
    private readonly List<int> _triangles = new();

    private readonly Dictionary<Vector3Int, OctreeNode> _leafMap = new();
    private readonly HashSet<EdgeKey> _processedEdges = new();
    private readonly HashSet<Vector3Int> _missingLeafCoords = new();
    private readonly Dictionary<Vector3Int, float> _cornerSampleCache = new();
    private readonly Dictionary<HermiteEdgeKey, HermiteSample> _hermiteSampleCache = new();

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
    private readonly List<Vector3> _qefPoints = new(12);
    private readonly List<Vector3> _qefNormals = new(12);
    private readonly List<float> _qefWeights = new(12);

    private readonly struct HermiteSample
    {
        public readonly Vector3 Point;
        public readonly Vector3 Normal;
        public readonly float Weight;

        public HermiteSample(Vector3 point, Vector3 normal, float weight)
        {
            Point = point;
            Normal = normal;
            Weight = weight;
        }
    }

    private readonly struct HermiteEdgeKey
    {
        public readonly Vector3Int A;
        public readonly Vector3Int B;

        public HermiteEdgeKey(Vector3Int a, Vector3Int b)
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
            if (obj is not HermiteEdgeKey other)
                return false;

            return A == other.A && B == other.B;
        }

        private static bool LexicographicLessOrEqual(Vector3Int x, Vector3Int y)
        {
            if (x.x != y.x) return x.x < y.x;
            if (x.y != y.y) return x.y < y.y;
            return x.z <= y.z;
        }
    }

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
    public void BuildMesh(OctreeVolume volume, float iso, Mesh mesh)
    {
        isoLevel = iso;
        mesh.Clear();

        _vertices.Clear();
        _triangles.Clear();

        _leafMap.Clear();
        _processedEdges.Clear();
        _missingLeafCoords.Clear();
        _cornerSampleCache.Clear();
        _hermiteSampleCache.Clear();

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

#if UNITY_EDITOR
        if (UnityEngine.Debug.isDebugBuild)
        {
            Debug.Log(
                $"Octree DC: leaves={_leafMap.Count}, vertices={_vertices.Count}, triangles={_triangles.Count}, nullQuads={_skippedNullQuads}, invalidQuads={_skippedInvalidQuads}"
            );
        }
#endif
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

        float[] corners = SampleCorners(_volume.Source, coord);

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
            ? EstimateSurfaceVertex(bounds, corners, _volume.Source, coord)
            : bounds.center;

        _leafMap[coord] = ghost;

        return ghost;
    }

    /// <summary>Samples all eight corners of a finest-grid leaf.</summary>
    private float[] SampleCorners(IScalarFieldSource source, Vector3Int cellCoord)
    {
        Vector3Int c000 = new Vector3Int(cellCoord.x, cellCoord.y, cellCoord.z);
        Vector3Int c100 = new Vector3Int(cellCoord.x + 1, cellCoord.y, cellCoord.z);
        Vector3Int c110 = new Vector3Int(cellCoord.x + 1, cellCoord.y + 1, cellCoord.z);
        Vector3Int c010 = new Vector3Int(cellCoord.x, cellCoord.y + 1, cellCoord.z);
        Vector3Int c001 = new Vector3Int(cellCoord.x, cellCoord.y, cellCoord.z + 1);
        Vector3Int c101 = new Vector3Int(cellCoord.x + 1, cellCoord.y, cellCoord.z + 1);
        Vector3Int c111 = new Vector3Int(cellCoord.x + 1, cellCoord.y + 1, cellCoord.z + 1);
        Vector3Int c011 = new Vector3Int(cellCoord.x, cellCoord.y + 1, cellCoord.z + 1);

        return new float[]
        {
            EvaluateCornerCached(source, c000),
            EvaluateCornerCached(source, c100),
            EvaluateCornerCached(source, c110),
            EvaluateCornerCached(source, c010),
            EvaluateCornerCached(source, c001),
            EvaluateCornerCached(source, c101),
            EvaluateCornerCached(source, c111),
            EvaluateCornerCached(source, c011)
        };
    }

    private float EvaluateCornerCached(IScalarFieldSource source, Vector3Int gridVertex)
    {
        if (_cornerSampleCache.TryGetValue(gridVertex, out float cached))
            return cached;

        Vector3 worldPos = _origin + new Vector3(
            gridVertex.x * _cellSize.x,
            gridVertex.y * _cellSize.y,
            gridVertex.z * _cellSize.z
        );

        float value = source.Evaluate(worldPos);
        _cornerSampleCache[gridVertex] = value;
        return value;
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
        float[] cornerValues,
        IScalarFieldSource source,
        Vector3Int cellCoord)
    {
        Vector3[] positions = GetCornerPositions(bounds);
        Vector3Int[] cornerCoords = GetCellCornerCoords(cellCoord);

        Vector3 sum = Vector3.zero;
        int count = 0;
        _qefPoints.Clear();
        _qefNormals.Clear();
        _qefWeights.Clear();

        for (int i = 0; i < SurfaceEdges.Length; i++)
        {
            Edge edge = SurfaceEdges[i];

            float va = cornerValues[edge.A];
            float vb = cornerValues[edge.B];

            if (!HasCrossing(va, vb))
                continue;

            Vector3 pa = positions[edge.A];
            Vector3 pb = positions[edge.B];
            Vector3Int ca = cornerCoords[edge.A];
            Vector3Int cb = cornerCoords[edge.B];

            AddHermiteSamplesForEdge(source, pa, pb, va, vb, ca, cb, isoLevel, ref sum, ref count);
        }

        Vector3 avg = count == 0 ? bounds.center : (sum / count);
        bool useQef = useQefVertices && qefVertexMode != QefVertexMode.AverageCrossings;
        bool useAdaptiveBlend = qefVertexMode == QefVertexMode.QefFeaturePreserving || qefVertexMode == QefVertexMode.QefAxisSnap;
        bool useAxisSnap = qefVertexMode == QefVertexMode.QefAxisSnap;

        if (useQef &&
            _qefPoints.Count >= 3 &&
            HasSufficientGradientDiversity(_qefNormals) &&
            QefSolver.TrySolve(_qefPoints, _qefNormals, _qefWeights, bounds, out Vector3 qef))
        {
            qef = ConstrainQefToLocalWindow(qef, avg, bounds);
            if (IsQefSolutionAcceptable(qef, avg, bounds))
            {
                float blend = useAdaptiveBlend ? GetAdaptiveQefBlend(_qefNormals) : Mathf.Clamp01(qefBlendFactor);
                Vector3 blended = Vector3.Lerp(avg, qef, blend);
                Vector3 result = useAxisSnap ? SnapAxisAlignedFeature(blended, _qefNormals) : blended;
                if (useAxisSnap)
                    result = SnapToGridNearBoundaryWithFactor(result, qefAxisSnapStrength);
                return SnapToGridNearBoundary(result);
            }
        }


        if (count == 0)
            return SnapToGridNearBoundary(bounds.center);

        Vector3 avgResult = useAxisSnap ? SnapAxisAlignedFeature(avg, _qefNormals) : avg;
        if (useAxisSnap)
            avgResult = SnapToGridNearBoundaryWithFactor(avgResult, qefAxisSnapStrength);
        return SnapToGridNearBoundary(avgResult);
    }

    private Vector3 ConstrainQefToLocalWindow(Vector3 qef, Vector3 avg, Bounds bounds)
    {
        float maxCell = Mathf.Max(Mathf.Abs(_cellSize.x), Mathf.Max(Mathf.Abs(_cellSize.y), Mathf.Abs(_cellSize.z)));
        float window = Mathf.Max(maxCell * Mathf.Max(0f, qefMaxOffsetCells), 1e-4f);

        Vector3 constrained = new Vector3(
            Mathf.Clamp(qef.x, avg.x - window, avg.x + window),
            Mathf.Clamp(qef.y, avg.y - window, avg.y + window),
            Mathf.Clamp(qef.z, avg.z - window, avg.z + window)
        );

        constrained.x = Mathf.Clamp(constrained.x, bounds.min.x, bounds.max.x);
        constrained.y = Mathf.Clamp(constrained.y, bounds.min.y, bounds.max.y);
        constrained.z = Mathf.Clamp(constrained.z, bounds.min.z, bounds.max.z);
        return constrained;
    }

    /// <summary>Checks whether two scalar samples cross the active iso level.</summary>
    private bool HasCrossing(float a, float b)
    {
        float da = a - isoLevel;
        float db = b - isoLevel;

        return (da <= 0f && db > 0f)
            || (da > 0f && db <= 0f);
    }

    private Vector3 SnapToGridNearBoundary(Vector3 p)
    {
        float sx = Mathf.Abs(_cellSize.x);
        float sy = Mathf.Abs(_cellSize.y);
        float sz = Mathf.Abs(_cellSize.z);

        if (sx <= 0f || sy <= 0f || sz <= 0f)
            return p;

        float gx = (p.x - _origin.x) / _cellSize.x;
        float gy = (p.y - _origin.y) / _cellSize.y;
        float gz = (p.z - _origin.z) / _cellSize.z;

        float rx = Mathf.Round(gx);
        float ry = Mathf.Round(gy);
        float rz = Mathf.Round(gz);

        float t = Mathf.Max(0f, qefSnapEpsilon);
        float tx = t;
        float ty = t;
        float tz = t;

        if (Mathf.Abs(gx - rx) <= tx)
            p.x = _origin.x + rx * _cellSize.x;

        if (Mathf.Abs(gy - ry) <= ty)
            p.y = _origin.y + ry * _cellSize.y;

        if (Mathf.Abs(gz - rz) <= tz)
            p.z = _origin.z + rz * _cellSize.z;

        return p;
    }

    private Vector3 SnapToGridNearBoundaryWithFactor(Vector3 p, float factor)
    {
        float sx = Mathf.Abs(_cellSize.x);
        float sy = Mathf.Abs(_cellSize.y);
        float sz = Mathf.Abs(_cellSize.z);

        if (sx <= 0f || sy <= 0f || sz <= 0f)
            return p;

        float gx = (p.x - _origin.x) / _cellSize.x;
        float gy = (p.y - _origin.y) / _cellSize.y;
        float gz = (p.z - _origin.z) / _cellSize.z;

        float rx = Mathf.Round(gx);
        float ry = Mathf.Round(gy);
        float rz = Mathf.Round(gz);

        float t = Mathf.Max(0f, qefSnapEpsilon) * Mathf.Max(1f, factor);

        if (Mathf.Abs(gx - rx) <= t)
            p.x = _origin.x + rx * _cellSize.x;
        if (Mathf.Abs(gy - ry) <= t)
            p.y = _origin.y + ry * _cellSize.y;
        if (Mathf.Abs(gz - rz) <= t)
            p.z = _origin.z + rz * _cellSize.z;

        return p;
    }

    private Vector3 RefineEdgeIntersection(IScalarFieldSource source, Vector3 pa, Vector3 pb, float va, float vb, float iso)
    {
        if (source == null)
            return Vector3.Lerp(pa, pb, 0.5f);

        float fa = va - iso;
        float fb = vb - iso;

        if (Mathf.Abs(fa) < 1e-8f)
            return pa;
        if (Mathf.Abs(fb) < 1e-8f)
            return pb;

        float t = fa / (fa - fb);
        t = Mathf.Clamp01(t);
        Vector3 best = Vector3.Lerp(pa, pb, t);

        Vector3 a = pa;
        Vector3 b = pb;
        float fA = fa;

        for (int i = 0; i < 3; i++)
        {
            Vector3 mid = (a + b) * 0.5f;
            float fM = source.Evaluate(mid) - iso;
            best = mid;

            if (Mathf.Abs(fM) < 1e-6f)
                break;

            if ((fA <= 0f && fM > 0f) || (fA > 0f && fM <= 0f))
            {
                b = mid;
            }
            else
            {
                a = mid;
                fA = fM;
            }
        }

        return best;
    }

    private HermiteSample GetHermiteSample(
        IScalarFieldSource source,
        Vector3 pa,
        Vector3 pb,
        float va,
        float vb,
        Vector3Int ca,
        Vector3Int cb,
        float isoLevel)
    {
        HermiteEdgeKey key = new HermiteEdgeKey(ca, cb);
        if (_hermiteSampleCache.TryGetValue(key, out HermiteSample cached))
            return cached;

        Vector3 p = RefineEdgeIntersection(source, pa, pb, va, vb, isoLevel);
        Vector3 g = EstimateGradientVector(source, p);
        float strength = g.magnitude;
        HermiteSample sample = new HermiteSample(
            p,
            SafeNormalize(g),
            Mathf.Max(0.05f, strength)
        );

        _hermiteSampleCache[key] = sample;
        return sample;
    }

    private void AddHermiteSamplesForEdge(
        IScalarFieldSource source,
        Vector3 pa,
        Vector3 pb,
        float va,
        float vb,
        Vector3Int ca,
        Vector3Int cb,
        float isoLevel,
        ref Vector3 sum,
        ref int count)
    {
        HermiteSample center = GetHermiteSample(source, pa, pb, va, vb, ca, cb, isoLevel);
        sum += center.Point;
        count++;
        _qefPoints.Add(center.Point);
        _qefNormals.Add(center.Normal);
        _qefWeights.Add(center.Weight);

        if (!qefEnableMultiHermite)
            return;

        int samples = Mathf.Max(1, qefHermiteSamplesPerEdge);
        if (samples <= 1)
            return;

        float denom = vb - va;
        if (Mathf.Abs(denom) < 1e-8f)
            return;
        float baseT = Mathf.Clamp01((isoLevel - va) / denom);
        float span = 0.2f;
        float step = (samples == 2) ? 0f : (2f * span / (samples - 1));

        for (int i = 0; i < samples; i++)
        {
            float offset = -span + step * i;
            if (Mathf.Abs(offset) < 1e-6f)
                continue;

            float t = Mathf.Clamp01(baseT + offset);
            Vector3 p = Vector3.Lerp(pa, pb, t);
            Vector3 g = EstimateGradientVector(source, p);
            float w = Mathf.Max(0.02f, g.magnitude * 0.35f);
            Vector3 n = SafeNormalize(g);

            sum += p;
            count++;
            _qefPoints.Add(p);
            _qefNormals.Add(n);
            _qefWeights.Add(w);
        }
    }

    private static Vector3Int[] GetCellCornerCoords(Vector3Int cellCoord)
    {
        return new Vector3Int[]
        {
            new Vector3Int(cellCoord.x, cellCoord.y, cellCoord.z),
            new Vector3Int(cellCoord.x + 1, cellCoord.y, cellCoord.z),
            new Vector3Int(cellCoord.x + 1, cellCoord.y + 1, cellCoord.z),
            new Vector3Int(cellCoord.x, cellCoord.y + 1, cellCoord.z),
            new Vector3Int(cellCoord.x, cellCoord.y, cellCoord.z + 1),
            new Vector3Int(cellCoord.x + 1, cellCoord.y, cellCoord.z + 1),
            new Vector3Int(cellCoord.x + 1, cellCoord.y + 1, cellCoord.z + 1),
            new Vector3Int(cellCoord.x, cellCoord.y + 1, cellCoord.z + 1)
        };
    }

    private Vector3 SnapAxisAlignedFeature(Vector3 p, List<Vector3> normals)
    {
        if (normals == null || normals.Count == 0)
            return p;

        Vector3 sum = Vector3.zero;
        for (int i = 0; i < normals.Count; i++)
            sum += normals[i];

        Vector3 mean = sum.normalized;
        float ax = Mathf.Abs(mean.x);
        float ay = Mathf.Abs(mean.y);
        float az = Mathf.Abs(mean.z);
        float dominant = Mathf.Max(ax, Mathf.Max(ay, az));

        if (dominant < 0.75f)
            return p;

        float strengthen = Mathf.Lerp(1.15f, 1.8f, Mathf.InverseLerp(0.75f, 1f, dominant));
        float eps = Mathf.Max(0f, qefSnapEpsilon) * strengthen;

        float gx = (p.x - _origin.x) / _cellSize.x;
        float gy = (p.y - _origin.y) / _cellSize.y;
        float gz = (p.z - _origin.z) / _cellSize.z;

        if (ax >= ay && ax >= az)
        {
            float r = Mathf.Round(gx);
            if (Mathf.Abs(gx - r) <= eps)
                p.x = _origin.x + r * _cellSize.x;
        }
        else if (ay >= ax && ay >= az)
        {
            float r = Mathf.Round(gy);
            if (Mathf.Abs(gy - r) <= eps)
                p.y = _origin.y + r * _cellSize.y;
        }
        else
        {
            float r = Mathf.Round(gz);
            if (Mathf.Abs(gz - r) <= eps)
                p.z = _origin.z + r * _cellSize.z;
        }

        return p;
    }

    private Vector3 EstimateGradientVector(IScalarFieldSource source, Vector3 p)
    {
        if (source == null)
            return Vector3.zero;

        float hx = Mathf.Max(Mathf.Abs(_cellSize.x), 1e-4f) * 0.5f;
        float hy = Mathf.Max(Mathf.Abs(_cellSize.y), 1e-4f) * 0.5f;
        float hz = Mathf.Max(Mathf.Abs(_cellSize.z), 1e-4f) * 0.5f;

        float dx = source.Evaluate(p + new Vector3(hx, 0f, 0f)) - source.Evaluate(p - new Vector3(hx, 0f, 0f));
        float dy = source.Evaluate(p + new Vector3(0f, hy, 0f)) - source.Evaluate(p - new Vector3(0f, hy, 0f));
        float dz = source.Evaluate(p + new Vector3(0f, 0f, hz)) - source.Evaluate(p - new Vector3(0f, 0f, hz));

        return new Vector3(dx, dy, dz);
    }

    private static Vector3 SafeNormalize(Vector3 v)
    {
        float len = v.magnitude;
        if (len < 1e-8f)
            return Vector3.up;
        return v / len;
    }

    private bool IsQefSolutionAcceptable(Vector3 qef, Vector3 avg, Bounds bounds)
    {
        float maxCell = Mathf.Max(Mathf.Abs(_cellSize.x), Mathf.Max(Mathf.Abs(_cellSize.y), Mathf.Abs(_cellSize.z)));
        float maxAllowedOffset = Mathf.Max(maxCell * Mathf.Max(0f, qefMaxOffsetCells), 1e-4f);

        if ((qef - avg).magnitude > maxAllowedOffset)
            return false;

        if (!bounds.Contains(qef))
            return false;

        return true;
    }

    private bool HasSufficientGradientDiversity(List<Vector3> normals)
    {
        if (normals == null || normals.Count < 3)
            return false;
        GetNormalEigenvalues(normals, out float l1, out float l2, out float l3);
        return l1 > 1e-4f && (l2 + l3) > 0.02f;
    }

    private float GetAdaptiveQefBlend(List<Vector3> normals)
    {
        float baseBlend = Mathf.Clamp01(qefBlendFactor);
        if (normals == null || normals.Count < 3)
            return baseBlend * 0.35f;
        GetNormalEigenvalues(normals, out float l1, out float l2, out float l3);
        float featureStrength = l1 > 1e-6f ? Mathf.Clamp01((l2 + l3) / l1) : 0f;
        float scale = Mathf.Lerp(0.35f, 1f, featureStrength);
        return baseBlend * scale;
    }

    private static void GetNormalEigenvalues(List<Vector3> normals, out float l1, out float l2, out float l3)
    {
        float c00 = 0f, c01 = 0f, c02 = 0f, c11 = 0f, c12 = 0f, c22 = 0f;
        int n = 0;
        for (int i = 0; i < normals.Count; i++)
        {
            Vector3 v = normals[i];
            float len = v.magnitude;
            if (len < 1e-8f)
                continue;
            v /= len;
            c00 += v.x * v.x; c01 += v.x * v.y; c02 += v.x * v.z;
            c11 += v.y * v.y; c12 += v.y * v.z; c22 += v.z * v.z;
            n++;
        }
        if (n == 0) { l1 = l2 = l3 = 0f; return; }
        float inv = 1f / n;
        c00 *= inv; c01 *= inv; c02 *= inv; c11 *= inv; c12 *= inv; c22 *= inv;
        for (int it = 0; it < 6; it++)
        {
            Rotate(ref c00, ref c01, ref c11);
            Rotate(ref c00, ref c02, ref c22);
            Rotate(ref c11, ref c12, ref c22);
        }
        l1 = c00; l2 = c11; l3 = c22;
        if (l1 < l2) Swap(ref l1, ref l2);
        if (l2 < l3) Swap(ref l2, ref l3);
        if (l1 < l2) Swap(ref l1, ref l2);
    }

    private static void Rotate(ref float app, ref float apq, ref float aqq)
    {
        if (Mathf.Abs(apq) < 1e-6f)
            return;
        float phi = 0.5f * Mathf.Atan2(2f * apq, aqq - app);
        float c = Mathf.Cos(phi);
        float s = Mathf.Sin(phi);
        float app2 = c * c * app - 2f * s * c * apq + s * s * aqq;
        float aqq2 = s * s * app + 2f * s * c * apq + c * c * aqq;
        app = app2;
        aqq = aqq2;
        apq = 0f;
    }

    private static void Swap(ref float a, ref float b)
    {
        float t = a; a = b; b = t;
    }
}
