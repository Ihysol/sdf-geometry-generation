using System.Collections.Generic;
using System.Threading;
using UnityEngine;

[RequireComponent(typeof(SDFSampler))]
[RequireComponent(typeof(MeshFilter))]
[RequireComponent(typeof(MeshRenderer))]
public class SDFMarchingCubesRenderer : MonoBehaviour
{
    [Header("Iso Surface")]
    public float isoLevel = 0f;
    [Header("Rebuild")]
    public bool rebuildEveryFrame = true;

    private SDFSampler _sampler;
    private MeshFilter _meshFilter;
    private Mesh _mesh;

    // 8 cube corners in local cube-index order
    private static readonly Vector3Int[] CubeCornerOffsets =
    {
        new Vector3Int(0, 0, 0), // 0
        new Vector3Int(1, 0, 0), // 1
        new Vector3Int(1, 1, 0), // 2
        new Vector3Int(0, 1, 0), // 3
        new Vector3Int(0, 0, 1), // 4
        new Vector3Int(1, 0, 1), // 5
        new Vector3Int(1, 1, 1), // 6
        new Vector3Int(0, 1, 1) // 7
    };

    // Decompose one cube into 6 tetrahedra using cube corner indices
    private static readonly int[,] Tetrahedra =
    {
        {0, 5, 1, 6},
        {0, 1, 2, 6},
        {0, 2, 3, 6},
        {0, 3, 7, 6},
        {0, 7, 4, 6},
        {0, 4, 5, 6}
    };

    private struct VertexSample
    {
        public Vector3 Position;
        public float Value;
    }

    private void Awake()
    {
        _sampler = GetComponent<SDFSampler>();
        _meshFilter = GetComponent<MeshFilter>();

        _mesh = new Mesh();
        _mesh.name = "SDF Surface Mesh";
        _mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        _meshFilter.sharedMesh = _mesh;
    }

    private void OnEnable()
    {
        RebuildMesh();
    }

    private void Update()
    {
        if (rebuildEveryFrame)
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
            _mesh.name = "SDF Surface Mesh";
            _mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            _meshFilter.sharedMesh = _mesh;
        }

        _sampler.RebuildSamples();

        if (_sampler.Samples == null || _sampler.Samples.Length == 0)
        {
            _mesh.Clear();
            return;
        }

        List<Vector3> vertices = new List<Vector3>();
        List<int> triangles = new List<int>();
        Vector3Int gridSize = _sampler.GridSize;

        // iterate cells, not sample points
        for (int x = 0; x < gridSize.x - 1; x++)
        {
            for (int y = 0; y < gridSize.y - 1; y++)
            {
                for (int z = 0; z < gridSize.z - 1; z++)
                {
                    PolygonizeCube(x, y, z, vertices, triangles);
                }
            }
        }

