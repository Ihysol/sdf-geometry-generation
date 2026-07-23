using Unity.Collections;
using UnityEngine;

public interface IVolumeBuffer
{
    VolumeLayout Layout { get; }
    void UpdateOrigin(Vector3 newOrigin);
    BufferSyncState SyncState { get; set; }

    void EnableComputeBuffers();

    bool HasCpuAccess { get; }
    bool HasGpuAccess { get; }

    NativeArray<float> DensityCpu { get; }
    NativeArray<int> MaterialCpu { get; }

    UnityEngine.GraphicsBuffer DensityGpu { get; }
    UnityEngine.GraphicsBuffer MaterialGpu { get; }

    ComputeBuffer DensityCompute { get; }
    ComputeBuffer MaterialCompute { get; }

    int TotalChunks { get; }
    Vector3Int ChunkGridSize { get; }

    VolumeChunk GetChunk(int cx, int cy, int cz);
    int GetChunkIndex(int cx, int cy, int cz);
    Vector3Int GetChunkCoords(Vector3Int cellIndex);

    void SyncCpuToGpu();
    void SyncGpuToCpu();

    void Dispose();
}
