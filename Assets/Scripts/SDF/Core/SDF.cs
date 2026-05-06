using System;
using UnityEngine;

public abstract class SDF : ScriptableObject, ISDF
{
    public event Action Changed;
    public abstract float Evaluate(Vector3 p);

    protected virtual void OnValidate()
    {
        Changed?.Invoke();
    }
}