using System;
using UnityEngine;

public abstract class SDFNode : ScriptableObject, ISDF
{
    public event Action Changed;
    public abstract float Evaluate(Vector3 p);

    protected virtual void OnValidate()
    {
        Changed?.Invoke();
    }
}