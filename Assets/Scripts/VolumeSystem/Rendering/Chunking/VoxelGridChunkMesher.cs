using UnityEngine;

public class VoxelGridChunkMesher : IChunkMesher<VoxelGrid>
{
    private readonly DualContouringVoxelMesher _mesher = new();

    public void BuildChunk(
        VolumeModel model,
        IScalarFieldSource source,
        VoxelGrid volume,
        Bounds coreBounds,
        Mesh targetMesh)
    {
        if (volume == null)
            return;

        _mesher.ownedBounds = coreBounds;
        _mesher.BuildMesh(volume, model.isoLevel, targetMesh);
        _mesher.ownedBounds = null;
    }
}
