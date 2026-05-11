using UnityEngine;

public interface IVolumeMesher<TVolume>
where TVolume : IVolumeData
{
    MeshData BuildMeshData(TVolume volume, float isoLevel);
}