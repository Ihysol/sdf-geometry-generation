using System;
using Unity.AppUI.UI;
using UnityEngine;

[CreateAssetMenu(menuName = "SDF/Primitives/Hyperboloid")]
public class HyperboloidSDFNode : SDFNode
{
    public float a = 1f;
    public float b = 1f;
    public float c = 1f;

    public override float Evaluate(Vector3 p)
    {
        float safeA = Mathf.Max(0.0001f, a);
        float safeB = Mathf.Max(0.0001f, b);
        float safeC = Mathf.Max(0.0001f, c);

        return
            (p.x * p.x) / (safeA * safeA) +
            (p.z * p.z) / (safeB * safeB) -
            (p.y * p.y) / (safeC * safeC) -
            1f;

    }
}