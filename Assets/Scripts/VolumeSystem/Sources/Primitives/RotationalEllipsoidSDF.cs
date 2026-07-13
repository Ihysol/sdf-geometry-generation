using UnityEngine;

[CreateAssetMenu(menuName = "SDF/Primitives/Rotational Ellipsoid")]
public class RotationalEllipsoidSDF : SDF
{
    public float radialRadius = 2f;
    public float verticalRadius = 1f;

    /// <summary>Evaluates a centered ellipsoid of revolution around the Y axis.</summary>
    public override float Evaluate(Vector3 p)
    {
        float safeRadial = Mathf.Max(0.0001f, radialRadius);
        float safeVertical = Mathf.Max(0.0001f, verticalRadius);

        float radial = new Vector2(p.x, p.z).magnitude / safeRadial;
        float y = p.y / safeVertical;
        float normalizedDistance = Mathf.Sqrt(radial * radial + y * y) - 1f;

        return normalizedDistance * Mathf.Min(safeRadial, safeVertical);
    }
}
