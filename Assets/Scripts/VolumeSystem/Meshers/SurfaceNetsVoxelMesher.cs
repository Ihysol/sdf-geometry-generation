using UnityEngine;

public class SurfaceNetsVoxelMesher : IVolumeMesher<VoxelGrid>
{
    private readonly DualContouringVoxelMesher _backend = new();
    public Bounds? ownedBounds;

    public void BuildMesh(VoxelGrid volume, float isoLevel, Mesh targetMesh)
    {
        _backend.ownedBounds = ownedBounds;
        _backend.BuildMesh(volume, isoLevel, targetMesh);
        _backend.ownedBounds = null;
    }
}
