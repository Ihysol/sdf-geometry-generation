using System;
using Unity.Collections;
using UnityEngine;

public class SurfaceNetsMesher : IVolumeMesher
{
    public bool SupportsCpu => true;
    public bool SupportsGpu => false;

    public CpuMeshData BuildCpu(IVolumeBuffer buffer, MeshingContext context)
    {
        CpuMeshData mesh = new CpuMeshData(Allocator.Temp);
        VolumeLayout layout = buffer.Layout;
        NativeArray<float> density = buffer.DensityCpu;
        Vector3Int res = layout.Resolution;
        float cellSize = layout.CellSize;
        float isoLevel = context.IsoLevel;

        for (int axis = 0; axis < 3; axis++)
        {
            BuildPlane(mesh, density, res, layout, cellSize, isoLevel, axis, true, context.GenerateNormals);
            BuildPlane(mesh, density, res, layout, cellSize, isoLevel, axis, false, context.GenerateNormals);
        }

        return mesh;
    }

    private static void BuildPlane(CpuMeshData mesh, NativeArray<float> density, Vector3Int res, VolumeLayout layout, float cellSize, float isoLevel, int axis, bool positive, bool generateNormals)
    {
        for (int iz = 0; iz < res.z; iz++)
        {
            for (int iy = 0; iy < res.y; iy++)
            {
                for (int ix = 0; ix < res.x; ix++)
                {
                    Vector3Int pos = new Vector3Int(ix, iy, iz);

                    if (!IsValidCell(pos, res, axis, positive)) continue;

                    float[] cornerValues = GetCornerValues(density, res, layout, pos, axis, positive, isoLevel);
                    int sgn = ComputeSurfaceSign(cornerValues);

                    if (sgn == 0) continue;

                    Vector3 vertexPos = ComputeVertex(pos, axis, positive, cellSize, cornerValues);
                    int baseVert = mesh.Vertices.Length;

                    EmitTriangles(mesh, axis, positive, sgn, baseVert, vertexPos, generateNormals);
                }
            }
        }
    }

    private static bool IsValidCell(Vector3Int pos, Vector3Int res, int axis, bool positive)
    {
        switch (axis)
        {
            case 0: return positive ? pos.x + 1 < res.x : pos.x > 0;
            case 1: return positive ? pos.y + 1 < res.y : pos.y > 0;
            case 2: return positive ? pos.z + 1 < res.z : pos.z > 0;
        }
        return false;
    }

    private static float[] GetCornerValues(NativeArray<float> density, Vector3Int res, VolumeLayout layout, Vector3Int pos, int axis, bool positive, float isoLevel)
    {
        float[] vals = new float[4];

        for (int i = 0; i < 4; i++)
        {
            Vector3Int corner = GetCornerPos(pos, axis, positive, i);
            if (!layout.IsInside(corner))
            {
                vals[i] = isoLevel + 1f;
            }
            else
            {
                vals[i] = density[corner.x + res.x * (corner.y + res.y * corner.z)] - isoLevel;
            }
        }

        return vals;
    }

    private static Vector3Int GetCornerPos(Vector3Int cell, int axis, bool positive, int cornerIdx)
    {
        Vector3Int p = cell;

        switch (axis)
        {
            case 0: // X-face, corners vary in Y,Z
                if (positive) p.x++;
                else p.x--;
                p.y += cornerIdx & 1;
                p.z += (cornerIdx >> 1) & 1;
                break;
            case 1: // Y-face, corners vary in X,Z
                if (positive) p.y++;
                else p.y--;
                p.x += cornerIdx & 1;
                p.z += (cornerIdx >> 1) & 1;
                break;
            case 2: // Z-face, corners vary in X,Y
                if (positive) p.z++;
                else p.z--;
                p.x += cornerIdx & 1;
                p.y += (cornerIdx >> 1) & 1;
                break;
        }

        return p;
    }

    private static int ComputeSurfaceSign(float[] vals)
    {
        bool anyPositive = false;
        bool anyNegative = false;

        for (int i = 0; i < 4; i++)
        {
            if (vals[i] > 0) anyPositive = true;
            else anyNegative = true;
        }

        if (!anyPositive || !anyNegative) return 0;

        bool c0 = vals[0] > 0;
        bool c1 = vals[1] > 0;
        bool c2 = vals[2] > 0;
        bool c3 = vals[3] > 0;

        if (c0 == c2) return 1;
        if (c1 == c3) return -1;
        return 0;
    }

