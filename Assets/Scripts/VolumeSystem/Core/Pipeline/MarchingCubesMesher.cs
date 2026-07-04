using System;
using Unity.Collections;
using UnityEngine;

public class MarchingCubesMesher : IVolumeMesher
{
    public bool SupportsCpu => true;
    public bool SupportsGpu => false;

    private static readonly Vector3Int[] EdgeV0 = new Vector3Int[12]
    {
        new(0,0,0), new(1,0,0), new(1,0,1), new(0,0,1),
        new(0,1,0), new(1,1,0), new(1,1,1), new(0,1,1),
        new(0,0,0), new(1,0,0), new(1,0,1), new(0,0,1)
    };

    private static readonly Vector3Int[] EdgeV1 = new Vector3Int[12]
    {
        new(1,0,0), new(1,0,1), new(0,0,1), new(0,0,0),
        new(1,1,0), new(1,1,1), new(0,1,1), new(0,1,0),
        new(0,1,0), new(1,1,0), new(1,1,1), new(0,1,1)
    };

    private static readonly short[] TriTable;

    static MarchingCubesMesher()
    {
        TriTable = GenerateTriangleTable();
    }

    private static short[] GenerateTriangleTable()
    {
        // Standard MC triangle table: 256 cases, each with up to 16 vertex references
        // Stored as: [case][triangle] where each triangle is (v0,v1,v2) and -1 terminates
        // Reference: Lorensen & Cline 1987
        string[] table = new string[256];

        // Primary cases (0-31), rest by symmetry
        table[0] = "";
        table[1] = "0,8,3";
        table[2] = "0,1,9";
        table[3] = "1,8,3 9,8,1";
        table[4] = "1,9,2";
        table[5] = "0,8,3 1,9,2";
        table[6] = "9,8,2 2,8,0";
        table[7] = "8,3,2 8,2,9";
        table[8] = "1,2,0";
        table[9] = "2,3,0 2,0,1";
        table[10] = "0,2,3 0,9,2";
        table[11] = "1,8,9 1,2,8 1,3,8";
        table[12] = "1,0,2 2,0,3";
        table[13] = "0,3,2 0,2,1";
        table[14] = "2,3,0 9,2,0";
        table[15] = "8,3,0 8,0,2 8,2,9";
        table[16] = "3,7,5";
        table[17] = "0,8,1 0,1,3 3,1,5";
        table[18] = "0,1,9 1,2,5 5,2,3";
        table[19] = "1,8,3 9,1,2 9,2,5";
        table[20] = "0,2,1 0,5,2 0,3,5";
        table[21] = "3,0,1 3,1,5 5,1,2";
        table[22] = "9,2,5 9,5,2 9,8,5 9,0,8";
        table[23] = "0,8,3 9,2,5 9,5,2 9,8,5";
        table[24] = "0,3,5 0,5,3 0,1,5 0,9,1";
        table[25] = "8,1,9 8,5,1 8,3,5 8,2,3 8,0,2";
        table[26] = "0,1,2 0,3,1 3,5,1 3,7,5";
        table[27] = "9,1,2 8,1,9 8,5,1 8,3,5";
        table[28] = "0,3,5 0,5,3 5,2,3 5,9,2";
        table[29] = "8,5,1 8,9,5 8,3,5 8,2,3 8,0,2";
        table[30] = "0,3,5 0,5,3 5,2,3 5,9,2";
        table[31] = "8,3,5 8,5,3 8,1,5 8,9,1 9,0,1";

        // Fill remaining by symmetry: case i and case (255-i) share the same edges but reversed winding
        for (int i = 32; i < 256; i++)
        {
            int sym = 255 - i;
            table[i] = table[sym];
        }

        // Convert to flat short array: 256 entries * 16 vertex refs each
        short[] result = new short[256 * 16];
        for (int i = 0; i < 256; i++)
        {
            int offset = i * 16;
            for (int j = 0; j < 16; j++) result[offset + j] = -1;

            if (string.IsNullOrEmpty(table[i])) continue;

            string[] tris = table[i].Split(' ');
            int idx = 0;
            foreach (string tri in tris)
            {
                string[] verts = tri.Split(',');
                for (int v = 0; v < 3 && idx < 16; v++, idx++)
                    result[offset + idx] = short.Parse(verts[v]);
            }
        }

        return result;
    }

