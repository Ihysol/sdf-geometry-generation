public interface IVolumeOutput
{
    void ApplyCpuMesh(CpuMeshData meshData);
    void ApplyGpuMesh(GpuMeshData meshData);
    void Clear();
}
