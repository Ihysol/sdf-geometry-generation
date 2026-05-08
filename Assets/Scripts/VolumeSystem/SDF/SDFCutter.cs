using UnityEngine;

public abstract class SDFCutter : ScriptableObject
{
    public abstract float Evaluate(Vector3 p, SDF baseShape);
}