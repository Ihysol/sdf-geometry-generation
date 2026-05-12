using System;
using UnityEngine;

public abstract class SDF : ScriptableObject, IScalarFieldSource
{
    public event Action Changed;
    /// <summary>Samples the SDF at a local-space position.</summary>
    public abstract float Evaluate(Vector3 p);

    /// <summary>Notifies listeners when the asset changes in the inspector.</summary>
    protected virtual void OnValidate()
    {
        Changed?.Invoke();
    }
}
