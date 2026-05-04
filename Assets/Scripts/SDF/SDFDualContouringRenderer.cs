using System.Collections.Generic;
using UnityEngine;
using System.Diagnostics;
using Unity.Mathematics;

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
        if(_sampler == null)
            _sampler = GetComponent<SDFSampler>();
        if(_meshFilter == null)
            _meshFilter = GetComponent<MeshFilter>();
        if(_mesh == null)
        {
            _mesh = new Mesh();
            _mesh.name = "SDF Dual Contouring Mesh";
            _mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            // _mesh.MarkDynamic();
            _meshFilter.sharedMesh = _mesh;
        }
    }

    private int CreateCellVertex(VoxelGrid volume, int cellX, int cellY, int cellZ)
    {
        Vector3 sum = Vector3.zero;
        int count = 0;
        float minValue = float.PositiveInfinity;
        float maxValue = float.NegativeInfinity;

        for(int i = 0; i< 8; i++)
        {
            
        }
    }

    [ContextMenu("Rebuild Mesh")]
    public void RebuildMesh()
    {
        Stopwatch sw = Stopwatch.StartNew();
    }


    private void Awake()
    {
        EnsureReferences();
    }

    private void OnEnable()
    {
        RebuildMesh();
    }

    private void update()
    {
        if(rebuildEveryFrame || IsDirty())
        {
            
        }
    }
}