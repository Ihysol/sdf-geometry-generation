using UnityEngine;

public interface IVolumeMesher<TVolume>
where TVolume : IVolumeData
{
    /// <summary>Builds the target Unity mesh at the requested iso level.</summary>
    void BuildMesh(TVolume volume, float isoLevel, Mesh targetMesh);
}
