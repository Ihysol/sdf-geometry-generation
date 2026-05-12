using UnityEngine;

public abstract class VolumeBuilderBase<TVolume> : IVolumeBuilder<TVolume>
where TVolume : IVolumeData
{
    public abstract Bounds Bounds { get; }

    /// <summary>Builds volume data by sampling the given scalar field source.</summary>
    public abstract TVolume Build(IScalarFieldSource source);
}

