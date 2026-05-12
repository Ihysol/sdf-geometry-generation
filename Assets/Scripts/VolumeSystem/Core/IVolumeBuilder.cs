using UnityEngine;

public interface IVolumeBuilder<TVolume>
where TVolume : IVolumeData
{
    Bounds Bounds { get; }

    /// <summary>Builds volume data by sampling the given scalar field source.</summary>
    TVolume Build(IScalarFieldSource source);
}
