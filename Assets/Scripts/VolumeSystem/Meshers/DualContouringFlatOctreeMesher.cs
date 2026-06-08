using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;

public class DualContouringFlatOctreeMesher : IVolumeMesher<IFlatAdaptiveVolumeData>
{
    public float isoLevel = 0f;
    public bool enableDebugLog = true;
    public Bounds? ownedBounds = null;
    public List<Bounds> ownedBoundsList = null;

    public bool useQefVertices = true;
    public QefVertexMode qefVertexMode = QefVertexMode.QefAxisSnap;
    public float qefBlendFactor = 0.5f;
    public float qefSnapEpsilon = 0.015f;
    public float qefMaxOffsetCells = 0.75f;
    public float qefAxisSnapStrength = 2.5f;
    public bool qefEnableMultiHermite = false;
    public int qefHermiteSamplesPerEdge = 3;

    private readonly List<Vector3> _vertices = new();
    private readonly List<Vector3> _normals = new();
    private readonly List<int> _triangles = new();
    private readonly HashSet<EdgeKey> _processedEdges = new();
    private readonly List<GridBounds> _ownedGridBounds = new();
    private readonly Dictionary<Vector3, Vector3> _normalByVertex = new();
    private int[] _meshVertexIndexByNode;

    private FlatOctreeLayout _layout;
    private IScalarFieldSource _source;
    private Vector3 _origin;
    private Vector3 _cellSize;
    private Vector3Int _gridMin;
    private Vector3Int _gridMax;
    private int _skippedNullQuads;
    private int _skippedInvalidQuads;
    private int _resolveLeafCalls;
    private int _resolveLeafDenseHits;
    private int _resolveLeafCacheHits;
    private int _resolveLeafExactHits;
    private int _resolveLeafFindCalls;
    private int _findContainingLeafSteps;

    private enum Axis { X, Y, Z }

    private readonly struct CellEdge
    {
        public readonly int A;
        public readonly int B;
        public readonly Axis Axis;
        public readonly Vector3Int GridStart;
        public CellEdge(int a, int b, Axis axis, Vector3Int gridStart) { A = a; B = b; Axis = axis; GridStart = gridStart; }
    }

    private readonly struct EdgeKey
    {
        public readonly Vector3Int Start;
        public readonly Axis Axis;
        public EdgeKey(Vector3Int start, Axis axis) { Start = start; Axis = axis; }
        public override int GetHashCode() => Start.GetHashCode() ^ ((int)Axis * 397);
        public override bool Equals(object obj) => obj is EdgeKey other && Start == other.Start && Axis == other.Axis;
    }

    private readonly struct GridBounds
    {
        public readonly Vector3Int Min;
        public readonly Vector3Int Max;
        public GridBounds(Vector3Int min, Vector3Int max)
        {
            Min = min;
            Max = max;
        }
    }

    private static readonly CellEdge[] CellEdges =
    {
        new CellEdge(0, 1, Axis.X, new Vector3Int(0, 0, 0)),
        new CellEdge(3, 2, Axis.X, new Vector3Int(0, 1, 0)),
        new CellEdge(4, 5, Axis.X, new Vector3Int(0, 0, 1)),
        new CellEdge(7, 6, Axis.X, new Vector3Int(0, 1, 1)),
        new CellEdge(0, 3, Axis.Y, new Vector3Int(0, 0, 0)),
        new CellEdge(1, 2, Axis.Y, new Vector3Int(1, 0, 0)),
        new CellEdge(4, 7, Axis.Y, new Vector3Int(0, 0, 1)),
        new CellEdge(5, 6, Axis.Y, new Vector3Int(1, 0, 1)),
        new CellEdge(0, 4, Axis.Z, new Vector3Int(0, 0, 0)),
        new CellEdge(1, 5, Axis.Z, new Vector3Int(1, 0, 0)),
        new CellEdge(3, 7, Axis.Z, new Vector3Int(0, 1, 0)),
        new CellEdge(2, 6, Axis.Z, new Vector3Int(1, 1, 0)),
    };

