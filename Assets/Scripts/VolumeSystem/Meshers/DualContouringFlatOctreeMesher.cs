using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;

public class DualContouringFlatOctreeMesher : IVolumeMesher<OctreeVolume>
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
    private readonly List<int> _triangles = new();
    private readonly HashSet<EdgeKey> _processedEdges = new();
    private readonly Dictionary<Vector3Int, int> _leafExactByCoord = new();
    private readonly Dictionary<Vector3Int, int> _resolvedLeafByCoord = new();
    private readonly HashSet<Vector3Int> _missingLeafCoords = new();
    private readonly List<int> _surfaceLeafIndices = new();
    private readonly List<GridBounds> _ownedGridBounds = new();
    private int[] _meshVertexIndexByNode;
    private int[] _subtreeSize;
    private int[] _childIndexByOctant;
    private Vector3Int[] _nodeSizeInCells;

    private FlatOctreeLayout _layout;
    private Vector3 _origin;
    private Vector3 _cellSize;
    private Vector3Int _gridMin;
    private Vector3Int _gridMax;
    private int _skippedNullQuads;
    private int _skippedInvalidQuads;
    private int _resolveLeafCalls;
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

    public void BuildMesh(OctreeVolume volume, float iso, Mesh mesh)
    {
        mesh.Clear();
        _vertices.Clear();
        _triangles.Clear();
        _processedEdges.Clear();
        _leafExactByCoord.Clear();
        _resolvedLeafByCoord.Clear();
        _missingLeafCoords.Clear();
        _surfaceLeafIndices.Clear();
        _ownedGridBounds.Clear();
        _skippedNullQuads = 0;
        _skippedInvalidQuads = 0;
        _resolveLeafCalls = 0;
        _resolveLeafCacheHits = 0;
        _resolveLeafExactHits = 0;
        _resolveLeafFindCalls = 0;
        _findContainingLeafSteps = 0;
        isoLevel = iso;

        if (volume == null || volume.Root == null || volume is not IFlatAdaptiveVolumeData flatVolume)
            return;

        _layout = flatVolume.GetFlatLayout(includeCornerValues: true);
        if (_layout == null || _layout.Count == 0 || _layout.Flags == null || _layout.Coords == null || _layout.Centers == null || _layout.Sizes == null || _layout.CornerValues8 == null)
            return;

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
        double lookupVerticesMs = 0d;
        double treeNavMs = 0d;
        double edgeQuadsMs = 0d;
        if (enableDebugLog && UnityEngine.Debug.isDebugBuild)
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
        BuildFlatLookupAndVertices();
#if UNITY_EDITOR
        if (phaseSw != null)
        {
            lookupVerticesMs = phaseSw.Elapsed.TotalMilliseconds;
            phaseSw.Restart();
        }
#endif
        BuildTreeNavigation();
#if UNITY_EDITOR
        if (phaseSw != null)
        {
            treeNavMs = phaseSw.Elapsed.TotalMilliseconds;
            phaseSw.Restart();
        }
#endif
        BuildEdgeQuads();
#if UNITY_EDITOR
        if (phaseSw != null)
            edgeQuadsMs = phaseSw.Elapsed.TotalMilliseconds;
#endif

        mesh.SetVertices(_vertices);
        mesh.SetTriangles(_triangles, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();

#if UNITY_EDITOR
        if (enableDebugLog && UnityEngine.Debug.isDebugBuild)
        {
            if (totalSw != null)
                totalSw.Stop();
            float avgFindSteps = _resolveLeafFindCalls > 0 ? (float)_findContainingLeafSteps / _resolveLeafFindCalls : 0f;
            UnityEngine.Debug.Log(
                $"Flat Octree DC: total={(totalSw != null ? totalSw.Elapsed.TotalMilliseconds : 0d):F2} ms, cacheOwned={cacheOwnedMs:F2} ms, lookupVertices={lookupVerticesMs:F2} ms, treeNav={treeNavMs:F2} ms, edgeQuads={edgeQuadsMs:F2} ms, leaves={_surfaceLeafIndices.Count}, vertices={_vertices.Count}, triangles={_triangles.Count}, nullQuads={_skippedNullQuads}, invalidQuads={_skippedInvalidQuads}, resolveCalls={_resolveLeafCalls}, resolveCacheHits={_resolveLeafCacheHits}, resolveExactHits={_resolveLeafExactHits}, resolveFindCalls={_resolveLeafFindCalls}, avgFindSteps={avgFindSteps:F2}");
        }
#endif
    }

    private void EnsureWorkingBuffers(int count)
    {
        if (_meshVertexIndexByNode == null || _meshVertexIndexByNode.Length != count)
            _meshVertexIndexByNode = new int[count];
        if (_subtreeSize == null || _subtreeSize.Length != count)
            _subtreeSize = new int[count];
        if (_childIndexByOctant == null || _childIndexByOctant.Length != count * 8)
            _childIndexByOctant = new int[count * 8];
        if (_nodeSizeInCells == null || _nodeSizeInCells.Length != count)
            _nodeSizeInCells = new Vector3Int[count];
        for (int i = 0; i < count; i++)
        {
            _meshVertexIndexByNode[i] = -1;
            _subtreeSize[i] = 0;
            _nodeSizeInCells[i] = Vector3Int.zero;
        }
        for (int i = 0; i < _childIndexByOctant.Length; i++)
            _childIndexByOctant[i] = -1;
    }

    private void BuildFlatLookupAndVertices()
    {
        for (int i = 0; i < _layout.Count; i++)
        {
            if ((_layout.Flags[i] & FlatOctreeLayout.FlagLeaf) == 0)
                continue;

            _leafExactByCoord[_layout.Coords[i]] = i;
            if ((_layout.Flags[i] & FlatOctreeLayout.FlagSurface) == 0)
                continue;

            _surfaceLeafIndices.Add(i);
            _meshVertexIndexByNode[i] = _vertices.Count;
            _vertices.Add(_layout.SurfaceVertices != null && i < _layout.SurfaceVertices.Length ? _layout.SurfaceVertices[i] : _layout.Centers[i]);
        }
    }

    private void BuildTreeNavigation()
    {
        ComputeSubtreeSize(0);
    }

    private int ComputeSubtreeSize(int nodeIndex)
    {
        if (nodeIndex < 0 || nodeIndex >= _layout.Count)
            return 0;
        if (_subtreeSize[nodeIndex] > 0)
            return _subtreeSize[nodeIndex];

        bool isLeaf = (_layout.Flags[nodeIndex] & FlatOctreeLayout.FlagLeaf) != 0;
        if (isLeaf)
        {
            _subtreeSize[nodeIndex] = 1;
            return 1;
        }

        int first = _layout.FirstChildIndex[nodeIndex];
        int mask = _layout.ChildMask[nodeIndex];
        int cursor = first;
        int size = 1;

        for (int oct = 0; oct < 8; oct++)
        {
            if ((mask & (1 << oct)) == 0)
                continue;

            _childIndexByOctant[nodeIndex * 8 + oct] = cursor;
            int childSize = ComputeSubtreeSize(cursor);
            size += childSize;
            cursor += childSize;
        }

        _subtreeSize[nodeIndex] = size;
        return size;
    }

    private void BuildEdgeQuads()
    {
        bool hasOwnedBounds = _ownedGridBounds.Count > 0;
        for (int si = 0; si < _surfaceLeafIndices.Count; si++)
        {
            int nodeIndex = _surfaceLeafIndices[si];
            Vector3Int cellCoord = _layout.Coords[nodeIndex];
            int baseIdx = nodeIndex * 8;

            for (int i = 0; i < CellEdges.Length; i++)
            {
                CellEdge edge = CellEdges[i];
                float a = _layout.CornerValues8[baseIdx + edge.A];
                float b = _layout.CornerValues8[baseIdx + edge.B];

                if (!HasCrossing(a, b))
                    continue;

                Vector3Int gridEdgeStart = cellCoord + edge.GridStart;
                EdgeKey key = new EdgeKey(gridEdgeStart, edge.Axis);
                if (_processedEdges.Contains(key))
                    continue;

            if (hasOwnedBounds)
            {
                if (!IsOwnedGridEdgeAny(gridEdgeStart, edge.Axis))
                    continue;
            }

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

        if (_resolvedLeafByCoord.TryGetValue(coord, out int cached))
        {
            _resolveLeafCacheHits++;
            return cached;
        }
        if (_missingLeafCoords.Contains(coord))
            return -1;

        if (_leafExactByCoord.TryGetValue(coord, out int exact))
        {
            _resolveLeafExactHits++;
            _resolvedLeafByCoord[coord] = exact;
            return exact;
        }

        _resolveLeafFindCalls++;
        int containing = FindContainingLeaf(coord);
        if (containing >= 0)
            _resolvedLeafByCoord[coord] = containing;
        else
            _missingLeafCoords.Add(coord);
        return containing;
    }

    private int FindContainingLeaf(Vector3Int coord)
    {
        int node = 0;
        int steps = 0;
        while (true)
        {
            steps++;
            bool isLeaf = (_layout.Flags[node] & FlatOctreeLayout.FlagLeaf) != 0;
            if (isLeaf)
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

            int child = _childIndexByOctant[node * 8 + oct];
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
        Vector3Int cached = _nodeSizeInCells[nodeIndex];
        if (cached.x > 0 && cached.y > 0 && cached.z > 0)
            return cached;

        Vector3 s = _layout.Sizes[nodeIndex];
        int sx = Mathf.Max(1, Mathf.RoundToInt(Mathf.Abs(s.x / _cellSize.x)));
        int sy = Mathf.Max(1, Mathf.RoundToInt(Mathf.Abs(s.y / _cellSize.y)));
        int sz = Mathf.Max(1, Mathf.RoundToInt(Mathf.Abs(s.z / _cellSize.z)));
        Vector3Int size = new Vector3Int(sx, sy, sz);
        _nodeSizeInCells[nodeIndex] = size;
        return size;
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
        bool hasSurface = (_layout.Flags[nodeIndex] & FlatOctreeLayout.FlagSurface) != 0;
        _vertices.Add(hasSurface && _layout.SurfaceVertices != null && nodeIndex < _layout.SurfaceVertices.Length
            ? _layout.SurfaceVertices[nodeIndex]
            : _layout.Centers[nodeIndex]);
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
