using System;
using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;

public class DualContouringMesher : IVolumeMesher, IChunkVolumeMesher
{
    public bool SupportsCpu => true;
    public bool SupportsGpu => false;

    private static readonly Vector3Int[] CornerOffsets = new Vector3Int[]
    {
        new(0, 0, 0), new(1, 0, 0), new(1, 1, 0), new(0, 1, 0),
        new(0, 0, 1), new(1, 0, 1), new(1, 1, 1), new(0, 1, 1)
    };

    private readonly struct Edge
    {
        public readonly int A;
        public readonly int B;
        public Edge(int a, int b) { A = a; B = b; }
    }

    private static readonly Edge[] Edges = new Edge[]
    {
        new(0, 1), new(1, 2), new(2, 3), new(3, 0),
        new(4, 5), new(5, 6), new(6, 7), new(7, 4),
        new(0, 4), new(1, 5), new(2, 6), new(3, 7)
    };

    public DualContouringSettings Settings { get; set; }

    public DualContouringMesher()
    {
        Settings = DualContouringSettings.Default();
    }

    private NativeArray<int> _cellVertexIndex;
    private readonly float[] _cellValues = new float[8];
    private readonly Vector3[] _cellPositions = new Vector3[8];
    private readonly Vector3[] _crossings = new Vector3[12];

    // QEF path: only allocate Lists when enabled (avoids GC on hot path)
    private List<Vector3> _qefPoints;
    private List<Vector3> _qefNormals;

    public CpuMeshData BuildCpu(IVolumeBuffer buffer, MeshingContext context)
    {
        CpuMeshData combined = new CpuMeshData(Allocator.TempJob);
        Vector3Int gridSize = buffer.ChunkGridSize;

        for (int cz = 0; cz < gridSize.z; cz++)
        {
            for (int cy = 0; cy < gridSize.y; cy++)
            {
                for (int cx = 0; cx < gridSize.x; cx++)
                {
                    ChunkCoord coord = new ChunkCoord(cx, cy, cz);
                    CpuMeshData chunkMesh = BuildChunkCpu(buffer, coord, context);
                    combined.Append(chunkMesh);
                    chunkMesh.Dispose();
                }
            }
        }

        return combined;
    }

