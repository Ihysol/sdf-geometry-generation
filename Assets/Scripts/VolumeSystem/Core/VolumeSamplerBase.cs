using System;
using UnityEngine;

public abstract class VolumeSamplerBase<TVolume> : MonoBehaviour
    where TVolume : class, IVolumeData
{
    public event Action Changed;

    public TVolume Volume { get; protected set; }
    public bool IsDirty { get; protected set; } = true;

    protected IScalarFieldSource runtimeSource;

    public virtual void SetRuntimeSource(IScalarFieldSource source)
    {
        runtimeSource = source;
        MarkDirty();
    }

    public virtual void ClearRuntimeSource()
    {
        runtimeSource = null;
        MarkDirty();
    }

    public virtual void MarkDirty()
    {
        IsDirty = true;
        Changed?.Invoke();
    }

    public abstract void RebuildVolume(IScalarFieldSource source);
}