    public void BuildMesh(IFlatAdaptiveVolumeData volume, float iso, Mesh mesh)
    {
        mesh.Clear();
        _vertices.Clear();
        _normals.Clear();
        _triangles.Clear();
        _processedEdges.Clear();
        _ownedGridBounds.Clear();
        _normalByVertex.Clear();
        _skippedNullQuads = 0;
        _skippedInvalidQuads = 0;
        _resolveLeafCalls = 0;
        _resolveLeafDenseHits = 0;
        _resolveLeafCacheHits = 0;
        _resolveLeafExactHits = 0;
        _resolveLeafFindCalls = 0;
        _findContainingLeafSteps = 0;
        isoLevel = iso;

        if (volume == null)
            return;

        _layout = volume.GetFlatLayout(includeCornerValues: true);
        if (_layout == null || _layout.Count == 0 || !_layout.IsValid)
            return;

        _source = volume.Source;
        int count = _layout.Count;
        EnsureWorkingBuffers(count);

        _origin = volume.GridOrigin;
        _cellSize = volume.CellSize;
        _gridMin = Vector3Int.zero;
        int resolution = 1 << volume.MaxDepth;
        _gridMax = new Vector3Int(Mathf.Max(0, resolution - 1), Mathf.Max(0, resolution - 1), Mathf.Max(0, resolution - 1));

#if UNITY_EDITOR
        Stopwatch totalSw = null;
        Stopwatch phaseSw = null;
        double cacheOwnedMs = 0d;
        double runtimeCacheMs = 0d;
        double edgeQuadsMs = 0d;
        if (enableDebugLog)
        {
            totalSw = Stopwatch.StartNew();
            phaseSw = Stopwatch.StartNew();
        }
#endif

        CacheOwnedGridBounds();
#if UNITY_EDITOR
        if (phaseSw != null)
        {
            cacheOwnedMs = phaseSw.Elapsed.TotalMilliseconds;
            phaseSw.Restart();
        }
#endif
        _layout.EnsureRuntimeCache();
#if UNITY_EDITOR
        if (phaseSw != null)
        {
            runtimeCacheMs = phaseSw.Elapsed.TotalMilliseconds;
            phaseSw.Restart();
        }
#endif
        BuildEdgeQuads();
#if UNITY_EDITOR
        if (phaseSw != null)
            edgeQuadsMs = phaseSw.Elapsed.TotalMilliseconds;
#endif

        mesh.SetVertices(_vertices);
        if (_normals.Count == _vertices.Count)
            mesh.SetNormals(_normals);
        mesh.SetTriangles(_triangles, 0);

#if UNITY_EDITOR
        if (enableDebugLog)
        {
            if (totalSw != null)
                totalSw.Stop();
            float avgFindSteps = _resolveLeafFindCalls > 0 ? (float)_findContainingLeafSteps / _resolveLeafFindCalls : 0f;
            UnityEngine.Debug.Log(
                $"Flat Octree DC: total={(totalSw != null ? totalSw.Elapsed.TotalMilliseconds : 0d):F2} ms, cacheOwned={cacheOwnedMs:F2} ms, runtimeCache={runtimeCacheMs:F2} ms, edgeQuads={edgeQuadsMs:F2} ms, leaves={_layout.SurfaceLeafIndices.Length}, vertices={_vertices.Count}, triangles={_triangles.Count}, nullQuads={_skippedNullQuads}, invalidQuads={_skippedInvalidQuads}, resolveCalls={_resolveLeafCalls}, resolveDenseHits={_resolveLeafDenseHits}, resolveCacheHits={_resolveLeafCacheHits}, resolveExactHits={_resolveLeafExactHits}, resolveFindCalls={_resolveLeafFindCalls}, avgFindSteps={avgFindSteps:F2}");
        }
#endif
    }

    private void EnsureWorkingBuffers(int count)
    {
        if (_meshVertexIndexByNode == null || _meshVertexIndexByNode.Length != count)
            _meshVertexIndexByNode = new int[count];
        for (int i = 0; i < count; i++)
            _meshVertexIndexByNode[i] = -1;
    }

    private void BuildEdgeQuads()
    {
        bool hasOwnedBounds = _ownedGridBounds.Count > 0;
        int[] surfaceLeafIndices = _layout.SurfaceLeafIndices;
        for (int si = 0; si < surfaceLeafIndices.Length; si++)
        {
            int nodeIndex = surfaceLeafIndices[si];
            if (hasOwnedBounds && !NodeMayTouchOwnedBounds(nodeIndex))
                continue;

            Vector3Int cellCoord = _layout.Coords[nodeIndex];

            for (int i = 0; i < CellEdges.Length; i++)
            {
                CellEdge edge = CellEdges[i];
                float a = _layout.GetCornerValue(nodeIndex, edge.A);
                float b = _layout.GetCornerValue(nodeIndex, edge.B);

                if (!HasCrossing(a, b))
                    continue;

                Vector3Int gridEdgeStart = cellCoord + edge.GridStart;
                EdgeKey key = new EdgeKey(gridEdgeStart, edge.Axis);
                if (_processedEdges.Contains(key))
                    continue;

                if (hasOwnedBounds && !IsOwnedGridEdgeAny(gridEdgeStart, edge.Axis))
                    continue;

                _processedEdges.Add(key);
                BuildQuadForEdge(gridEdgeStart, edge.Axis, a);
            }
        }
    }

