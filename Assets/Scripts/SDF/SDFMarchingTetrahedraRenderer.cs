using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Unity.Burst.CompilerServices;
using Unity.Mathematics;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.UIElements;

[RequireComponent(typeof(SDFSampler))]
[RequireComponent(typeof(MeshFilter))]
[RequireComponent(typeof(MeshRenderer))]
public class SDFMarchingTetrahedraRenderer : MonoBehaviour
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
    private readonly Dictionary<EdgeKey, int> _globalEdgeCache = new();
    private readonly Dictionary<(int, int), int> _localDiagonalCache = new();
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
    private struct EdgeKey : IEquatable<EdgeKey>
    {
        public int X;
        public int Y;
        public int Z;
        public byte Axis;

        public EdgeKey(int x, int y, int z, byte axis)
        {
            X = x;
            Y = y;
            Z = z;
            Axis = axis;
        }

        public bool Equals(EdgeKey other)
        {
            return X == other.X &&
                   Y == other.Y &&
                   Z == other.Z &&
                   Axis == other.Axis;
        }

        public override bool Equals(object obj)
        {
            return obj is EdgeKey other && Equals(other);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(X, Y, Z, (int)Axis);
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

        // CacheState();
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
        _globalEdgeCache.Clear();
        Vector3Int cells = _sampler.CellCount;

        for (int x = 0; x < cells.x; x++)
        {
            for (int y = 0; y < cells.y; y++)
            {
                for (int z = 0; z < cells.z; z++)
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

        _localDiagonalCache.Clear();

        for (int t = 0; t < 6; t++)
        {
            int i0 = Tetrahedra[t, 0];
            int i1 = Tetrahedra[t, 1];
            int i2 = Tetrahedra[t, 2];
            int i3 = Tetrahedra[t, 3];

            PolygonizeTetrahedron(
                x, y, z,
                _cube[i0], _cube[i1], _cube[i2], _cube[i3],
                i0, i1, i2, i3);
        }
    }

    private static bool TryGetGlobalEdgeKey(int cubeX, int cubeY, int cubeZ, int a, int b, out EdgeKey key)
    {
        key = default;

        int min = Mathf.Min(a, b);
        int max = Mathf.Max(a, b);

        switch (min, max)
        {
            // bottom face
            case (0, 1): key = new EdgeKey(cubeX, cubeY, cubeZ, 0); return true; // X
            case (1, 2): key = new EdgeKey(cubeX + 1, cubeY, cubeZ, 1); return true; // Y
            case (2, 3): key = new EdgeKey(cubeX, cubeY + 1, cubeZ, 0); return true; // X
            case (0, 3): key = new EdgeKey(cubeX, cubeY, cubeZ, 1); return true; // Y

            // top face
            case (4, 5): key = new EdgeKey(cubeX, cubeY, cubeZ + 1, 0); return true; // X
            case (5, 6): key = new EdgeKey(cubeX + 1, cubeY, cubeZ + 1, 1); return true; // Y
            case (6, 7): key = new EdgeKey(cubeX, cubeY + 1, cubeZ + 1, 0); return true; // X
            case (4, 7): key = new EdgeKey(cubeX, cubeY, cubeZ + 1, 1); return true; // Y

            // verticals
            case (0, 4): key = new EdgeKey(cubeX, cubeY, cubeZ, 2); return true; // Z
            case (1, 5): key = new EdgeKey(cubeX + 1, cubeY, cubeZ, 2); return true; // Z
            case (2, 6): key = new EdgeKey(cubeX + 1, cubeY + 1, cubeZ, 2); return true; // Z
            case (3, 7): key = new EdgeKey(cubeX, cubeY + 1, cubeZ, 2); return true; // Z

            default:
                return false; // diagonal / internal tetra edge
        }
    }

    private void PolygonizeTetrahedron(
    int cubeX, int cubeY, int cubeZ,
    VertexSample a, VertexSample b, VertexSample c, VertexSample d,
    int ia, int ib, int ic, int id)
    {
        VertexSample[] samples = { a, b, c, d };
        int[] ids = { ia, ib, ic, id };

        List<int> inside = new(4);
        List<int> outside = new(4);

        for (int i = 0; i < 4; i++)
        {
            if (samples[i].Value <= isoLevel)
                inside.Add(i);
            else
                outside.Add(i);
        }

        int insideCount = inside.Count;

        if (insideCount == 0 || insideCount == 4)
            return;

        // 1 inside -> 1 triangle
        if (insideCount == 1)
        {
            int i0 = inside[0];

            int o0 = outside[0];
            int o1 = outside[1];
            int o2 = outside[2];

            int p0 = GetOrCreateVertex(cubeX, cubeY, cubeZ, samples[i0], samples[o0], ids[i0], ids[o0]);
            int p1 = GetOrCreateVertex(cubeX, cubeY, cubeZ, samples[i0], samples[o1], ids[i0], ids[o1]);
            int p2 = GetOrCreateVertex(cubeX, cubeY, cubeZ, samples[i0], samples[o2], ids[i0], ids[o2]);

            Vector3 outsideCenter =
                (samples[o0].Position + samples[o1].Position + samples[o2].Position) / 3f;

            Vector3 desiredNormal = outsideCenter - samples[i0].Position;

            AddTriangleFacing(p0, p1, p2, desiredNormal);
            return;
        }

        // 3 inside -> 1 triangle
        if (insideCount == 3)
        {
            int o0 = outside[0];

            int i0 = inside[0];
            int i1 = inside[1];
            int i2 = inside[2];

            int p0 = GetOrCreateVertex(cubeX, cubeY, cubeZ, samples[o0], samples[i0], ids[o0], ids[i0]);
            int p1 = GetOrCreateVertex(cubeX, cubeY, cubeZ, samples[o0], samples[i1], ids[o0], ids[i1]);
            int p2 = GetOrCreateVertex(cubeX, cubeY, cubeZ, samples[o0], samples[i2], ids[o0], ids[i2]);

            Vector3 insideCenter =
                (samples[i0].Position + samples[i1].Position + samples[i2].Position) / 3f;

            Vector3 desiredNormal = samples[o0].Position - insideCenter;

            AddTriangleFacing(p0, p2, p1, desiredNormal);
            return;
        }

        // 2 inside -> quad -> 2 triangles
        if (insideCount == 2)
        {
            int i0 = inside[0];
            int i1 = inside[1];

            int o0 = outside[0];
            int o1 = outside[1];

            int p00 = GetOrCreateVertex(cubeX, cubeY, cubeZ, samples[i0], samples[o0], ids[i0], ids[o0]);
            int p01 = GetOrCreateVertex(cubeX, cubeY, cubeZ, samples[i0], samples[o1], ids[i0], ids[o1]);
            int p10 = GetOrCreateVertex(cubeX, cubeY, cubeZ, samples[i1], samples[o0], ids[i1], ids[o0]);
            int p11 = GetOrCreateVertex(cubeX, cubeY, cubeZ, samples[i1], samples[o1], ids[i1], ids[o1]);

            Vector3 insideCenter =
                (samples[i0].Position + samples[i1].Position) * 0.5f;

            Vector3 outsideCenter =
                (samples[o0].Position + samples[o1].Position) * 0.5f;

            Vector3 desiredNormal = outsideCenter - insideCenter;

            AddTriangleFacing(p00, p10, p01, desiredNormal);
            AddTriangleFacing(p01, p10, p11, desiredNormal);
        }
    }

    // ensures each edge produces only one vertex
    private int GetOrCreateVertex(int cubeX, int cubeY, int cubeZ, VertexSample a, VertexSample b, int ia, int ib)
    {
        // 1) echte äußere Cube-Kante -> globaler Cache
        if (TryGetGlobalEdgeKey(cubeX, cubeY, cubeZ, ia, ib, out EdgeKey globalKey))
        {
            if (_globalEdgeCache.TryGetValue(globalKey, out int cachedIndex))
                return cachedIndex;

            Vector3 position = Interpolate(a, b);
            int newIndex = _vertices.Count;

            _vertices.Add(position);
            _globalEdgeCache[globalKey] = newIndex;

            return newIndex;
        }

        // 2) interne / diagonale Tetra-Kante -> nur lokal pro Cube cachen
        var localKey = ia < ib ? (ia, ib) : (ib, ia);

        if (_localDiagonalCache.TryGetValue(localKey, out int localCachedIndex))
            return localCachedIndex;

        Vector3 localPosition = Interpolate(a, b);
        int localNewIndex = _vertices.Count;

        _vertices.Add(localPosition);
        _localDiagonalCache[localKey] = localNewIndex;

        return localNewIndex;
    }

    private Vector3 Interpolate(VertexSample a, VertexSample b)
    {
        const float epsilon = 1e-8f;

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

    private void AddTriangleFacing(int ia, int ib, int ic, Vector3 desiredNormal)
    {
        Vector3 a = _vertices[ia];
        Vector3 b = _vertices[ib];
        Vector3 c = _vertices[ic];

        Vector3 faceNormal = Vector3.Cross(b - a, c - a);

        if (faceNormal.sqrMagnitude < 1e-20f)
            return;

        if (Vector3.Dot(faceNormal, desiredNormal) < 0f)
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

    private void OnDrawGizmosSelected()
    {
        if (_sampler == null)
            _sampler = GetComponent<SDFSampler>();

        if (_sampler == null)
            return;

        Vector3 extent = _sampler.useAutomaticBounds
            ? _sampler.GetEffectiveGridExtent()
            : _sampler.gridExtent;

        Gizmos.color = Color.green; // actual shape approx
        Gizmos.DrawWireCube(transform.position, extent * 0.9f);

        Gizmos.color = Color.cyan; // sampling bounds
        Gizmos.matrix = transform.localToWorldMatrix;
        Gizmos.DrawWireCube(Vector3.zero, extent);
        Gizmos.matrix = Matrix4x4.identity;
    }

    // checks if something changed since last rebuild
    private bool IsDirty()
    {
        if (_sampler == null)
            return true;

        // transform changed
        // if (transform.hasChanged)
        //     return true;
        if (_lastScale != transform.lossyScale)
            return true;

        // iso surface changed
        if (!Mathf.Approximately(_lastIsoLevel, isoLevel))
            return true;

        if (_sampler.GridSize != _lastGridSize)
            return true;

        // grid resolution / size changed
        if (_sampler.IsDirty)
            return true;

        return false;
    }
}