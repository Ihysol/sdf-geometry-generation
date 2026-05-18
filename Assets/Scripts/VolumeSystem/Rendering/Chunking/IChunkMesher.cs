using UnityEngine;

public interface IChunkMesher<TVolume>
    where TVolume : class, IVolumeData
{
    void BuildChunk(
        VolumeModel model,
        IScalarFieldSource source,
        TVolume volume,
        Bounds coreBounds,
        Mesh targetMesh
    );
}
