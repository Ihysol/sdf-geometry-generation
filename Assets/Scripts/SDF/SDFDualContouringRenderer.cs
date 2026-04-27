using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(SDFSampler))]
[RequireComponent(typeof(MeshFilter))]
[RequireComponent(typeof(MeshRenderer))]
public class SDFDualContouringRenderer : MonoBehaviour
{
    [Header("Iso Surface")]
    public float isoLevel = 1e-6f;
    [Header("Rebuild")]
    public bool rebuildEveryFrame = false;

    private SDFSampler _sampler;
    private MeshFilter _meshFilter;
    private Mesh _mesh;

    private readonly List<Vector3> _vertices = new();
    private readonly List<int> _triangles = new();

    //one vertex index per call
    private int[,,] _cellVertexIndex;
    private Vector3Int _lastCellArraySize;
    private float _lastIsoLevel;
    private Vector3 _lastScale;

    // cube corner offsets
    private static readonly Vector3Int[] CornerOffsets =
    {
        new Vector3Int(0, 0, 0), // 0
        new Vector3Int(1, 0, 0), // 1
        new Vector3Int(1, 1, 0), // 2
        new Vector3Int(0, 1, 0), // 3
        new Vector3Int(0, 0, 1), // 4
        new Vector3Int(1, 0, 1), // 5
        new Vector3Int(1, 1, 1), // 6
        new Vector3Int(0, 1, 1), // 7
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
    // 12 cube edges as corner index pairs
    private static readonly Edge[] Edges =
    {
        new Edge(0, 1), new Edge(1, 2), new Edge(2, 3), new Edge(3, 0),
        new Edge(4, 5), new Edge(5, 6), new Edge(6, 7), new Edge(7, 4),
        new Edge(0, 4), new Edge(1, 5), new Edge(2, 6), new Edge(3, 7)
    };

    private void Awake()
    {
        _sampler = GetComponent<SDFSampler>();
        _meshFilter = GetComponent<MeshFilter>();
        _mesh = new Mesh();
        _mesh.name = "SDF Dual Contouring Mesh";
        _mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        _meshFilter.sharedMesh = _mesh;
    }

    private void OnEnable()
    {
        RebuildMesh();
    }

    private void Update()
    {
        if (rebuildEveryFrame || IsDirty())
        {
            RebuildMesh();
        }
    }

    [ContextMenu("Rebuild Mesh")]
    public void RebuildMesh()
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
            _meshFilter.sharedMesh = _mesh;
        }

        if (_sampler.IsDirty || _sampler.Samples == null || _sampler.Samples.Length == 0)
            _sampler.RebuildSamples();

        Vector3Int cells = _sampler.CellCount;

        if (cells.x <= 0 || cells.y <= 0 || cells.z <= 0)
        {
            _mesh.Clear();
            CacheState();
            transform.hasChanged = false;
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
            _cellVertexIndex = new int[cells.x, cells.y, cells.z];
            _lastCellArraySize = cells;
        }

        //  default all cells to invalid
        for (int x = 0; x < cells.x; x++)
        {
            for (int y = 0; y < cells.y; y++)
            {
                for (int z = 0; z < cells.z; z++)
                {
                    _cellVertexIndex[x, y, z] = -1;
                }
            }
        }

        // 1. create one vertex per active all
        for (int x = 0; x < cells.x; x++)
        {
            for (int y = 0; y < cells.y; y++)
            {
                for (int z = 0; z < cells.z; z++)
                {
                    _cellVertexIndex[x, y, z] = CreateCellVertex(x, y, z);
                }
            }
        }

        // 2. connect neiboring cell vertices
        BuildQuads();

        _mesh.Clear();
        _mesh.SetVertices(_vertices);
        _mesh.SetTriangles(_triangles, 0);
        _mesh.RecalculateNormals();
        _mesh.RecalculateBounds();

