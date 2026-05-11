using UnityEngine;

public abstract class VolumeBuilderBase<TVolume> : IVolumeBuilder<TVolume>
where TVolume : IVolumeData
{
    public abstract Bounds Bounds { get; }

    public abstract TVolume Build(IScalarFieldSource source);
}

