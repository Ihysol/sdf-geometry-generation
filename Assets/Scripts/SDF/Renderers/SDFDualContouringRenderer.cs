using System.Collections.Generic;
using UnityEngine;
using System.Diagnostics;

[RequireComponent(typeof(SDFSampler))]
[RequireComponent(typeof(MeshFilter))]
[RequireComponent(typeof(MeshRenderer))]
public class SDFDualContouringRenderer : MonoBehaviour
{
    [Header("Iso Surface")]
    public float isoLevel = 0f;
    [Header("Rebuild")]
    public bool rebuildEveryFrame = false;
    public bool autoRebuildOnChange = true;
    public bool recalculateNormals = true;
    [Header("Debug")]
    public bool showGizmos = true;


    private SDFSampler _sampler;
    private MeshFilter _meshFilter;
    private Mesh _mesh;

    private readonly List<Vector3> _vertices = new();
    private readonly List<int> _triangles = new();

    private int[] _cellVertexIndex;
    private readonly float[] _cellValues = new float[8];
    private readonly Vector3[] _cellPositions = new Vector3[8];

    private Vector3Int _lastCellArraySize;
    private float _lastIsoLevel;

    private static readonly Vector3Int[] CornerOffsets =
    {
        new Vector3Int(0, 0, 0),
        new Vector3Int(1, 0, 0),
        new Vector3Int(1, 1, 0),
        new Vector3Int(0, 1, 0),
        new Vector3Int(0, 0, 1),
        new Vector3Int(1, 0, 1),
        new Vector3Int(1, 1, 1),
        new Vector3Int(0, 1, 1)
    };

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
        new Edge(0,1), new Edge(1,2), new Edge(2,3), new Edge(3,0),
        new Edge(4,5), new Edge(5,6), new Edge(6,7), new Edge(7,4),
        new Edge(0,4), new Edge(1,5), new Edge(2,6), new Edge(3,7)
    };

    private static int CellIndex(int x, int y, int z, Vector3Int cells)
    {
        return x + cells.x * (y + cells.y * z);
    }

    private void EnsureReferences()
    {
        if (_sampler == null)
            _sampler = GetComponent<SDFSampler>();
        if (_meshFilter == null)
            _meshFilter = GetComponent<MeshFilter>();
        if (_mesh == null)
        {
            _mesh = new Mesh();
            _mesh.name = "SDF Dual Contouring Mesh";
            _mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            _mesh.MarkDynamic();
            _meshFilter.sharedMesh = _mesh;
        }
    }

    private bool HasCrossing(float a, float b)
    {
        float da = a - isoLevel;
        float db = b - isoLevel;

        return (da <= 0f && db > 0f) || (da > 0f && db <= 0f);
    }

    private Vector3 Interpolate(Vector3 pa, float va, Vector3 pb, float vb)
    {
        float denom = vb - va;
        if (Mathf.Abs(denom) < 1e-8f)
            return (pa + pb) * 0.5f;

        float t = Mathf.Clamp01((isoLevel - va) / denom);
        return Vector3.Lerp(pa, pb, t);
    }

    private int CreateCellVertex(float[] distances, Vector3Int size, Vector3 origin, Vector3 cellSize, int cellX, int cellY, int cellZ)
    {
        Vector3 sum = Vector3.zero;
        int count = 0;

        float minValue = float.PositiveInfinity;
        float maxValue = float.NegativeInfinity;

        int sizeX = size.x;
        int sizeY = size.y;

        for (int i = 0; i < 8; i++)
        {
            Vector3Int o = CornerOffsets[i];

            int sx = cellX + o.x;
            int sy = cellY + o.y;
            int sz = cellZ + o.z;

            int index = sx + sizeX * (sy + sizeY * sz);

            float distance = distances[index];

            Vector3 position = origin + new Vector3(
                sx * cellSize.x,
                sy * cellSize.y,
                sz * cellSize.z
            );

            _cellValues[i] = distance;
            _cellPositions[i] = position;

            if (distance < minValue) minValue = distance;
            if (distance > maxValue) maxValue = distance;
        }

        if (minValue > isoLevel || maxValue < isoLevel)
            return -1;

        for (int e = 0; e < 12; e++)
        {
            Edge edge = Edges[e];

            float va = _cellValues[edge.A];
            float vb = _cellValues[edge.B];

            if (!HasCrossing(va, vb))
                continue;

            Vector3 pa = _cellPositions[edge.A];
            Vector3 pb = _cellPositions[edge.B];

            Vector3 p = Interpolate(pa, va, pb, vb);

            sum += p;
            count++;
        }

        if (count == 0)
            return -1;

        Vector3 vertex = sum / count;

        int vertexIndex = _vertices.Count;
        _vertices.Add(vertex);

        return vertexIndex;
    }

    private void AddQuad(int v0, int v1, int v2, int v3, bool flip)
    {
        if (v0 < 0 || v1 < 0 || v2 < 0 || v3 < 0)
            return;

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
    }

    private bool IsDirty()
    {
        if (_sampler == null)
            return true;
        if (_sampler.IsDirty)
            return true;
        if (_sampler.Volume == null)
            return true;
        if (!Mathf.Approximately(_lastIsoLevel, isoLevel))
            return true;
        return false;
    }

    private void CacheState()
    {
        _lastIsoLevel = isoLevel;
    }

    private void BuildQuads(float[] distances, Vector3Int size, Vector3Int cells)
    {
        int sizeX = size.x;
        int sizeY = size.y;
        int zStride = size.x * size.y;

        // X-axis grid edges
        for (int x = 0; x < cells.x; x++)
        {
            for (int y = 1; y < cells.y; y++)
            {
                for (int z = 1; z < cells.z; z++)
                {
                    int baseIndex = x + sizeX * (y + sizeY * z);

                    float a = distances[baseIndex];
                    float b = distances[baseIndex + 1];

                    if (!HasCrossing(a, b))
                        continue;

                    int v0 = _cellVertexIndex[CellIndex(x, y - 1, z - 1, cells)];
                    int v1 = _cellVertexIndex[CellIndex(x, y, z - 1, cells)];
                    int v2 = _cellVertexIndex[CellIndex(x, y, z, cells)];
                    int v3 = _cellVertexIndex[CellIndex(x, y - 1, z, cells)];

                    AddQuad(v0, v1, v2, v3, a < isoLevel);
                }
            }
        }

        // Y-axis grid edges
        for (int x = 1; x < cells.x; x++)
        {
            for (int y = 0; y < cells.y; y++)
            {
                for (int z = 1; z < cells.z; z++)
                {

                    int baseIndex = x + sizeX * (y + sizeY * z);

                    float a = distances[baseIndex];
                    float b = distances[baseIndex + sizeX];

                    if (!HasCrossing(a, b))
                        continue;

                    int v0 = _cellVertexIndex[CellIndex(x - 1, y, z - 1, cells)];
                    int v1 = _cellVertexIndex[CellIndex(x, y, z - 1, cells)];
                    int v2 = _cellVertexIndex[CellIndex(x, y, z, cells)];
                    int v3 = _cellVertexIndex[CellIndex(x - 1, y, z, cells)];

                    AddQuad(v0, v1, v2, v3, a > isoLevel);
                }
            }
        }

        // Z-axis grid edges
        for (int x = 1; x < cells.x; x++)
        {
            for (int y = 1; y < cells.y; y++)
            {
                for (int z = 0; z < cells.z; z++)
                {

                    int baseIndex = x + sizeX * (y + sizeY * z);

                    float a = distances[baseIndex];
                    float b = distances[baseIndex + zStride];

                    if (!HasCrossing(a, b))
                        continue;

                    int v0 = _cellVertexIndex[CellIndex(x - 1, y - 1, z, cells)];
                    int v1 = _cellVertexIndex[CellIndex(x, y - 1, z, cells)];
                    int v2 = _cellVertexIndex[CellIndex(x, y, z, cells)];
                    int v3 = _cellVertexIndex[CellIndex(x - 1, y, z, cells)];

                    AddQuad(v0, v1, v2, v3, a < isoLevel);
                }
            }
        }
    }

    [ContextMenu("Rebuild Mesh")]
    public void RebuildMesh()
    {
        Stopwatch sw = Stopwatch.StartNew();
        EnsureReferences();

        long tStart = sw.ElapsedMilliseconds;
        if (_sampler.IsDirty || _sampler.Volume == null)
            _sampler.RebuildVolume();

        VoxelGrid volume = _sampler.Volume;
        if (volume == null)
        {
            _mesh.Clear(false);
            CacheState();
            return;
        }

        float[] distances = volume.Distances;
        Vector3Int size = volume.GridSize;
        Vector3 origin = volume.Origin;
        Vector3 cellSize = volume.CellSize;

        long tSamples = sw.ElapsedMilliseconds;
        Vector3Int samples = volume.GridSize;
        Vector3Int cells = samples - Vector3Int.one;

        if (cells.x <= 0 || cells.y <= 0 || cells.z <= 0)
        {
            _mesh.Clear(false);
            CacheState();
            return;
        }

        int estimatedCells = cells.x * cells.y * cells.z;

        if (_vertices.Capacity < estimatedCells)
            _vertices.Capacity = estimatedCells;
        if (_triangles.Capacity < estimatedCells * 6)
            _triangles.Capacity = estimatedCells * 6;

        _vertices.Clear();
        _triangles.Clear();

        if (_cellVertexIndex == null || _lastCellArraySize != cells)
        {
            _cellVertexIndex = new int[estimatedCells];
            _lastCellArraySize = cells;
        }

        System.Array.Fill(_cellVertexIndex, -1);
        long tPrepare = sw.ElapsedMilliseconds;

        for (int x = 0; x < cells.x; x++)
        {
            for (int y = 0; y < cells.y; y++)
            {
                for (int z = 0; z < cells.z; z++)
                {
                    int index = CellIndex(x, y, z, cells);
                    _cellVertexIndex[index] = CreateCellVertex(distances, size, origin, cellSize, x, y, z);
                }
            }
        }
        long tVertices = sw.ElapsedMilliseconds;

        BuildQuads(distances, size, cells);
        long tQuads = sw.ElapsedMilliseconds;

        _mesh.Clear(false);
        _mesh.SetVertices(_vertices);
        _mesh.SetTriangles(_triangles, 0);
        if (recalculateNormals)
            _mesh.RecalculateNormals();
        // _mesh.RecalculateBounds();
        _mesh.bounds = new Bounds(Vector3.zero, _sampler.gridExtent);
        long tUpload = sw.ElapsedMilliseconds;

        sw.Stop();

        UnityEngine.Debug.Log(
            $"[DualContouring] Total: {sw.Elapsed.TotalMilliseconds:F2} ms | " +
            $"Samples: {tSamples - tStart} ms | " +
            $"Prepare: {tPrepare - tSamples} ms | " +
            $"Vertices: {tVertices - tPrepare} ms | " +
            $"Quads: {tQuads - tVertices} ms | " +
            $"Upload: {tUpload - tQuads} ms | " +
            $"Verts: {_vertices.Count} | " +
            $"Tris: {_triangles.Count / 3}"
        );

        CacheState();
    }

    public void DestroyMesh()
    {
        if (_mesh != null)
        {
#if UNITY_EDITOR
            if (!Application.isPlaying)
                DestroyImmediate(_mesh);
            else
#endif
                Destroy(_mesh);
        }

        if (_meshFilter != null)
            _meshFilter.sharedMesh = null;

        _mesh = null;
    }


    private void Awake()
    {
        EnsureReferences();
    }

    private void OnEnable()
    {
        EnsureReferences();
    }

    private void Update()
    {
        if (rebuildEveryFrame || (autoRebuildOnChange && IsDirty()))
        {
            RebuildMesh();
        }
    }

    private void OnDrawGizmos()
    {
        if (!showGizmos)
            return;

        if (_sampler == null)
            _sampler = GetComponent<SDFSampler>();
        if (_sampler == null)
            return;

        Gizmos.matrix = transform.localToWorldMatrix;
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireCube(Vector3.zero, _sampler.gridExtent);
        Gizmos.matrix = Matrix4x4.identity;
    }
}