    public CpuMeshData BuildChunkCpu(IVolumeBuffer buffer, ChunkCoord coord, MeshingContext context)
    {
        DualContouringSettings settings = Settings;
        CpuMeshData mesh = new CpuMeshData(Allocator.Temp);
        VolumeLayout layout = buffer.Layout;
        NativeArray<float> density = buffer.DensityCpu;
        Vector3Int res = layout.Resolution;
        float cellSize = layout.CellSize;
        float isoLevel = context.IsoLevel;

        VolumeChunk chunk = buffer.GetChunk(coord.X, coord.Y, coord.Z);
        BoundsInt region = chunk.CellBounds;

        // Phase 1: chunk cells + trailing +1 halo for boundary quad vertices
        int phase1MinX = region.position.x;
        int phase1MinY = region.position.y;
        int phase1MinZ = region.position.z;
        int phase1MaxX = Mathf.Min(res.x - 1, region.position.x + region.size.x);
        int phase1MaxY = Mathf.Min(res.y - 1, region.position.y + region.size.y);
        int phase1MaxZ = Mathf.Min(res.z - 1, region.position.z + region.size.z);

        // Phase 2: original chunk bounds (clamped to res-1, trailing edge handled by neighbor chunk)
        int phase2MinX = region.position.x;
        int phase2MinY = region.position.y;
        int phase2MinZ = region.position.z;
        int phase2MaxX = Mathf.Min(res.x - 1, region.position.x + region.size.x);
        int phase2MaxY = Mathf.Min(res.y - 1, region.position.y + region.size.y);
        int phase2MaxZ = Mathf.Min(res.z - 1, region.position.z + region.size.z);

        // cellVertexIndex covers the Phase 1 region
        int cellsW = phase1MaxX - phase1MinX + 1;
        int cellsH = phase1MaxY - phase1MinY + 1;
        int cellsD = phase1MaxZ - phase1MinZ + 1;

        if (cellsW <= 0 || cellsH <= 0 || cellsD <= 0)
            return mesh;

        Vector3Int cells = new Vector3Int(cellsW, cellsH, cellsD);
        int estimatedCells = cellsW * cellsH * cellsD;
        _cellVertexIndex = new NativeArray<int>(estimatedCells, Allocator.Temp);
        for (int ci = 0; ci < estimatedCells; ci++) _cellVertexIndex[ci] = -1;

        int sizeX = res.x;
        int sizeY = res.y;
        Vector3 origin = layout.Origin;
        bool useQef = settings.UseQefVertices;

        // Lazily allocate QEF lists only when needed
        if (useQef && _qefPoints == null)
        {
            _qefPoints = new List<Vector3>(12);
            _qefNormals = new List<Vector3>(12);
        }

        // Phase 1: Create one vertex per surface cell (with halo)
        for (int x = phase1MinX; x < phase1MaxX; x++)
        {
            for (int y = phase1MinY; y < phase1MaxY; y++)
            {
                for (int z = phase1MinZ; z < phase1MaxZ; z++)
                {
                    int ci = CellIndexLocal(x - phase1MinX, y - phase1MinY, z - phase1MinZ, cells);

                    float minVal = float.PositiveInfinity;
                    float maxVal = float.NegativeInfinity;

                    // Gather corner values and positions
                    for (int i = 0; i < 8; i++)
                    {
                        Vector3Int o = CornerOffsets[i];
                        int sx = x + o.x, sy = y + o.y, sz = z + o.z;

                        if (sx < 0 || sx >= res.x || sy < 0 || sy >= res.y || sz < 0 || sz >= res.z)
                            continue;

                        int idx = sx + sizeX * (sy + sizeY * sz);
                        float val = density[idx];
                        _cellValues[i] = val;
                        _cellPositions[i] = origin + new Vector3(sx, sy, sz) * cellSize;

                        if (val < minVal) minVal = val;
                        if (val > maxVal) maxVal = val;
                    }

                    // Check if cell contains surface
                    if (minVal > isoLevel || maxVal < isoLevel)
                        continue;

                    // Collect edge crossings
                    int crossingCount = 0;

                    for (int e = 0; e < Edges.Length; e++)
                    {
                        Edge edge = Edges[e];
                        float va = _cellValues[edge.A];
                        float vb = _cellValues[edge.B];

                        if (!HasCrossing(va, vb, isoLevel))
                            continue;

                        Vector3 crossing = Interpolate(_cellPositions[edge.A], va, _cellPositions[edge.B], vb, isoLevel);

                        _crossings[crossingCount] = crossing;
                        crossingCount++;
                    }

                    if (crossingCount == 0)
                        continue;

                    // Compute vertex position
                    Vector3 vertex;
                    if (useQef && crossingCount >= 3)
                    {
                        _qefPoints.Clear();
                        _qefNormals.Clear();
                        for (int i = 0; i < crossingCount; i++)
                            _qefPoints.Add(_crossings[i]);

                        Bounds clampBounds = GetCellBounds(x, y, z, layout);
                        if (!QefSolver.TrySolve(_qefPoints, _qefNormals, null, clampBounds, settings.QefSolverSettings, out vertex))
                        {
                            vertex = AveragePositions(_crossings, crossingCount);
                        }
                    }
                    else
                    {
                        vertex = AveragePositions(_crossings, crossingCount);
                    }

                    int vertIdx = mesh.Vertices.Length;
                    mesh.Vertices.Add(vertex);
                    _cellVertexIndex[ci] = vertIdx;
                }
            }
        }

       try
        {
        // Phase 2: Emit quads around crossed grid edges
        int sizeZ = sizeX * sizeY;

        // X-axis edges
        for (int x = phase2MinX; x < phase2MaxX; x++)
        {
            for (int y = phase2MinY + 1; y < phase2MaxY; y++)
            {
                for (int z = phase2MinZ + 1; z < phase2MaxZ; z++)
                {
                    int baseIdx = x + sizeX * (y + sizeY * z);
                    float a = density[baseIdx];
                    float b = density[baseIdx + 1];

                    if (!HasCrossing(a, b, isoLevel)) continue;

                    int v0 = GetVertexAt(x, y - 1, z - 1, phase1MinX, phase1MinY, phase1MinZ, cells);
                    int v1 = GetVertexAt(x, y, z - 1, phase1MinX, phase1MinY, phase1MinZ, cells);
                    int v2 = GetVertexAt(x, y, z, phase1MinX, phase1MinY, phase1MinZ, cells);
                    int v3 = GetVertexAt(x, y - 1, z, phase1MinX, phase1MinY, phase1MinZ, cells);

                    AddQuad(ref mesh, v0, v1, v2, v3, a < isoLevel);
                }
            }
        }

        // Y-axis edges
        for (int x = phase2MinX + 1; x < phase2MaxX; x++)
        {
            for (int y = phase2MinY; y < phase2MaxY; y++)
            {
                for (int z = phase2MinZ + 1; z < phase2MaxZ; z++)
                {
                    int baseIdx = x + sizeX * (y + sizeY * z);
                    float a = density[baseIdx];
                    float b = density[baseIdx + sizeX];

                    if (!HasCrossing(a, b, isoLevel)) continue;

                    int v0 = GetVertexAt(x - 1, y, z - 1, phase1MinX, phase1MinY, phase1MinZ, cells);
                    int v1 = GetVertexAt(x, y, z - 1, phase1MinX, phase1MinY, phase1MinZ, cells);
                    int v2 = GetVertexAt(x, y, z, phase1MinX, phase1MinY, phase1MinZ, cells);
                    int v3 = GetVertexAt(x - 1, y, z, phase1MinX, phase1MinY, phase1MinZ, cells);

                    AddQuad(ref mesh, v0, v1, v2, v3, a > isoLevel);
                }
            }
        }

        // Z-axis edges
        for (int x = phase2MinX + 1; x < phase2MaxX; x++)
        {
            for (int y = phase2MinY + 1; y < phase2MaxY; y++)
            {
                for (int z = phase2MinZ; z < phase2MaxZ; z++)
                {
                    int baseIdx = x + sizeX * (y + sizeY * z);
                    float a = density[baseIdx];
                    float b = density[baseIdx + sizeZ];

                    if (!HasCrossing(a, b, isoLevel)) continue;

                    int v0 = GetVertexAt(x - 1, y - 1, z, phase1MinX, phase1MinY, phase1MinZ, cells);
                    int v1 = GetVertexAt(x, y - 1, z, phase1MinX, phase1MinY, phase1MinZ, cells);
                    int v2 = GetVertexAt(x, y, z, phase1MinX, phase1MinY, phase1MinZ, cells);
                    int v3 = GetVertexAt(x - 1, y, z, phase1MinX, phase1MinY, phase1MinZ, cells);

                    AddQuad(ref mesh, v0, v1, v2, v3, a < isoLevel);
                }
            }
        }
        }
        finally
        {
            if (_cellVertexIndex.IsCreated) _cellVertexIndex.Dispose();
        }

        return mesh;
    }