    private static Vector3 ComputeVertex(Vector3Int pos, int axis, bool positive, float cellSize, float[] vals)
    {
        float[] absVals = new float[4];
        for (int i = 0; i < 4; i++) absVals[i] = Mathf.Abs(vals[i]);

        float totalW = absVals[0] + absVals[1] + absVals[2] + absVals[3];
        if (totalW == 0f) return new Vector3(pos.x, pos.y, pos.z) * cellSize;

        float[] w = new float[4];
        for (int i = 0; i < 4; i++) w[i] = absVals[i] / totalW;

        Vector3 center = new Vector3(pos.x + 0.5f, pos.y + 0.5f, pos.z + 0.5f) * cellSize;

        switch (axis)
        {
            case 0:
                center.x += (w[0] - w[1] - w[2] + w[3]) * 0.5f * cellSize;
                break;
            case 1:
                center.y += (w[0] - w[1] - w[2] + w[3]) * 0.5f * cellSize;
                break;
            case 2:
                center.z += (w[0] - w[1] - w[2] + w[3]) * 0.5f * cellSize;
                break;
        }

        return center;
    }

    private static void EmitTriangles(CpuMeshData mesh, int axis, bool positive, int sign, int baseVert, Vector3 vertexPos, bool generateNormals)
    {
        mesh.Vertices.Add(vertexPos);

        if (generateNormals)
        {
            Vector3 normal = GetFaceNormal(axis, positive);
            mesh.Normals.Add(normal);
        }

        switch (axis)
        {
            case 0:
                if (positive)
                {
                    if (sign > 0)
                    {
                        mesh.Indices.Add(baseVert);
                        mesh.Indices.Add(baseVert + 1);
                        mesh.Indices.Add(baseVert + 2);
                    }
                    else
                    {
                        mesh.Indices.Add(baseVert);
                        mesh.Indices.Add(baseVert + 3);
                        mesh.Indices.Add(baseVert + 2);
                    }
                }
                else
                {
                    if (sign > 0)
                    {
                        mesh.Indices.Add(baseVert);
                        mesh.Indices.Add(baseVert + 2);
                        mesh.Indices.Add(baseVert + 1);
                    }
                    else
                    {
                        mesh.Indices.Add(baseVert);
                        mesh.Indices.Add(baseVert + 3);
                        mesh.Indices.Add(baseVert + 2);
                    }
                }
                break;

            case 1:
                if (positive)
                {
                    if (sign > 0)
                    {
                        mesh.Indices.Add(baseVert);
                        mesh.Indices.Add(baseVert + 1);
                        mesh.Indices.Add(baseVert + 2);
                    }
                    else
                    {
                        mesh.Indices.Add(baseVert);
                        mesh.Indices.Add(baseVert + 3);
                        mesh.Indices.Add(baseVert + 2);
                    }
                }
                else
                {
                    if (sign > 0)
                    {
                        mesh.Indices.Add(baseVert);
                        mesh.Indices.Add(baseVert + 2);
                        mesh.Indices.Add(baseVert + 1);
                    }
                    else
                    {
                        mesh.Indices.Add(baseVert);
                        mesh.Indices.Add(baseVert + 3);
                        mesh.Indices.Add(baseVert + 2);
                    }
                }
                break;

            case 2:
                if (positive)
                {
                    if (sign > 0)
                    {
                        mesh.Indices.Add(baseVert);
                        mesh.Indices.Add(baseVert + 1);
                        mesh.Indices.Add(baseVert + 2);
                    }
                    else
                    {
                        mesh.Indices.Add(baseVert);
                        mesh.Indices.Add(baseVert + 3);
                        mesh.Indices.Add(baseVert + 2);
                    }
                }
                else
                {
                    if (sign > 0)
                    {
                        mesh.Indices.Add(baseVert);
                        mesh.Indices.Add(baseVert + 2);
                        mesh.Indices.Add(baseVert + 1);
                    }
                    else
                    {
                        mesh.Indices.Add(baseVert);
                        mesh.Indices.Add(baseVert + 3);
                        mesh.Indices.Add(baseVert + 2);
                    }
                }
                break;
        }
    }

    private static Vector3 GetFaceNormal(int axis, bool positive)
    {
        switch (axis)
        {
            case 0: return positive ? Vector3.right : -Vector3.right;
            case 1: return positive ? Vector3.up : -Vector3.up;
            case 2: return positive ? Vector3.forward : -Vector3.forward;
        }
        return Vector3.up;
    }

    public GpuMeshData BuildGpu(IVolumeBuffer buffer, MeshingContext context)
    {
        throw new NotSupportedException("GPU Surface Nets not yet implemented.");
    }
}
