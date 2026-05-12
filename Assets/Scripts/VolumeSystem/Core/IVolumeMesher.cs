using UnityEngine;

public interface IVolumeMesher<TVolume>
where TVolume : IVolumeData
{
    /// <summary>Converts volume data into mesh buffers at the requested iso level.</summary>
    MeshData BuildMeshData(TVolume volume, float isoLevel);
}