    private int GetVertexAt(int gx, int gy, int gz, int offsetX, int offsetY, int offsetZ, Vector3Int cells)
    {
        if (gx < offsetX || gx >= offsetX + cells.x ||
            gy < offsetY || gy >= offsetY + cells.y ||
            gz < offsetZ || gz >= offsetZ + cells.z)
            return -1;

        return _cellVertexIndex[CellIndexLocal(gx - offsetX, gy - offsetY, gz - offsetZ, cells)];
    }

    private static int CellIndexLocal(int lx, int ly, int lz, Vector3Int cells)
    {
        return lx + cells.x * (ly + cells.y * lz);
    }

    private static bool HasCrossing(float a, float b, float isoLevel)
    {
        float da = a - isoLevel;
        float db = b - isoLevel;
        return (da <= 0f && db > 0f) || (da > 0f && db <= 0f);
    }

    private static Vector3 Interpolate(Vector3 pa, float va, Vector3 pb, float vb, float isoLevel)
    {
        float denom = vb - va;
        if (Mathf.Abs(denom) < 1e-8f)
            return (pa + pb) * 0.5f;
        float t = Mathf.Clamp01((isoLevel - va) / denom);
        return Vector3.Lerp(pa, pb, t);
    }

    private static Vector3 AveragePositions(Vector3[] crossings, int count)
    {
        if (count == 0) return Vector3.zero;
        Vector3 sum = Vector3.zero;
        for (int i = 0; i < count; i++)
            sum += crossings[i];
        return sum / count;
    }

    private static Bounds GetCellBounds(int cx, int cy, int cz, VolumeLayout layout)
    {
        Vector3 min = layout.Origin + new Vector3(cx, cy, cz) * layout.CellSize;
        Vector3 max = min + new Vector3(layout.CellSize, layout.CellSize, layout.CellSize);
        return new Bounds((min + max) * 0.5f, max - min);
    }

    private static void AddQuad(ref CpuMeshData mesh, int v0, int v1, int v2, int v3, bool flip)
    {
        if (v0 < 0 || v1 < 0 || v2 < 0 || v3 < 0) return;

        if (flip)
        {
            mesh.Indices.Add(v0); mesh.Indices.Add(v1); mesh.Indices.Add(v2);
            mesh.Indices.Add(v0); mesh.Indices.Add(v2); mesh.Indices.Add(v3);
        }
        else
        {
            mesh.Indices.Add(v0); mesh.Indices.Add(v2); mesh.Indices.Add(v1);
            mesh.Indices.Add(v0); mesh.Indices.Add(v3); mesh.Indices.Add(v2);
        }
    }

    public GpuMeshData BuildGpu(IVolumeBuffer buffer, MeshingContext context)
    {
        throw new NotSupportedException("GPU Dual Contouring not yet implemented.");
    }
}