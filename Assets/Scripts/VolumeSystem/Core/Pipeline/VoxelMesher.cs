using System;
using Unity.Collections;
using UnityEngine;

public class VoxelMesher : IVolumeMesher
{
    public bool SupportsCpu => true;
    public bool SupportsGpu => false;

    private static readonly Vector3Int[] FaceNormals = new Vector3Int[]
    {
        new Vector3Int(1, 0, 0),   // +X
        new Vector3Int(-1, 0, 0),  // -X
        new Vector3Int(0, 1, 0),   // +Y
        new Vector3Int(0, -1, 0),  // -Y
        new Vector3Int(0, 0, 1),   // +Z
        new Vector3Int(0, 0, -1),  // -Z
    };

    private static readonly int[][] FaceVertexOffsets = new int[][]
    {
        new[] { 1, 5, 7, 3 },   // +X
        new[] { 0, 4, 6, 2 },   // -X
        new[] { 3, 7, 6, 2 },   // +Y
        new[] { 0, 1, 5, 4 },   // -Y
        new[] { 4, 5, 7, 6 },   // +Z
        new[] { 0, 2, 3, 1 },   // -Z
    };

    private static readonly int[][] FaceEdges = new int[][]
    {
        new[] { 0, 4, 5, 1 },  // +X corners
        new[] { 0, 4, 5, 1 },  // -X corners
        new[] { 2, 3, 6, 7 },  // +Y corners
        new[] { 0, 1, 4, 5 },  // -Y corners
        new[] { 4, 5, 6, 7 },  // +Z corners
        new[] { 0, 1, 2, 3 },  // -Z corners
    };

    public CpuMeshData BuildCpu(IVolumeBuffer buffer, MeshingContext context)
    {
        CpuMeshData mesh = new CpuMeshData(Allocator.Temp);

        VolumeLayout layout = buffer.Layout;
        NativeArray<float> density = buffer.DensityCpu;
        Vector3Int res = layout.Resolution;
        float cellSize = layout.CellSize;
        float isoLevel = context.IsoLevel;

        int vertOffset = 0;

        for (int z = 0; z < res.z; z++)
        {
            for (int y = 0; y < res.y; y++)
            {
                for (int x = 0; x < res.x; x++)
                {
                    Vector3Int cellIndex = new Vector3Int(x, y, z);

                    for (int face = 0; face < 6; face++)
                    {
                        Vector3Int neighbor = cellIndex + FaceNormals[face];

                        if (neighbor.x < 0 || neighbor.x >= res.x ||
                            neighbor.y < 0 || neighbor.y >= res.y ||
                            neighbor.z < 0 || neighbor.z >= res.z)
                        {
                            continue;
                        }

                        float cellDensity = density[layout.IndexToOffset(cellIndex)];
                        float neighborDensity = density[layout.IndexToOffset(neighbor)];

                        bool cellInside = cellDensity > isoLevel;
                        bool neighborInside = neighborDensity > isoLevel;

                        if (cellInside == neighborInside)
                            continue;

                        Vector3 normal = FaceNormals[face];
                        int[] vertexOrder = FaceVertexOffsets[face];

                        int baseVertIndex = vertOffset;
                        vertOffset += 4;

                        for (int vi = 0; vi < 4; vi++)
                        {
                            Vector3Int cornerOffset = GetCornerOffset(face, vi);
                            Vector3Int cornerIndex = cellIndex + cornerOffset;
                            float cornerDensity = density[layout.IndexToOffset(cornerIndex)];
                            float t = (isoLevel - cellDensity) / (neighborDensity - cellDensity);
                            t = Mathf.Clamp01(t);

                            Vector3 cornerWorld = layout.IndexToWorld(cellIndex);
                            cornerWorld += new Vector3(cornerOffset.x, cornerOffset.y, cornerOffset.z) * cellSize;
                            cornerWorld += normal * cellSize * t;

                            mesh.Vertices.Add(cornerWorld);
                            if (context.GenerateNormals)
                                mesh.Normals.Add(normal);
                        }

                        mesh.Indices.Add(baseVertIndex);
                        mesh.Indices.Add(baseVertIndex + 1);
                        mesh.Indices.Add(baseVertIndex + 2);
                        mesh.Indices.Add(baseVertIndex);
                        mesh.Indices.Add(baseVertIndex + 2);
                        mesh.Indices.Add(baseVertIndex + 3);
                    }
                }
            }
        }

        return mesh;
    }

    private static Vector3Int GetCornerOffset(int face, int corner)
    {
        switch (face)
        {
            case 0: // +X
                switch (corner)
                {
                    case 0: return new Vector3Int(1, 0, 0);
                    case 1: return new Vector3Int(1, 1, 0);
                    case 2: return new Vector3Int(1, 1, 1);
                    case 3: return new Vector3Int(1, 0, 1);
                }
                break;
            case 1: // -X
                switch (corner)
                {
                    case 0: return new Vector3Int(0, 0, 0);
                    case 1: return new Vector3Int(0, 1, 0);
                    case 2: return new Vector3Int(0, 1, 1);
                    case 3: return new Vector3Int(0, 0, 1);
                }
                break;
            case 2: // +Y
                switch (corner)
                {
                    case 0: return new Vector3Int(0, 1, 0);
                    case 1: return new Vector3Int(1, 1, 0);
                    case 2: return new Vector3Int(1, 1, 1);
                    case 3: return new Vector3Int(0, 1, 1);
                }
                break;
            case 3: // -Y
                switch (corner)
                {
                    case 0: return new Vector3Int(0, 0, 0);
                    case 1: return new Vector3Int(1, 0, 0);
                    case 2: return new Vector3Int(1, 0, 1);
                    case 3: return new Vector3Int(0, 0, 1);
                }
                break;
            case 4: // +Z
                switch (corner)
                {
                    case 0: return new Vector3Int(0, 0, 1);
                    case 1: return new Vector3Int(1, 0, 1);
                    case 2: return new Vector3Int(1, 1, 1);
                    case 3: return new Vector3Int(0, 1, 1);
                }
                break;
            case 5: // -Z
                switch (corner)
                {
                    case 0: return new Vector3Int(0, 0, 0);
                    case 1: return new Vector3Int(1, 0, 0);
                    case 2: return new Vector3Int(1, 1, 0);
                    case 3: return new Vector3Int(0, 1, 0);
                }
                break;
        }
        return Vector3Int.zero;
    }

    public GpuMeshData BuildGpu(IVolumeBuffer buffer, MeshingContext context)
    {
        throw new NotSupportedException("GPU voxel meshing not yet implemented.");
    }
}
