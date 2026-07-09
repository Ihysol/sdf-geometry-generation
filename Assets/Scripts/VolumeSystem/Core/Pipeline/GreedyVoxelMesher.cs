using System;
using Unity.Collections;
using UnityEngine;

public class GreedyVoxelMesher : IVolumeMesher
{
    public bool SupportsCpu => true;
    public bool SupportsGpu => false;

    // +X, -X, +Y, -Y, +Z, -Z
    private static readonly Vector3Int[] Normals = new Vector3Int[]
    {
        new(1,0,0), new(-1,0,0), new(0,1,0), new(0,-1,0), new(0,0,1), new(0,0,-1)
    };

    public CpuMeshData BuildCpu(IVolumeBuffer buffer, MeshingContext context)
    {
        CpuMeshData mesh = new CpuMeshData(Allocator.Temp);
        VolumeLayout layout = buffer.Layout;
        NativeArray<float> density = buffer.DensityCpu;
        Vector3Int res = layout.Resolution;
        float cellSize = layout.CellSize;
        float isoLevel = context.IsoLevel;

        bool genNormals = context.GenerateNormals;

        foreach (Vector3Int normal in Normals)
        {
            if (normal.x != 0) ProcessAxis(ref mesh, density, res, layout, cellSize, isoLevel, 0, 1, 2, normal, genNormals);
            else if (normal.y != 0) ProcessAxis(ref mesh, density, res, layout, cellSize, isoLevel, 1, 0, 2, normal, genNormals);
            else ProcessAxis(ref mesh, density, res, layout, cellSize, isoLevel, 2, 0, 1, normal, genNormals);
        }

        return mesh;
    }

    // axisN = normal axis, axisA = first plane axis, axisB = second plane axis
    private void ProcessAxis(
        ref CpuMeshData mesh,
        NativeArray<float> density,
        Vector3Int res,
        VolumeLayout layout,
        float cellSize,
        float isoLevel,
        int axisN, int axisA, int axisB,
        Vector3Int normal,
        bool genNormals)
    {
        int rN = GetAxis(res, axisN);
        int rA = GetAxis(res, axisA);
        int rB = GetAxis(res, axisB);

        NativeArray<bool> visited = new NativeArray<bool>(rA * rB, Allocator.Temp);

        for (int n = 0; n < rN; n++)
        {
            Vector3Int cellCoord = Vector3Int.zero;
            Vector3Int neighborCoord = Vector3Int.zero;
            SetAxis(ref cellCoord, axisN, n);
            SetAxis(ref neighborCoord, axisN, n + GetAxis(normal, axisN));

            if (GetAxis(neighborCoord, axisN) < 0 || GetAxis(neighborCoord, axisN) >= GetAxis(res, axisN)) continue;

            for (int vi = 0; vi < visited.Length; vi++) visited[vi] = false;

            for (int a = 0; a < rA; a++)
            {
                for (int b = 0; b < rB; b++)
                {
                    if (visited[a * rB + b]) continue;

                    SetAxis(ref cellCoord, axis(axisA), a);
                    SetAxis(ref cellCoord, axis(axisB), b);
                    neighborCoord = cellCoord + normal;

                    if (!IsInside(res, cellCoord) || !IsInside(res, neighborCoord)) continue;

                    float cv = density[layout.IndexToOffset(cellCoord)] - isoLevel;
                    float nv = density[layout.IndexToOffset(neighborCoord)] - isoLevel;

                    bool cellIn = cv > 0;
                    bool neighIn = nv > 0;

                    if (cellIn == neighIn) continue;

                    // For +N direction: emit if cell is inside. For -N: emit if neighbor is inside.
                    int ndir = GetAxis(normal, axisN);
                    if (ndir == 1 && !cellIn) continue;
                    if (ndir == -1 && !neighIn) continue;

                    // Check all corners have the same inside state for a valid face
                    bool uniformCell = cellIn;
                    bool uniformNeigh = neighIn;

                    // Greedy extend in axisA
                    int lenA = 1;
                    while (a + lenA < rA)
                    {
                        if (visited[(a + lenA) * rB + b]) break;

                        Vector3Int ec = cellCoord;
                        SetAxis(ref ec, axis(axisA), a + lenA);
                        Vector3Int en = ec + normal;

                        if (!IsInside(res, ec) || !IsInside(res, en)) break;

                        float ecv = density[layout.IndexToOffset(ec)] - isoLevel;
                        float env = density[layout.IndexToOffset(en)] - isoLevel;
                        bool eci = ecv > 0;
                        bool eni = env > 0;

                        if (eci == eni) break;
                        if (ndir == 1 && !eci) break;
                        if (ndir == -1 && !eni) break;

                        lenA++;
                    }

                    // Greedy extend in axisB while keeping rectangle uniform
                    int lenB = 1;
                    while (b + lenB < rB)
                    {
                        bool ok = true;
                        for (int ea = 0; ea < lenA; ea++)
                        {
                            if (visited[(a + ea) * rB + b + lenB]) { ok = false; break; }

                            Vector3Int ec = cellCoord;
                            SetAxis(ref ec, axis(axisA), a + ea);
                            SetAxis(ref ec, axis(axisB), b + lenB);
                            Vector3Int en = ec + normal;

                            if (!IsInside(res, ec) || !IsInside(res, en)) { ok = false; break; }

                            float ecv = density[layout.IndexToOffset(ec)] - isoLevel;
                            float env = density[layout.IndexToOffset(en)] - isoLevel;
                            bool eci = ecv > 0;
                            bool eni = env > 0;

                            if (eci == eni) { ok = false; break; }
                            if (ndir == 1 && !eci) { ok = false; break; }
                            if (ndir == -1 && !eni) { ok = false; break; }
                        }
                        if (!ok) break;
                        lenB++;
                    }

                    // Mark visited
                    for (int va = 0; va < lenA; va++)
                        for (int vb = 0; vb < lenB; vb++)
                            visited[(a + va) * rB + b + vb] = true;

                    // Emit merged quad
                    Vector3 basePos = GetFaceCornerWorld(cellCoord, normal, axisN, axisA, axisB, cellSize, isoLevel, density, layout);
                    Vector3 dirA = new Vector3(GetAxisInt(axis(axisA)), GetAxisInt(axis(axisA), 1), GetAxisInt(axis(axisA), 2)) * cellSize;
                    Vector3 dirB = new Vector3(GetAxisInt(axis(axisB)), GetAxisInt(axis(axisB), 1), GetAxisInt(axis(axisB), 2)) * cellSize;

                    // Recompute properly
                    Vector3 da = Vector3.zero;
                    SetAxis(ref da, axis(axisA), lenA * cellSize);
                    Vector3 db = Vector3.zero;
                    SetAxis(ref db, axis(axisB), lenB * cellSize);

                    int vi0 = mesh.Vertices.Length;
                    mesh.Vertices.Add(basePos);
                    mesh.Vertices.Add(basePos + da);
                    mesh.Vertices.Add(basePos + da + db);
                    mesh.Vertices.Add(basePos + db);

                    if (genNormals)
                    {
                        Vector3 faceNormal = new Vector3(normal.x, normal.y, normal.z).normalized;
                        for (int i = 0; i < 4; i++) mesh.Normals.Add(faceNormal);
                    }

                    mesh.Indices.Add(vi0); mesh.Indices.Add(vi0 + 1); mesh.Indices.Add(vi0 + 2);
                    mesh.Indices.Add(vi0); mesh.Indices.Add(vi0 + 2); mesh.Indices.Add(vi0 + 3);
                }
            }
        }

        visited.Dispose();
    }

