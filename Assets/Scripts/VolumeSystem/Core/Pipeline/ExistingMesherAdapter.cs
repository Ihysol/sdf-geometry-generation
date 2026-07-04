using System;
using Unity.Collections;
using UnityEngine;

public class ExistingMesherAdapter : IVolumeMesher
{
    private readonly VolumeModel _model;

    public bool SupportsCpu => true;
    public bool SupportsGpu => false;

    public ExistingMesherAdapter(VolumeModel model)
    {
        _model = model;
    }

    public CpuMeshData BuildCpu(IVolumeBuffer buffer, MeshingContext context)
    {
        VolumeData volumeData = CollectVolumeData(buffer);
        return BuildMesh(volumeData, context);
    }

    public GpuMeshData BuildGpu(IVolumeBuffer buffer, MeshingContext context)
    {
        throw new NotSupportedException("GPU meshing not yet implemented for existing mesher adapter.");
    }

    private VolumeData CollectVolumeData(IVolumeBuffer buffer)
    {
        VolumeLayout layout = buffer.Layout;
        NativeArray<float> density = buffer.DensityCpu;

        return new VolumeData
        {
            Density = new float[density.Length],
            Resolution = layout.Resolution,
            CellSize = layout.CellSize,
            Origin = layout.Origin
        };
    }

    private CpuMeshData BuildMesh(VolumeData data, MeshingContext context)
    {
        CpuMeshData mesh = new CpuMeshData(Allocator.Temp);

        Vector3Int res = data.Resolution;
        float cellSize = data.CellSize;
        float isoLevel = context.IsoLevel;

        for (int z = 0; z < res.z; z++)
        {
            for (int y = 0; y < res.y; y++)
            {
                for (int x = 0; x < res.x; x++)
                {
                    BuildCell(ref mesh, data.Density, res, cellSize, isoLevel, new Vector3Int(x, y, z), context.GenerateNormals);
                }
            }
        }

        return mesh;
    }

    private static void BuildCell(ref CpuMeshData mesh, float[] density, Vector3Int res, float cellSize, float isoLevel, Vector3Int index, bool generateNormals)
    {
        int ix = index.x, iy = index.y, iz = index.z;

        for (int face = 0; face < 6; face++)
        {
            int nx = ix + FaceDx[face];
            int ny = iy + FaceDy[face];
            int nz = iz + FaceDz[face];

            if (nx < 0 || nx >= res.x || ny < 0 || ny >= res.y || nz < 0 || nz >= res.z)
                continue;

            float cellVal = density[ix + res.x * (iy + res.y * iz)];
            float neighborVal = density[nx + res.x * (ny + res.y * nz)];

            bool cellInside = cellVal > isoLevel;
            bool neighborInside = neighborVal > isoLevel;

            if (cellInside == neighborInside)
                continue;

            int baseVert = mesh.Vertices.Length;

            for (int vi = 0; vi < 4; vi++)
            {
                int cx = ix + CornerDx[face, vi];
                int cy = iy + CornerDy[face, vi];
                int cz = iz + CornerDz[face, vi];

                float cornerVal = density[cx + res.x * (cy + res.y * cz)];
                float t = (isoLevel - cellVal) / (neighborVal - cellVal);
                t = Mathf.Clamp01(t);

                Vector3 pos = new Vector3(cx + 0.5f, cy + 0.5f, cz + 0.5f) * cellSize;
                pos += new Vector3(FaceDx[face], FaceDy[face], FaceDz[face]) * cellSize * t;

                mesh.Vertices.Add(pos);
                if (generateNormals)
                    mesh.Normals.Add(new Vector3(FaceDx[face], FaceDy[face], FaceDz[face]));
            }

            mesh.Indices.Add(baseVert);
            mesh.Indices.Add(baseVert + 1);
            mesh.Indices.Add(baseVert + 2);
            mesh.Indices.Add(baseVert);
            mesh.Indices.Add(baseVert + 2);
            mesh.Indices.Add(baseVert + 3);
        }
    }

    private static readonly int[] FaceDx = { 1, -1, 0, 0, 0, 0 };
    private static readonly int[] FaceDy = { 0, 0, 1, -1, 0, 0 };
    private static readonly int[] FaceDz = { 0, 0, 0, 0, 1, -1 };

    private static readonly int[,] CornerDx = {
        { 1, 1, 1, 1 }, { 0, 0, 0, 0 }, { 0, 1, 1, 0 }, { 0, 1, 1, 0 }, { 0, 1, 1, 0 }, { 0, 1, 1, 0 }
    };
    private static readonly int[,] CornerDy = {
        { 0, 1, 1, 0 }, { 0, 1, 1, 0 }, { 1, 1, 1, 1 }, { 0, 0, 0, 0 }, { 0, 1, 1, 0 }, { 0, 1, 1, 0 }
    };
    private static readonly int[,] CornerDz = {
        { 0, 0, 1, 1 }, { 0, 0, 1, 1 }, { 0, 0, 1, 1 }, { 0, 0, 1, 1 }, { 1, 1, 1, 1 }, { 0, 0, 0, 0 }
    };

    private struct VolumeData
    {
        public float[] Density;
        public Vector3Int Resolution;
        public float CellSize;
        public Vector3 Origin;
    }
}
