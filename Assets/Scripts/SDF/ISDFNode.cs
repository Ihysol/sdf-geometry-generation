using UnityEngine;

public abstract class SDFNode : ScriptableObject, ISDF
{
    public abstract float Evaluate(Vector3 p);
}