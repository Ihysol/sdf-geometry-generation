using UnityEngine;

public interface IChunkMesher<TVolume>
    where TVolume : class, IVolumeData
{
    void BuildChunk(
        VolumeProcessor model,
        IScalarFieldSource source,
        TVolume volume,
        Bounds coreBounds,
        Mesh targetMesh
    );
}