        _mesh.Clear();
        _mesh.SetVertices(vertices);
        _mesh.SetTriangles(triangles, 0);
        _mesh.RecalculateNormals();
        _mesh.RecalculateBounds();
    }

    private void PolygonizeCube(int x, int y, int z, List<Vector3> vertices, List<int> triangles)
    {
        VertexSample[] cube = new VertexSample[8];

        for (int i = 0; i < 8; i++)
        {
            Vector3Int o = CubeCornerOffsets[i];
            SDFSample sample = _sampler.GetSample(x + o.x, y + o.y, z + o.z);

            cube[i] = new VertexSample
            {
                Position = transform.InverseTransformPoint(sample.WorldPosition),
                Value = sample.Distance
            };
        }

        for (int t = 0; t < 6; t++)
        {
            VertexSample a = cube[Tetrahedra[t, 0]];
            VertexSample b = cube[Tetrahedra[t, 1]];
            VertexSample c = cube[Tetrahedra[t, 2]];
            VertexSample d = cube[Tetrahedra[t, 3]];

            PolygonizeTetrahedron(a, b, c, d, vertices, triangles);
        }
    }

    private void PolygonizeTetrahedron(VertexSample a, VertexSample b, VertexSample c, VertexSample d, List<Vector3> vertices, List<int> triangles)
    {
        bool aInside = a.Value < isoLevel;
        bool bInside = b.Value < isoLevel;
        bool cInside = c.Value < isoLevel;
        bool dInside = d.Value < isoLevel;

        int insideCount = (aInside ? 1 : 0) + (bInside ? 1 : 0) + (cInside ? 1 : 0) + (dInside ? 1 : 0);

        if (insideCount == 0 || insideCount == 4) // if empty space or full, skip
            return;

        VertexSample[] inside = new VertexSample[4];
        VertexSample[] outside = new VertexSample[4];
        int insideIndex = 0;
        int outsideIndex = 0;

        AddBySide(a, aInside, inside, outside, ref insideIndex, ref outsideIndex);
        AddBySide(b, bInside, inside, outside, ref insideIndex, ref outsideIndex);
        AddBySide(c, cInside, inside, outside, ref insideIndex, ref outsideIndex);
        AddBySide(d, dInside, inside, outside, ref insideIndex, ref outsideIndex);

        if (insideCount == 1)
        {
            Vector3 p0 = Interpolate(inside[0], outside[0]);
            Vector3 p1 = Interpolate(inside[0], outside[1]);
            Vector3 p2 = Interpolate(inside[0], outside[2]);
            AddTriangle(vertices, triangles, p0, p1, p2);
        }
        else if (insideCount == 3)
        {
            Vector3 p0 = Interpolate(outside[0], inside[0]);
            Vector3 p1 = Interpolate(outside[0], inside[1]);
            Vector3 p2 = Interpolate(outside[0], inside[2]);
            AddTriangle(vertices, triangles, p0, p2, p1);
        }
        else if (insideCount == 2)
        {
            Vector3 p0 = Interpolate(inside[0], outside[0]);
            Vector3 p1 = Interpolate(inside[0], outside[1]);
            Vector3 p2 = Interpolate(inside[1], outside[0]);
            Vector3 p3 = Interpolate(inside[1], outside[1]);
            AddTriangle(vertices, triangles, p0, p1, p2);
            AddTriangle(vertices, triangles, p2, p1, p3);
        }
    }

    private void AddBySide(VertexSample v, bool isInside, VertexSample[] inside, VertexSample[] outside, ref int insideIndex, ref int outsideIndex)
    {
        if (isInside)
        {
            inside[insideIndex] = v;
            insideIndex++;
        }
        else
        {
            outside[outsideIndex] = v;
            outsideIndex++;
        }
    }

    private Vector3 Interpolate(VertexSample a, VertexSample b)
    {
        float delta = b.Value - a.Value;
        if (Mathf.Abs(delta) < 0.000001f)
            return a.Position;

        float t = (isoLevel - a.Value) / delta;
        t = Mathf.Clamp01(t);
        return Vector3.Lerp(a.Position, b.Position, t);
    }

    private void AddTriangle(List<Vector3> vertices, List<int> triangles, Vector3 a, Vector3 b, Vector3 c)
    {
        Vector3 normal = Vector3.Cross(b - a, c - a).normalized;
        Vector3 center = (a + b + c) / 3f;

        // for shapes centered around local origin:
        // if normal points inward, flip winding
        if(Vector3.Dot(normal, center.normalized) < 0f)
        {
            Vector3 temp = b;
            b = c;
            c = temp;    
        }
        
        int start = vertices.Count;
        vertices.Add(a);
        vertices.Add(b);
        vertices.Add(c);

        triangles.Add(start);
        triangles.Add(start + 1);
        triangles.Add(start + 2);
    }

    private void OnDrawGizmos()
    {
        // draw grid outline
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireCube(transform.position, _sampler.gridExtent);
    }
}