    private Vector3 GetFaceCornerWorld(Vector3Int cellCoord, Vector3Int normal, int axisN, int axisA, int axisB, float cellSize, float isoLevel, NativeArray<float> density, VolumeLayout layout)
    {
        Vector3Int neighbor = cellCoord + normal;
        float cv = density[layout.IndexToOffset(cellCoord)];
        float nv = density[layout.IndexToOffset(neighbor)];

        // Base corner is at the face boundary in the direction of the normal
        Vector3 baseCell = new Vector3(cellCoord.x, cellCoord.y, cellCoord.z) * cellSize;

        int ndir = GetAxis(normal, axisN);
        if (ndir == 1)
        {
            // Face is on the far side of cellCoord in the normal direction
            float t = Mathf.Clamp01((isoLevel - cv) / (nv - cv));
            return baseCell + new Vector3(normal.x, normal.y, normal.z) * cellSize * t;
        }
        else
        {
            // Face is on the near side of cellCoord in the normal direction
            float t = Mathf.Clamp01((isoLevel - nv) / (cv - nv));
            return baseCell + new Vector3(normal.x, normal.y, normal.z) * cellSize * (-1f + t);
        }
    }

    private static int axis(int ax) => ax;
    private static int GetAxis(Vector3Int v, int ax) => ax == 0 ? v.x : (ax == 1 ? v.y : v.z);
    private static int GetAxisInt(int ax, int yOff = 0, int zOff = 0) => ax;

    private static void SetAxis(ref Vector3Int v, int ax, int val)
    {
        if (ax == 0) v.x = val;
        else if (ax == 1) v.y = val;
        else v.z = val;
    }

    private static void SetAxis(ref Vector3 v, int ax, float val)
    {
        if (ax == 0) v.x = val;
        else if (ax == 1) v.y = val;
        else v.z = val;
    }

    private static bool IsInside(Vector3Int res, Vector3Int pos)
    {
        return pos.x >= 0 && pos.x < res.x &&
               pos.y >= 0 && pos.y < res.y &&
               pos.z >= 0 && pos.z < res.z;
    }

    public GpuMeshData BuildGpu(IVolumeBuffer buffer, MeshingContext context)
    {
        throw new NotSupportedException("GPU Greedy Voxel meshing not yet implemented.");
    }
}
