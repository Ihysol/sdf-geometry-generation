using UnityEngine;

public class VoxelGridChunkSeamStitcher : IChunkSeamStitcher
{
    public bool CanHandle(VolumeModel model, IVolumeData activeVolume)
    {
        return model != null && model.dataStructure == VolumeDataStructure.VoxelGrid;
    }

    public void RebuildSeams(
        VolumeModel model,
        IScalarFieldSource source,
        Bounds globalBounds,
        Vector3Int chunkCount,
        Mesh seamMesh)
    {
        seamMesh.Clear();
    }
}
