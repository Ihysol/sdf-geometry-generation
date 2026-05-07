using System;
using UnityEngine;

public abstract class SDF : ScriptableObject, IScalarFieldSource
{
    public event Action Changed;
    public abstract float Evaluate(Vector3 p);

    protected virtual void OnValidate()
    {
        Changed?.Invoke();
    }
}