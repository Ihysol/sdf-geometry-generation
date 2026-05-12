using UnityEngine;

[CreateAssetMenu(menuName = "SDF/Primitives/Hyperboloid")]
public class HyperboloidSDF : SDF
{
    public float a = 1f;
    public float b = 1f;
    public float c = 1f;

    /// <summary>Evaluates a centered hyperboloid implicit field.</summary>
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
