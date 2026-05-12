using UnityEngine;

public abstract class SDFCutter : ScriptableObject
{
    /// <summary>Evaluates a cutter SDF against a base shape.</summary>
    public abstract float Evaluate(Vector3 p, SDF baseShape);
}
