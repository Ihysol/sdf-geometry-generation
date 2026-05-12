using UnityEngine;

[CreateAssetMenu(menuName = "SDF/Primitives/Torus")]
public class TorusSDF : SDF
{
    public float majorRadius = 1.5f;
    public float minorRadius = 0.4f;
    /// <summary>Evaluates a centered torus SDF around the Y axis.</summary>
    public override float Evaluate(Vector3 p)
    {
        Vector2 q = new Vector2(new Vector2(p.x, p.z).magnitude - majorRadius, p.y);
        return q.magnitude - minorRadius;
    }

}
