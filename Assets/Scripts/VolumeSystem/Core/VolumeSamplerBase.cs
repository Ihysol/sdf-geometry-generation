using System;
using UnityEngine;

public abstract class VolumeSamplerBase<TVolume> : MonoBehaviour
    where TVolume : class, IVolumeData
{
    public event Action Changed;

    public TVolume Volume { get; protected set; }
    public bool IsDirty { get; protected set; } = true;

    protected IScalarFieldSource runtimeSource;

    /// <summary>Sets the runtime source and marks the sampler dirty.</summary>
    public virtual void SetRuntimeSource(IScalarFieldSource source)
    {
        runtimeSource = source;
        MarkDirty();
    }

    /// <summary>Clears the runtime source and marks the sampler dirty.</summary>
    public virtual void ClearRuntimeSource()
    {
        runtimeSource = null;
        MarkDirty();
    }

    /// <summary>Marks the sampled volume as stale and notifies listeners.</summary>
    public virtual void MarkDirty()
    {
        IsDirty = true;
        Changed?.Invoke();
    }

    /// <summary>Rebuilds the sampled volume from the given field source.</summary>
    public abstract void RebuildVolume(IScalarFieldSource source);
}
