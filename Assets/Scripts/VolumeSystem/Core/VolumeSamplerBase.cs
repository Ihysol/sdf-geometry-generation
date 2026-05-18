using System;

public abstract class VolumeSamplerBase<TVolume> : IVolumeSampler
    where TVolume : class, IVolumeData
{
    public event Action Changed;

    public TVolume Volume { get; protected set; }
    IVolumeData IVolumeSampler.Volume => Volume;
    public bool IsDirty { get; protected set; } = true;

    /// <summary>Marks the sampled volume as stale and notifies listeners.</summary>
    public virtual void MarkDirty()
    {
        IsDirty = true;
        Changed?.Invoke();
    }

    /// <summary>Rebuilds the sampled volume from the given field source.</summary>
    public abstract void RebuildVolume(IScalarFieldSource source);
}
