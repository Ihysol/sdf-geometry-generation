using UnityEngine;

public interface IChunkMesher
{
    bool CanHandle(VolumeModel model, IVolumeData activeVolume);

    void BuildChunk(
        VolumeModel model,
        IScalarFieldSource source,
        Bounds coreBounds,
        Mesh targetMesh
    );
}
