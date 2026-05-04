using UnityEngine;

[CreateAssetMenu(menuName = "SDF/Operations/Difference")]
public class DifferenceSDFNode : SDFNode
{
    public SDFNode baseShape;
    public SDFNode toolShape;

    public override float Evaluate(Vector3 p)
    {
        if (baseShape == null)
            return 1f;

        if (toolShape == null)
            return baseShape.Evaluate(p);

        return Mathf.Max(baseShape.Evaluate(p), -toolShape.Evaluate(p));
    }
}