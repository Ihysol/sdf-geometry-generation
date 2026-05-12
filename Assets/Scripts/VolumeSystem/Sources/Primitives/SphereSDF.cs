using UnityEngine;

[CreateAssetMenu(menuName = "SDF/Primitives/Sphere")]
public class SphereSDF : SDF
{
    public float radius = 1.5f;
    /// <summary>Evaluates a centered sphere SDF.</summary>
    public override float Evaluate(Vector3 p)
    {
        return p.magnitude - radius;
    }
}
