using System.Collections.Generic;
using System.Threading;
using UnityEngine;
using UnityEngine.UIElements;

[RequireComponent(typeof(SDFSampler))]
[RequireComponent(typeof(MeshFilter))]
[RequireComponent(typeof(MeshRenderer))]
public class SDFMarchingCubesRenderer : MonoBehaviour
{
    [Header("Iso Surface")]
    public float isoLevel = 1e-6f;
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
    private void OnValidate()
    {
        if(isoLevel== 0f)
        {
            isoLevel = 1e-6f;
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
        int mask = 0;
        if (a.Value < isoLevel) mask |= 1;
        if (b.Value < isoLevel) mask |= 2;
        if (c.Value < isoLevel) mask |= 4;
        if (d.Value < isoLevel) mask |= 8;

        switch (mask)
        {
            case 0:
            case 15:
                return;
            // 1 inside
            case 1:
                AddTriangle(vertices, triangles, Interpolate(a, b), Interpolate(a, c), Interpolate(a, d));
                break;
            case 2:
                AddTriangle(vertices, triangles, Interpolate(b, a), Interpolate(b, d), Interpolate(b, c));
                break;
            case 4:
                AddTriangle(vertices, triangles, Interpolate(c, a), Interpolate(c, b), Interpolate(c, d));
                break;
            case 8:
                AddTriangle(vertices, triangles, Interpolate(d, a), Interpolate(d, c), Interpolate(d, b));
                break;
            // 3 inside = inverse of 1 inside
            case 14:
                AddTriangle(vertices, triangles, Interpolate(a, b), Interpolate(a, d), Interpolate(a, c));
                break;
            case 13:
                AddTriangle(vertices, triangles, Interpolate(b, a), Interpolate(b, c), Interpolate(b, d));
                break;
            case 11:
                AddTriangle(vertices, triangles, Interpolate(c, a), Interpolate(c, d), Interpolate(c, b));
                break;
            case 7:
                AddTriangle(vertices, triangles, Interpolate(d, a), Interpolate(d, b), Interpolate(d, c));
                break;
            // 3 inside
            case 3:
            case 12:
                {
                    Vector3 p0 = Interpolate(a, c);
                    Vector3 p1 = Interpolate(a, d);
                    Vector3 p2 = Interpolate(b, c);
                    Vector3 p3 = Interpolate(b, d);
                    AddTriangle(vertices, triangles, p0, p1, p2);
                    AddTriangle(vertices, triangles, p2, p1, p3);
                    break;
                }
            case 5:
            case 10:
                {
                    Vector3 p0 = Interpolate(a, b);
                    Vector3 p1 = Interpolate(a, d);
                    Vector3 p2 = Interpolate(c, b);
                    Vector3 p3 = Interpolate(c, d);
                    AddTriangle(vertices, triangles, p0, p1, p2);
                    AddTriangle(vertices, triangles, p2, p1, p3);
                    break;
                }

            case 6:
            case 9:
                {
                    Vector3 p0 = Interpolate(a, b);
                    Vector3 p1 = Interpolate(a, c);
                    Vector3 p2 = Interpolate(d, b);
                    Vector3 p3 = Interpolate(d, c);

                    AddTriangle(vertices, triangles, p0, p1, p2);
                    AddTriangle(vertices, triangles, p2, p1, p3);
                    break;
                }

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
        const float epsilon = 1e-6f;

        // if a is already on iso surface -> use directly
        if (Mathf.Abs(isoLevel - a.Value) < epsilon)
            return a.Position;

        // if b is already on iso surface -> use directly
        if (Mathf.Abs(isoLevel - b.Value) < epsilon)
            return b.Position;

        float delta = b.Value - a.Value;

        // avoid division by ~0 (flat field / identical values)
        if (Mathf.Abs(delta) < epsilon)
            return a.Position;

        // linear interpolation factor along edge (iso crossing)
        float t = (isoLevel - a.Value) / delta;

        // clamp to avoid numeric overshoot
        t = Mathf.Clamp01(t);

        // interpolate position along edge
        return Vector3.Lerp(a.Position, b.Position, t);
    }

    private void AddTriangle(List<Vector3> vertices, List<int> triangles, Vector3 a, Vector3 b, Vector3 c)
    {
        Vector3 ab = b - a;
        Vector3 ac = c - a;
        Vector3 faceNormal = Vector3.Cross(ab, ac);

        // skip tiny/invalid triangles
        // if (faceNormal.sqrMagnitude < 1e-8f)
        //     return;

        faceNormal.Normalize();

        Vector3 center = (a + b + c) / 3f;

        // get SDF normal at triangle center
        Vector3 sdfNormal = _sampler.EstimateNormalLocal(center);

        // flip if facing wrong direction
        if (Vector3.Dot(faceNormal, sdfNormal) < 0f)
        {
            Vector3 tmp = b;
            b = c;
            c = tmp;
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