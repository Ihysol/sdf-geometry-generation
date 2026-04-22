using System.Collections.Generic;
using System.IO;
using System.Threading;
using Unity.Burst.CompilerServices;
using Unity.VisualScripting;
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
    public bool rebuildEveryFrame = false;
    private SDFSampler _sampler;
    private MeshFilter _meshFilter;
    private Mesh _mesh;

    // reused buffers (avoid allocations every frame)
    private readonly List<Vector3> _vertices = new();
    private readonly List<int> _triangles = new();
    private readonly VertexSample[] _cube = new VertexSample[8];
    private readonly Dictionary<EdgeKey, int> _localEdgeCache = new();
    // cache previous state to detect changes
    private float _lastIsoLevel;
    private Vector3 _lastScale;
    private Vector3Int _lastGridSize;

    // 8 cube corners
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

    // cube -> 6 tetrahedra
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

    // identifies an edge between two cube corners (order-independent)
    private struct EdgeKey
    {
        public int A;
        public int B;

        public EdgeKey(int a, int b)
        {
            // normalize order so (a,b) == (b,a)
            if (a < b)
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
    }

    private void Awake()
    {
        _sampler = GetComponent<SDFSampler>();
        _meshFilter = GetComponent<MeshFilter>();

        _mesh = new Mesh();
        _mesh.name = "SDF Surface Mesh";
        _mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        _meshFilter.sharedMesh = _mesh;

        CacheState();
    }

    private void OnEnable()
    {
        RebuildMesh();
    }

    private void Update()
    {
        // only rebuild if needed (huge performance gain)
        if (rebuildEveryFrame || IsDirty())
        {
            RebuildMesh();
        }
    }
    private void OnValidate()
    {
        if (isoLevel == 0f)
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
            CacheState();
            transform.hasChanged = false;
            return;
        }

        _vertices.Clear();
        _triangles.Clear();
        Vector3Int gridSize = _sampler.GridSize;

        for (int x = 0; x < gridSize.x - 1; x++)
        {
            for (int y = 0; y < gridSize.y - 1; y++)
            {
                for (int z = 0; z < gridSize.z - 1; z++)
                {
                    PolygonizeCube(x, y, z);
                }
            }
        }

        _mesh.Clear();
        _mesh.SetVertices(_vertices);
        _mesh.SetTriangles(_triangles, 0);
        _mesh.RecalculateNormals();
        _mesh.RecalculateBounds();

        CacheState();
        transform.hasChanged = false;
    }

    private void PolygonizeCube(int x, int y, int z)
    {
        float minValue = float.PositiveInfinity;
        float maxValue = float.NegativeInfinity;

        for (int i = 0; i < 8; i++)
        {
            Vector3Int o = CubeCornerOffsets[i];
            SDFSample sample = _sampler.GetSample(x + o.x, y + o.y, z + o.z);

            _cube[i] = new VertexSample
            {
                Position = sample.LocalPosition,
                Value = sample.Distance
            };

            if (sample.Distance < minValue) minValue = sample.Distance;
            if (sample.Distance > maxValue) maxValue = sample.Distance;
        }

        if (minValue > isoLevel || maxValue < isoLevel)
            return;

        _localEdgeCache.Clear();

        for (int t = 0; t < 6; t++)
        {
            int i0 = Tetrahedra[t, 0];
            int i1 = Tetrahedra[t, 1];
            int i2 = Tetrahedra[t, 2];
            int i3 = Tetrahedra[t, 3];

            PolygonizeTetrahedron(_cube[i0], _cube[i1], _cube[i2], _cube[i3], i0, i1, i2, i3, _localEdgeCache);
        }
    }

    private void PolygonizeTetrahedron(VertexSample a, VertexSample b, VertexSample c, VertexSample d, int ia, int ib, int ic, int id, Dictionary<EdgeKey, int> edgeCache)
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
                AddTriangle(
                    GetOrCreateVertex(a, b, ia, ib, edgeCache),
                    GetOrCreateVertex(a, c, ia, ic, edgeCache),
                    GetOrCreateVertex(a, d, ia, id, edgeCache));
                break;
            case 2:
                AddTriangle(
                    GetOrCreateVertex(b, a, ib, ia, edgeCache),
                    GetOrCreateVertex(b, d, ib, id, edgeCache),
                    GetOrCreateVertex(b, c, ib, ic, edgeCache));
                break;
            case 4:
                AddTriangle(
                    GetOrCreateVertex(c, a, ic, ia, edgeCache),
                    GetOrCreateVertex(c, b, ic, ib, edgeCache),
                    GetOrCreateVertex(c, d, ic, id, edgeCache));
                break;
            case 8:
                AddTriangle(
                    GetOrCreateVertex(d, a, id, ia, edgeCache),
                    GetOrCreateVertex(d, c, id, ic, edgeCache),
                    GetOrCreateVertex(d, b, id, ib, edgeCache));
                break;
            // 3 inside = inverse of 1 inside
            case 14:
                AddTriangle(
                    GetOrCreateVertex(a, b, ia, ib, edgeCache),
                    GetOrCreateVertex(a, d, ia, id, edgeCache),
                    GetOrCreateVertex(a, c, ia, ic, edgeCache));
                break;
            case 13:
                AddTriangle(
                    GetOrCreateVertex(b, a, ib, ia, edgeCache),
                    GetOrCreateVertex(b, c, ib, ic, edgeCache),
                    GetOrCreateVertex(b, d, ib, id, edgeCache));
                break;
            case 11:
                AddTriangle(
                    GetOrCreateVertex(c, a, ic, ia, edgeCache),
                    GetOrCreateVertex(c, d, ic, id, edgeCache),
                    GetOrCreateVertex(c, b, ic, ib, edgeCache));
                break;
            case 7:
                AddTriangle(
                    GetOrCreateVertex(d, a, id, ia, edgeCache),
                    GetOrCreateVertex(d, b, id, ib, edgeCache),
                    GetOrCreateVertex(d, c, id, ic, edgeCache));
                break;
            // 3 inside
            case 3:
            case 12:
                {
                    int p0 = GetOrCreateVertex(a, c, ia, ic, edgeCache);
                    int p1 = GetOrCreateVertex(a, d, ia, id, edgeCache);
                    int p2 = GetOrCreateVertex(b, c, ib, ic, edgeCache);
                    int p3 = GetOrCreateVertex(b, d, ib, id, edgeCache);
                    AddTriangle(p0, p1, p2);
                    AddTriangle(p2, p1, p3);
                    break;
                }
            case 5:
            case 10:
                {
                    int p0 = GetOrCreateVertex(a, b, ia, ib, edgeCache);
                    int p1 = GetOrCreateVertex(a, d, ia, id, edgeCache);
                    int p2 = GetOrCreateVertex(c, b, ic, ib, edgeCache);
                    int p3 = GetOrCreateVertex(c, d, ic, id, edgeCache);
                    AddTriangle(p0, p1, p2);
                    AddTriangle(p2, p1, p3);
                    break;
                }

            case 6:
            case 9:
                {
                    int p0 = GetOrCreateVertex(a, b, ia, ib, edgeCache);
                    int p1 = GetOrCreateVertex(a, c, ia, ic, edgeCache);
                    int p2 = GetOrCreateVertex(d, b, id, ib, edgeCache);
                    int p3 = GetOrCreateVertex(d, c, id, ic, edgeCache);

                    AddTriangle(p0, p1, p2);
                    AddTriangle(p2, p1, p3);
                    break;
                }
        }
    }

    // ensures each edge produces only one vertex
    private int GetOrCreateVertex(VertexSample a, VertexSample b, int ia, int ib, Dictionary<EdgeKey, int> edgeCache)
    {
        EdgeKey key = new EdgeKey(ia, ib);

        // reuse vertex if already created
        if (edgeCache.TryGetValue(key, out int cachedIndex))
            return cachedIndex;

        Vector3 position = Interpolate(a, b);

        int newIndex = _vertices.Count;

        _vertices.Add(position);
        edgeCache[key] = newIndex;

        return newIndex;
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

        // safe interpolation between SDF values (avoids division issues)
        if (Mathf.Abs(delta) < epsilon)
            return (a.Position + b.Position) * 0.5f;

        // linear interpolation factor along edge (iso crossing)
        float t = (isoLevel - a.Value) / delta;

        // clamp to avoid numeric overshoot
        t = Mathf.Clamp01(t);

        // interpolate position along edge
        return Vector3.Lerp(a.Position, b.Position, t);
    }

    private void AddTriangle(int ia, int ib, int ic)
    {
        Vector3 a = _vertices[ia];
        Vector3 b = _vertices[ib];
        Vector3 c = _vertices[ic];

        Vector3 faceNormal = Vector3.Cross(b - a, c - a);

        // if (faceNormal.sqrMagnitude < 1e-20f)
        //     return;

        faceNormal.Normalize();

        Vector3 center = (a + b + c) / 3f;

        // estimate surface normal from SDF
        Vector3 sdfNormal = _sampler.EstimateNormalLocal(center);

        // flip triangle if facing wrong direction
        if (Vector3.Dot(faceNormal, sdfNormal) < 0f)
        {
            (ib, ic) = (ic, ib);
        }

        _triangles.Add(ia);
        _triangles.Add(ib);
        _triangles.Add(ic);
    }

    // store current state after rebuild
    private void CacheState()
    {
        _lastIsoLevel = isoLevel;
        _lastScale = transform.lossyScale;

        if (_sampler != null)
            _lastGridSize = _sampler.GridSize;
    }

    private void OnDrawGizmos()
    {
        if (_sampler == null)
            _sampler = GetComponent<SDFSampler>();
        if (_sampler == null)
            return;

        // draw grid outline
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireCube(transform.position, _sampler.gridExtent);
    }

    // checks if something changed since last rebuild
    private bool IsDirty()
    {
        if (_sampler == null)
            return true;

        // transform changed
        // if (transform.hasChanged)
        //     return true;
        if(_lastScale != transform.lossyScale)
            return true;

        // iso surface changed
        if (!Mathf.Approximately(_lastIsoLevel, isoLevel))
            return true;

        // grid resolution / size changed
        if (_sampler.IsDirty)
            return true;

        return false;
    }
}