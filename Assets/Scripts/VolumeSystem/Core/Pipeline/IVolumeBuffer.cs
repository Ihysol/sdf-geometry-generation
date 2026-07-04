using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;

public interface IVolumeBuffer
{
    VolumeLayout Layout { get; }
    BufferSyncState SyncState { get; }

    bool HasCpuAccess { get; }
    bool HasGpuAccess { get; }

    NativeArray<float> DensityCpu { get; }
    NativeArray<int> MaterialCpu { get; }

    UnityEngine.GraphicsBuffer DensityGpu { get; }
    UnityEngine.GraphicsBuffer MaterialGpu { get; }

    void MarkDirty(BoundsInt region);
    IReadOnlyList<BoundsInt> GetDirtyRegions();
    void ClearDirtyRegions();

    void SyncCpuToGpu();
    void SyncGpuToCpu();

    void Dispose();
}
