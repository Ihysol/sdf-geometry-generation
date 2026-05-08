using UnityEngine;

public interface IVolumeMesher<TVolume>
{
    MeshData BuildMeshData(TVolume volume, float isoLevel);
}