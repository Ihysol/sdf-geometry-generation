using System;
using UnityEngine;

[CreateAssetMenu(menuName = "SDF/Operations/Union")]
public class UnionSDFNode : SDFNode
{
    public SDFNode a;
    public SDFNode b;

    public override float Evaluate(Vector3 p)
    {
        if (a == null && b == null) return 1f;
        if (a == null) return b.Evaluate(p);
        if (b == null) return a.Evaluate(p);

        return Mathf.Min(a.Evaluate(p), b.Evaluate(p));
    }
}