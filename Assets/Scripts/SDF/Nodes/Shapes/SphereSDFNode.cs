using UnityEngine;

[CreateAssetMenu(menuName = "SDF/Primitives/Sphere")]
public class SphereSDFNode : SDFNode
{
    public float radius = 1.5f;
    public override float Evaluate(Vector3 p)
    {
        return p.magnitude - radius;
    }
}