        CacheState();
        transform.hasChanged = false;
    }

    private int CreateCellVertex(int cellX, int cellY, int cellZ)
    {
        Vector3 sum = Vector3.zero;
        int count = 0;
        float minValue = float.PositiveInfinity;
        float maxValue = float.NegativeInfinity;

        // fist check if this cell contains the iso surface
        for (int i = 0; i < 8; i++)
        {
            Vector3Int o = CornerOffsets[i];
            float value = _sampler.GetSample(cellX + o.x, cellY + o.y, cellZ + o.z).Distance;

            if (value < minValue) minValue = value;
            if (value > maxValue) maxValue = value;
        }

        if (minValue > isoLevel || maxValue < isoLevel)
            return -1;

        // collect all edge crossing inside this cell
        for (int e = 0; e < 12; e++)
        {
            Edge edge = Edges[e];
            int ca = edge.A;
            int cb = edge.B;

            Vector3Int oa = CornerOffsets[ca];
            Vector3Int ob = CornerOffsets[cb];

            SDFSample a = _sampler.GetSample(cellX + oa.x, cellY + oa.y, cellZ + oa.z);
            SDFSample b = _sampler.GetSample(cellX + ob.x, cellY + ob.y, cellZ + ob.z);

            if (!HasCrossing(a.Distance, b.Distance))
                continue;

            Vector3 p = Interpolate(a.LocalPosition, a.Distance, b.LocalPosition, b.Distance);

            sum += p;
            count++;

        }

        if (count == 0)
            return -1;

        Vector3 vertex = sum / count;
        int index = _vertices.Count;
        _vertices.Add(vertex);
        return index;
    }

    private void BuildQuads()
    {
        Vector3Int cells = _sampler.CellCount;

        // for every grid edge with a sign change, connect the 4 cells around that edge.
        // X-axis grid edges
        for (int x = 0; x < cells.x; x++)
        {
            for (int y = 1; y < cells.y; y++)
            {
                for (int z = 1; z < cells.z; z++)
                {
                    float a = _sampler.GetSample(x, y, z).Distance;
                    float b = _sampler.GetSample(x + 1, y, z).Distance;
                    if (!HasCrossing(a, b))
                        continue;

                    int v0 = _cellVertexIndex[x, y - 1, z - 1];
                    int v1 = _cellVertexIndex[x, y, z - 1];
                    int v2 = _cellVertexIndex[x, y, z];
                    int v3 = _cellVertexIndex[x, y - 1, z];

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
                    float a = _sampler.GetSample(x, y, z).Distance;
                    float b = _sampler.GetSample(x, y + 1, z).Distance;

                    if (!HasCrossing(a, b))
                        continue;

                    int v0 = _cellVertexIndex[x - 1, y, z - 1];
                    int v1 = _cellVertexIndex[x, y, z - 1];
                    int v2 = _cellVertexIndex[x, y, z];
                    int v3 = _cellVertexIndex[x - 1, y, z];
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
                    float a = _sampler.GetSample(x, y, z).Distance;
                    float b = _sampler.GetSample(x, y, z + 1).Distance;

                    if (!HasCrossing(a, b))
                        continue;

                    int v0 = _cellVertexIndex[x - 1, y - 1, z];
                    int v1 = _cellVertexIndex[x, y - 1, z];
                    int v2 = _cellVertexIndex[x, y, z];
                    int v3 = _cellVertexIndex[x - 1, y, z];

                    AddQuad(v0, v1, v2, v3, a < isoLevel);
                }
            }
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
        const float epsilon = 1e-8f;

        float da = va - isoLevel;
        float db = vb - isoLevel;

        float denom = da - db;

        if (Mathf.Abs(denom) < epsilon)
            return (pa + pb) * 0.5f;

        float t = da / denom;
        t = Mathf.Clamp01(t);

        return Vector3.Lerp(pa, pb, t);
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

        if (!Mathf.Approximately(_lastIsoLevel, isoLevel))
            return true;

        if (_lastScale != transform.lossyScale)
            return true;

        return false;
    }

    private void CacheState()
    {
        _lastIsoLevel = isoLevel;
        _lastScale = transform.lossyScale;
    }

    private void OnDrawGizmosSelected()
    {
        if (_sampler == null)
            _sampler = GetComponent<SDFSampler>();

        if (_sampler == null)
            return;

        Vector3 extent = _sampler.GetEffectiveGridExtent();
        Gizmos.matrix = transform.localToWorldMatrix;
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireCube(Vector3.zero, extent);
        Gizmos.matrix = Matrix4x4.identity;
    }
}