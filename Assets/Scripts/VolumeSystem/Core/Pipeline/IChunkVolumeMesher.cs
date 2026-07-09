using UnityEngine;

public interface IChunkVolumeMesher
{
    CpuMeshData BuildChunkCpu(IVolumeBuffer buffer, ChunkCoord coord, MeshingContext context);
}