    private void BuildQuadForEdge(Vector3Int g, Axis axis, float startValue)
    {
        int v0, v1, v2, v3;
        bool flip;

        switch (axis)
        {
            case Axis.X:
                v0 = ResolveLeaf(new Vector3Int(g.x, g.y - 1, g.z - 1));
                v1 = ResolveLeaf(new Vector3Int(g.x, g.y, g.z - 1));
                v2 = ResolveLeaf(new Vector3Int(g.x, g.y, g.z));
                v3 = ResolveLeaf(new Vector3Int(g.x, g.y - 1, g.z));
                flip = startValue < isoLevel;
                break;
            case Axis.Y:
                v0 = ResolveLeaf(new Vector3Int(g.x - 1, g.y, g.z - 1));
                v1 = ResolveLeaf(new Vector3Int(g.x, g.y, g.z - 1));
                v2 = ResolveLeaf(new Vector3Int(g.x, g.y, g.z));
                v3 = ResolveLeaf(new Vector3Int(g.x - 1, g.y, g.z));
                flip = startValue > isoLevel;
                break;
            default:
                v0 = ResolveLeaf(new Vector3Int(g.x - 1, g.y - 1, g.z));
                v1 = ResolveLeaf(new Vector3Int(g.x, g.y - 1, g.z));
                v2 = ResolveLeaf(new Vector3Int(g.x, g.y, g.z));
                v3 = ResolveLeaf(new Vector3Int(g.x - 1, g.y, g.z));
                flip = startValue < isoLevel;
                break;
        }

        TryAddQuad(v0, v1, v2, v3, flip);
    }

    private int ResolveLeaf(Vector3Int coord)
    {
        _resolveLeafCalls++;
        if (!IsCoordInsideVolumeGrid(coord))
            return -1;

        if (_layout.TryGetContainingLeafIndex(coord, out int denseLeaf))
        {
            _resolveLeafDenseHits++;
            return denseLeaf;
        }

        if (_layout.ResolvedLeafByCoord.TryGetValue(coord, out int cached))
        {
            _resolveLeafCacheHits++;
            return cached;
        }
        if (_layout.MissingLeafCoords.Contains(coord))
            return -1;

        if (_layout.LeafExactByCoord.TryGetValue(coord, out int exact))
        {
            _resolveLeafExactHits++;
            _layout.ResolvedLeafByCoord[coord] = exact;
            return exact;
        }

        _resolveLeafFindCalls++;
        int containing = FindContainingLeaf(coord);
        if (containing >= 0)
            _layout.ResolvedLeafByCoord[coord] = containing;
        else
            _layout.MissingLeafCoords.Add(coord);
        return containing;
    }

    private int FindContainingLeaf(Vector3Int coord)
    {
        int node = 0;
        int steps = 0;
        while (true)
        {
            steps++;
            if (_layout.IsLeaf(node))
            {
                _findContainingLeafSteps += steps;
                return node;
            }

            Vector3Int nodeCoord = _layout.Coords[node];
            Vector3Int sizeCells = GetNodeSizeInCells(node);

            int hx = Mathf.Max(1, sizeCells.x / 2);
            int hy = Mathf.Max(1, sizeCells.y / 2);
            int hz = Mathf.Max(1, sizeCells.z / 2);

            int ox = coord.x >= nodeCoord.x + hx ? 1 : 0;
            int oy = coord.y >= nodeCoord.y + hy ? 1 : 0;
            int oz = coord.z >= nodeCoord.z + hz ? 1 : 0;
            int oct = (ox << 2) | (oy << 1) | oz;

            int child = _layout.ChildIndexByOctant[node * 8 + oct];
            if (child < 0 || child >= _layout.Count)
            {
                _findContainingLeafSteps += steps;
                return -1;
            }
            node = child;
        }
    }

    private Vector3Int GetNodeSizeInCells(int nodeIndex)
    {
        return _layout.GetNodeSizeInCells(nodeIndex);
    }

    private bool TryAddQuad(int a, int b, int c, int d, bool flip)
    {
        if (a < 0 || b < 0 || c < 0 || d < 0)
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

        int v0 = _meshVertexIndexByNode[a];
        int v1 = _meshVertexIndexByNode[b];
        int v2 = _meshVertexIndexByNode[c];
        int v3 = _meshVertexIndexByNode[d];

        if (v0 < 0 || v1 < 0 || v2 < 0 || v3 < 0)
        {
            _skippedInvalidQuads++;
            return false;
        }

        if (flip)
        {
            _triangles.Add(v0); _triangles.Add(v1); _triangles.Add(v2);
            _triangles.Add(v0); _triangles.Add(v2); _triangles.Add(v3);
        }
        else
        {
            _triangles.Add(v0); _triangles.Add(v2); _triangles.Add(v1);
            _triangles.Add(v0); _triangles.Add(v3); _triangles.Add(v2);
        }
        return true;
    }

