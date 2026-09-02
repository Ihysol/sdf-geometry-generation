using UnityEngine;

public interface IVolumeMesher
{
    bool SupportsCpu { get; }
    bool SupportsGpu { get; }

    CpuMeshData BuildCpu(IVolumeBuffer buffer, MeshingContext context);
    GpuMeshData BuildGpu(IVolumeBuffer buffer, MeshingContext context);

    /// <summary>
    /// ADR-019: The maximum number of cells a chunk mesher may read <em>outside</em> a
    /// chunk's cell region on any axis during <c>BuildChunkCpu</c>. The partial-rebuild
    /// sampler uses this to size the resample halo so every remeshed chunk sees only
    /// fresh SDF values.
    ///
    /// Contract: the declared value must be <em>always sufficient</em> for this mesher's
    /// actual read pattern — never merely typical. If you extend a mesher's reads (e.g. a
    /// farther corner probe), raise the value. Default is 2 (the DualContouring trailing-halo
    /// + far-corner read), a safe upper bound; meshers that provably read less override it
    /// to avoid wasted sampling.
    /// </summary>
    int ReadHaloCells { get => 2; }
}
