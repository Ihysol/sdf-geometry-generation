using UnityEngine;

[CreateAssetMenu(menuName = "SDF/Primitives/Pyramid")]
public class PyramidSDF : SDF
{
    public float height = 2f;
    public float halfBase = 1f;

    public override float Evaluate(Vector3 p)
    {
        float safeHeight = Mathf.Max(0.0001f, height);
        float safeHalfBase = Mathf.Max(0.0001f, halfBase);

        // Center pyramid around local origin:
        // base at y = -height / 2
        // tip  at y =  height / 2
        float y = p.y + safeHeight * 0.5f;

        // Radius of square cross-section decreases linearly with height.
        float t = Mathf.Clamp01(y / safeHeight);
        float currentHalfBase = safeHalfBase * (1f - t);

        // Square pyramid side field
        float side = Mathf.Max(Mathf.Abs(p.x), Mathf.Abs(p.z)) - currentHalfBase;

        // Bottom / top limits
        float bottom = -y;
        float top = y - safeHeight;

        return Mathf.Max(side, Mathf.Max(bottom, top));
    }
}