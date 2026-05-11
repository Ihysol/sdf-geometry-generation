using UnityEngine;

public interface IVolumeBuilder<TVolume>
where TVolume : IVolumeData
{
    Bounds Bounds { get; }
    TVolume Build(IScalarFieldSource source);
}