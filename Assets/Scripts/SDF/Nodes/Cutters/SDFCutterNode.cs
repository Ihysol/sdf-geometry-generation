using UnityEngine;

public abstract class SDFCutterNode : ScriptableObject
{
    public abstract float Evaluate(Vector3 p, SDFNode baseShape);
}