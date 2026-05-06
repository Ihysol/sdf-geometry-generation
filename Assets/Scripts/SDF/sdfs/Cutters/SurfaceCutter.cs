using UnityEngine;

[CreateAssetMenu(menuName = "SDF/Operations/Surface Cut")]
public class SurfaceCutSDF : SDF
{
    public SDF baseShape;
    public SDFCutter cutter;

    public override float Evaluate(Vector3 p)
    {
        if (baseShape == null)
            return 1f;

        float baseD = baseShape.Evaluate(p);

        if (cutter == null)
            return baseD;

        float cutterD = cutter.Evaluate(p, baseShape);

        return Mathf.Max(baseD, -cutterD);
    }
}