    private void EnsureVertex(int nodeIndex)
    {
        if (_meshVertexIndexByNode[nodeIndex] >= 0)
            return;

        _meshVertexIndexByNode[nodeIndex] = _vertices.Count;
        Vector3 vertex = _layout.GetSurfaceVertexOrCenter(nodeIndex);
        _vertices.Add(vertex);
        _normals.Add(EstimateNormal(vertex));
    }

    private Vector3 EstimateNormal(Vector3 position)
    {
        if (_source == null)
            return Vector3.up;

        if (_normalByVertex.TryGetValue(position, out Vector3 cached))
            return cached;

        float h = Mathf.Max(1e-4f, Mathf.Min(_cellSize.x, Mathf.Min(_cellSize.y, _cellSize.z)) * 0.5f);
        Vector3 dx = new Vector3(h, 0f, 0f);
        Vector3 dy = new Vector3(0f, h, 0f);
        Vector3 dz = new Vector3(0f, 0f, h);

        Vector3 n = new Vector3(
            _source.Evaluate(position + dx) - _source.Evaluate(position - dx),
            _source.Evaluate(position + dy) - _source.Evaluate(position - dy),
            _source.Evaluate(position + dz) - _source.Evaluate(position - dz)
        );

        if (n.sqrMagnitude <= 1e-12f)
            n = Vector3.up;
        else
            n.Normalize();

        _normalByVertex[position] = n;
        return n;
    }

    private bool IsCoordInsideVolumeGrid(Vector3Int coord)
    {
        return coord.x >= _gridMin.x && coord.x <= _gridMax.x &&
               coord.y >= _gridMin.y && coord.y <= _gridMax.y &&
               coord.z >= _gridMin.z && coord.z <= _gridMax.z;
    }

    private bool IsOwnedGridEdgeAny(Vector3Int g, Axis axis)
    {
        if (_ownedGridBounds.Count == 0)
            return true;

        for (int i = 0; i < _ownedGridBounds.Count; i++)
        {
            if (IsOwnedGridEdge(g, axis, _ownedGridBounds[i]))
                return true;
        }
        return false;
    }

    private bool NodeMayTouchOwnedBounds(int nodeIndex)
    {
        if (_ownedGridBounds.Count == 0)
            return true;

        Vector3Int min = _layout.Coords[nodeIndex] - Vector3Int.one;
        Vector3Int max = _layout.Coords[nodeIndex] + GetNodeSizeInCells(nodeIndex) + Vector3Int.one;

        for (int i = 0; i < _ownedGridBounds.Count; i++)
        {
            GridBounds b = _ownedGridBounds[i];
            if (min.x <= b.Max.x && max.x >= b.Min.x &&
                min.y <= b.Max.y && max.y >= b.Min.y &&
                min.z <= b.Max.z && max.z >= b.Min.z)
                return true;
        }

        return false;
    }

    private bool IsOwnedGridEdge(Vector3Int g, Axis axis, GridBounds bounds)
    {
        int gx2 = g.x * 2 + (axis == Axis.X ? 1 : 0);
        int gy2 = g.y * 2 + (axis == Axis.Y ? 1 : 0);
        int gz2 = g.z * 2 + (axis == Axis.Z ? 1 : 0);
        return gx2 >= bounds.Min.x * 2 && gy2 >= bounds.Min.y * 2 && gz2 >= bounds.Min.z * 2 &&
               gx2 < bounds.Max.x * 2 && gy2 < bounds.Max.y * 2 && gz2 < bounds.Max.z * 2;
    }

    private void CacheOwnedGridBounds()
    {
        if (ownedBounds.HasValue)
            _ownedGridBounds.Add(new GridBounds(WorldToGridCoord(ownedBounds.Value.min), WorldToGridCoord(ownedBounds.Value.max)));

        if (ownedBoundsList == null)
            return;

        for (int i = 0; i < ownedBoundsList.Count; i++)
        {
            Bounds b = ownedBoundsList[i];
            _ownedGridBounds.Add(new GridBounds(WorldToGridCoord(b.min), WorldToGridCoord(b.max)));
        }
    }

    private Vector3Int WorldToGridCoord(Vector3 p)
    {
        Vector3 local = p - _origin;
        return new Vector3Int(
            Mathf.RoundToInt(local.x / _cellSize.x),
            Mathf.RoundToInt(local.y / _cellSize.y),
            Mathf.RoundToInt(local.z / _cellSize.z));
    }

    private bool HasCrossing(float a, float b)
    {
        float da = a - isoLevel;
        float db = b - isoLevel;
        return (da <= 0f && db > 0f) || (da > 0f && db <= 0f);
    }
}
