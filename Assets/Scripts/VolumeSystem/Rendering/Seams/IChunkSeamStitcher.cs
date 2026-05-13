using UnityEngine;

public interface IChunkSeamStitcher
{
    bool CanHandle(VolumeModel model, IVolumeData activeVolume);

    void RebuildSeams(
        VolumeModel model,
        IScalarFieldSource source,
        Bounds globalBounds,
        Vector3Int chunkCount,
        Mesh seamMesh
    );
}
