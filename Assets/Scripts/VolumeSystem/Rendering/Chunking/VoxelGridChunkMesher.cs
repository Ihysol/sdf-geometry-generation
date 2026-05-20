using UnityEngine;

public class VoxelGridChunkMesher : IChunkMesher<VoxelGrid>
{
    private readonly DualContouringVoxelMesher _mesher = new();
    private readonly DualMarchingCubesVoxelMesher _dualMarchingCubesMesher = new();
    private readonly DualMarchingTetrahedraVoxelMesher _dualMarchingTetrahedraMesher = new();
    private readonly SurfaceNetsVoxelMesher _surfaceNetsMesher = new();

    public void BuildChunk(
        VolumeModel model,
        IScalarFieldSource source,
        VoxelGrid volume,
        Bounds coreBounds,
        Mesh targetMesh)
    {
        if (volume == null)
            return;

        switch (model.octreeMesherType)
        {
            case OctreeMesherType.DualMarchingCubes:
                _dualMarchingCubesMesher.ownedBounds = coreBounds;
                _dualMarchingCubesMesher.BuildMesh(volume, model.isoLevel, targetMesh);
                _dualMarchingCubesMesher.ownedBounds = null;
                break;
            case OctreeMesherType.DualMarchingTetrahedra:
                _dualMarchingTetrahedraMesher.ownedBounds = coreBounds;
                _dualMarchingTetrahedraMesher.BuildMesh(volume, model.isoLevel, targetMesh);
                _dualMarchingTetrahedraMesher.ownedBounds = null;
                break;
            case OctreeMesherType.SurfaceNets:
                _surfaceNetsMesher.ownedBounds = coreBounds;
                _surfaceNetsMesher.BuildMesh(volume, model.isoLevel, targetMesh);
                _surfaceNetsMesher.ownedBounds = null;
                break;
            case OctreeMesherType.DualContouring:
            default:
                _mesher.ownedBounds = coreBounds;
                _mesher.BuildMesh(volume, model.isoLevel, targetMesh);
                _mesher.ownedBounds = null;
                break;
        }
    }
}
