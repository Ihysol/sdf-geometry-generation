using System.Collections.Generic;
using UnityEngine;

public class DualContouringVoxelMesher : IVolumeMesher<VoxelGrid>
{
    private readonly List<Vector3> _vertices = new();
    private readonly List<int> _triangles = new();

    private int[] _cellVertexIndex;
    private readonly float[] _cellValues = new float[8];
    private readonly Vector3[] _cellPositions = new Vector3[8];

    private Vector3Int _lastCellArraySize;
    private float _isoLevel;

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

        /// <summary>Stores the two corner indices for one cell edge.</summary>
        public Edge(int a, int b)
        {
            A = a;
            B = b;
        }
    }

    private static readonly Edge[] Edges =
    {
        new Edge(0, 1), new Edge(1, 2), new Edge(2, 3), new Edge(3, 0),
        new Edge(4, 5), new Edge(5, 6), new Edge(6, 7), new Edge(7, 4),
        new Edge(0, 4), new Edge(1, 5), new Edge(2, 6), new Edge(3, 7)
    };

    /// <summary>Builds dual-contouring mesh buffers from a dense voxel grid.</summary>
    public MeshData BuildMeshData(VoxelGrid volume, float isoLevel)
    {
        _isoLevel = isoLevel;

        MeshData meshData = new MeshData();

        if (volume == null)
            return meshData;

        float[] values = volume.Values;
        Vector3Int size = volume.GridSize;
        Vector3 origin = volume.Origin;
        Vector3 cellSize = volume.CellSize;

        Vector3Int cells = size - Vector3Int.one;

        meshData.Bounds = volume.Bounds;

        if (cells.x <= 0 || cells.y <= 0 || cells.z <= 0)
            return meshData;

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

        for (int x = 0; x < cells.x; x++)
        {
            for (int y = 0; y < cells.y; y++)
            {
                for (int z = 0; z < cells.z; z++)
                {
                    int index = CellIndex(x, y, z, cells);

                    _cellVertexIndex[index] = CreateCellVertex(
                        values,
                        size,
                        origin,
                        cellSize,
                        x,
                        y,
                        z
                    );
                }
            }
        }

        BuildQuads(values, size, cells);

        meshData.Vertices.AddRange(_vertices);
        meshData.Triangles.AddRange(_triangles);
        meshData.Bounds = volume.Bounds;

        return meshData;
    }

    /// <summary>Converts cell coordinates into the flat cell-vertex array index.</summary>
    private static int CellIndex(int x, int y, int z, Vector3Int cells)
    {
        return x + cells.x * (y + cells.y * z);
    }

    /// <summary>Checks whether two scalar samples cross the active iso level.</summary>
    private bool HasCrossing(float a, float b)
    {
        float da = a - _isoLevel;
        float db = b - _isoLevel;

        return (da <= 0f && db > 0f) || (da > 0f && db <= 0f);
    }

    /// <summary>Interpolates the iso-surface position along one sampled edge.</summary>
    private Vector3 Interpolate(Vector3 pa, float va, Vector3 pb, float vb)
    {
        float denom = vb - va;

        if (Mathf.Abs(denom) < 1e-8f)
            return (pa + pb) * 0.5f;

        float t = Mathf.Clamp01((_isoLevel - va) / denom);
        return Vector3.Lerp(pa, pb, t);
    }

    /// <summary>Creates one dual vertex for a cell by averaging all edge crossings.</summary>
    private int CreateCellVertex(
        float[] values,
        Vector3Int size,
        Vector3 origin,
        Vector3 cellSize,
        int cellX,
        int cellY,
        int cellZ)
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

            float value = values[index];

            Vector3 position = origin + new Vector3(
                sx * cellSize.x,
                sy * cellSize.y,
                sz * cellSize.z
            );

            _cellValues[i] = value;
            _cellPositions[i] = position;

            if (value < minValue)
                minValue = value;

            if (value > maxValue)
                maxValue = value;
        }

        if (minValue > _isoLevel || maxValue < _isoLevel)
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

    /// <summary>Adds two triangles for a quad when all four cell vertices exist.</summary>
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

    /// <summary>Emits quads around all crossed grid edges.</summary>
    private void BuildQuads(float[] values, Vector3Int size, Vector3Int cells)
    {
        int sizeX = size.x;
        int sizeY = size.y;
        int zStride = sizeX * sizeY;

        // X-axis grid edges
        for (int x = 0; x < cells.x; x++)
        {
            for (int y = 1; y < cells.y; y++)
            {
                for (int z = 1; z < cells.z; z++)
                {
                    int baseIndex = x + sizeX * (y + sizeY * z);

                    float a = values[baseIndex];
                    float b = values[baseIndex + 1];

                    if (!HasCrossing(a, b))
                        continue;

                    int v0 = _cellVertexIndex[CellIndex(x, y - 1, z - 1, cells)];
                    int v1 = _cellVertexIndex[CellIndex(x, y, z - 1, cells)];
                    int v2 = _cellVertexIndex[CellIndex(x, y, z, cells)];
                    int v3 = _cellVertexIndex[CellIndex(x, y - 1, z, cells)];

                    AddQuad(v0, v1, v2, v3, a < _isoLevel);
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

                    float a = values[baseIndex];
                    float b = values[baseIndex + sizeX];

                    if (!HasCrossing(a, b))
                        continue;

                    int v0 = _cellVertexIndex[CellIndex(x - 1, y, z - 1, cells)];
                    int v1 = _cellVertexIndex[CellIndex(x, y, z - 1, cells)];
                    int v2 = _cellVertexIndex[CellIndex(x, y, z, cells)];
                    int v3 = _cellVertexIndex[CellIndex(x - 1, y, z, cells)];

                    AddQuad(v0, v1, v2, v3, a > _isoLevel);
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

                    float a = values[baseIndex];
                    float b = values[baseIndex + zStride];

                    if (!HasCrossing(a, b))
                        continue;

                    int v0 = _cellVertexIndex[CellIndex(x - 1, y - 1, z, cells)];
                    int v1 = _cellVertexIndex[CellIndex(x, y - 1, z, cells)];
                    int v2 = _cellVertexIndex[CellIndex(x, y, z, cells)];
                    int v3 = _cellVertexIndex[CellIndex(x - 1, y, z, cells)];

                    AddQuad(v0, v1, v2, v3, a < _isoLevel);
                }
            }
        }
    }
}