    public CpuMeshData BuildCpu(IVolumeBuffer buffer, MeshingContext context)
    {
        CpuMeshData mesh = new CpuMeshData(Allocator.Temp);
        VolumeLayout layout = buffer.Layout;
        NativeArray<float> density = buffer.DensityCpu;
        Vector3Int res = layout.Resolution;
        float cellSize = layout.CellSize;
        float isoLevel = context.IsoLevel;

        for (int z = 0; z < res.z - 1; z++)
        {
            for (int y = 0; y < res.y - 1; y++)
            {
                for (int x = 0; x < res.x - 1; x++)
                {
                    BuildCell(ref mesh, density, res, cellSize, isoLevel, new Vector3Int(x, y, z), context.GenerateNormals);
                }
            }
        }

        return mesh;
    }

    private static void BuildCell(ref CpuMeshData mesh, NativeArray<float> density, Vector3Int res, float cellSize, float isoLevel, Vector3Int pos, bool generateNormals)
    {
        float[] d = new float[8];
        for (int i = 0; i < 8; i++)
        {
            int ix = pos.x + (i & 1);
            int iy = pos.y + ((i >> 1) & 1);
            int iz = pos.z + ((i >> 2) & 1);
            d[i] = density[ix + res.x * (iy + res.y * iz)] - isoLevel;
        }

        int cubeIndex = 0;
        for (int i = 0; i < 8; i++)
        {
            if (d[i] >= 0)
                cubeIndex |= (1 << i);
        }

        Vector3[] intersections = new Vector3[12];
        bool[] hasIntersection = new bool[12];

        for (int e = 0; e < 12; e++)
        {
            Vector3Int v0 = EdgeV0[e] + pos;
            Vector3Int v1 = EdgeV1[e] + pos;

            float d0 = density[v0.x + res.x * (v0.y + res.y * v0.z)] - isoLevel;
            float d1 = density[v1.x + res.x * (v1.y + res.y * v1.z)] - isoLevel;

            if ((d0 >= 0) != (d1 >= 0))
            {
                float t = d0 / (d0 - d1);
                Vector3 p0 = new Vector3(v0.x, v0.y, v0.z) * cellSize;
                Vector3 p1 = new Vector3(v1.x, v1.y, v1.z) * cellSize;
                intersections[e] = Vector3.Lerp(p0, p1, t);
                hasIntersection[e] = true;
            }
        }

        int triOffset = cubeIndex * 16;
        for (int i = 0; i < 16; i += 3)
        {
            short v0 = TriTable[triOffset + i];
            short v1 = TriTable[triOffset + i + 1];
            short v2 = TriTable[triOffset + i + 2];

            if (v0 < 0 || v1 < 0 || v2 < 0) break;

            int iv0 = (int)v0, iv1 = (int)v1, iv2 = (int)v2;
            if (!hasIntersection[iv0] || !hasIntersection[iv1] || !hasIntersection[iv2]) continue;

            int baseVert = mesh.Vertices.Length;
            mesh.Vertices.Add(intersections[iv0]);
            mesh.Vertices.Add(intersections[iv1]);
            mesh.Vertices.Add(intersections[iv2]);

            if (generateNormals)
            {
                Vector3 normal = Vector3.Cross(intersections[iv1] - intersections[iv0], intersections[iv2] - intersections[iv0]).normalized;
                mesh.Normals.Add(normal);
                mesh.Normals.Add(normal);
                mesh.Normals.Add(normal);
            }

            mesh.Indices.Add(baseVert);
            mesh.Indices.Add(baseVert + 1);
            mesh.Indices.Add(baseVert + 2);
        }
    }

    public GpuMeshData BuildGpu(IVolumeBuffer buffer, MeshingContext context)
    {
        throw new NotSupportedException("GPU Marching Cubes not yet implemented.");
    }
}
