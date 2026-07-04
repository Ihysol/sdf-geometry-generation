using UnityEngine;

public interface IVolumeMesher
{
    bool SupportsCpu { get; }
    bool SupportsGpu { get; }

    CpuMeshData BuildCpu(IVolumeBuffer buffer, MeshingContext context);
    GpuMeshData BuildGpu(IVolumeBuffer buffer, MeshingContext context);
}
